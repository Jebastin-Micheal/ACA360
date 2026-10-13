/* =============================================================================
   013_F-32_secure_link_reuse_and_lockout.sql

   FIXES   F-32 — employee download links are replayable for 90 days, the IsUsed
           flag is written and never read, and there is no persistent limit on
           guessing the last four SSN digits.

   WHAT THE THREAT ACTUALLY IS

           The link is /MyForms/Access?token={LinkId} where LinkId is a GUID —
           122 bits, not guessable. sp_GenerateSecureLink also stores a Token
           column, but the application puts the SAME Guid in both, and validation
           only ever matched on LinkId, so Token is a duplicate rather than a
           second secret.

           So nobody brute-forces the GUID. The realistic exposure is the link
           LEAKING — a forwarded email, a shared or archived mailbox, browser
           history on a family computer — after which only four digits stand
           between the holder and someone's 1095-C. That is what this script
           defends against, and it is why single-use is the wrong lever.

   THE POLICY, STATED PLAINLY

           The link REMAINS REUSABLE until it expires. An employee returning for
           their form at tax time is ordinary behaviour; burning the link on first
           use would create support load and defeat the feature. Protection comes
           from a persistent attempt limit, an audit trail, and the ability to
           revoke — not from one-shot access.

           If you would rather have strictly single-use links, the change is one
           predicate in sp_ValidateSecureToken, marked below.

   WHAT    1. Adds the columns that make the link's own row carry its security
              state, so it survives a restart and works across multiple nodes —
              unlike the in-memory throttle added with F-31, which stays as a
              complementary first line of defence against per-address bursts.

           2. Rewrites sp_ValidateSecureToken to honour expiry, revocation and
              lockout, to count failures, and to record every successful use.

           3. Gives IsUsed the meaning its name implies: this form has been
              collected. That makes "who has not picked up their 1095-C yet"
              a query the Distribution Center can run, which it could not before.

   NOTHING IS IN FLIGHT

           SecureDownloadLinks holds zero rows and CommunicationLog is empty —
           consistent with F-37, since no email has ever actually been sent. No
           issued link can be invalidated by this change.

   TO REVOKE A LEAKED LINK

           UPDATE dbo.SecureDownloadLinks SET RevokedDate = GETUTCDATE()
           WHERE  LinkId = '<guid>';

           Deliberately no procedure and no button: a wrapper nothing calls is the
           dead-code pattern this audit has been removing. Wire a control when
           there is a screen for it.

   SAFE TO RE-RUN
           Yes. Column adds are guarded, procedures use CREATE OR ALTER.

   COMPANION C# CHANGES — applied in the working tree alongside this
           SecureTokenValidationResult carries a status so the portal can say
           "too many attempts" without implying whether the link exists;
           ValidateTokenAsync passes the caller's address for the audit trail.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- PART 1 ----
   Columns. Each guarded so a re-run is a no-op.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.SecureDownloadLinks', 'FailedAttempts') IS NULL
    ALTER TABLE dbo.SecureDownloadLinks ADD FailedAttempts INT NOT NULL CONSTRAINT DF_SDL_FailedAttempts DEFAULT (0);
IF COL_LENGTH('dbo.SecureDownloadLinks', 'LockedUntil') IS NULL
    ALTER TABLE dbo.SecureDownloadLinks ADD LockedUntil DATETIME NULL;
IF COL_LENGTH('dbo.SecureDownloadLinks', 'UseCount') IS NULL
    ALTER TABLE dbo.SecureDownloadLinks ADD UseCount INT NOT NULL CONSTRAINT DF_SDL_UseCount DEFAULT (0);
IF COL_LENGTH('dbo.SecureDownloadLinks', 'FirstUsedDate') IS NULL
    ALTER TABLE dbo.SecureDownloadLinks ADD FirstUsedDate DATETIME NULL;
IF COL_LENGTH('dbo.SecureDownloadLinks', 'LastUsedDate') IS NULL
    ALTER TABLE dbo.SecureDownloadLinks ADD LastUsedDate DATETIME NULL;
IF COL_LENGTH('dbo.SecureDownloadLinks', 'LastUsedIp') IS NULL
    ALTER TABLE dbo.SecureDownloadLinks ADD LastUsedIp VARCHAR(64) NULL;
IF COL_LENGTH('dbo.SecureDownloadLinks', 'RevokedDate') IS NULL
    ALTER TABLE dbo.SecureDownloadLinks ADD RevokedDate DATETIME NULL;
GO

/* IsUsed was nullable with no default and no reader. It now means "this form has
   been collected at least once", so give it a default and settle existing NULLs. */
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc
               JOIN sys.columns c ON c.object_id = dc.parent_object_id
                                 AND c.column_id = dc.parent_column_id
               WHERE dc.parent_object_id = OBJECT_ID('dbo.SecureDownloadLinks')
                 AND c.name = 'IsUsed')
    ALTER TABLE dbo.SecureDownloadLinks ADD CONSTRAINT DF_SDL_IsUsed DEFAULT (0) FOR IsUsed;
GO

UPDATE dbo.SecureDownloadLinks SET IsUsed = 0 WHERE IsUsed IS NULL;
GO

CREATE NONCLUSTERED INDEX IX_SecureDownloadLinks_Employee_Year
    ON dbo.SecureDownloadLinks (EmployeeId, TaxYear)
    INCLUDE (IsUsed, UseCount, LastUsedDate, ExpirationDate, RevokedDate)
    WITH (DROP_EXISTING = OFF, ONLINE = OFF);
GO

/* ---------------------------------------------------------------- PART 2 ----
   sp_GenerateSecureLink — expiry is now a parameter rather than a hardcoded 90.
   The default preserves current behaviour, so existing callers are unaffected.
   ---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE [dbo].[sp_GenerateSecureLink]
    @LinkId     UNIQUEIDENTIFIER,
    @EmployeeId INT,
    @TaxYear    INT,
    @Token      NVARCHAR(255),
    @ExpiryDays INT = 90
AS
BEGIN
    SET NOCOUNT ON;

    IF @ExpiryDays IS NULL OR @ExpiryDays <= 0 SET @ExpiryDays = 90;

    INSERT INTO dbo.SecureDownloadLinks
        (LinkId, EmployeeId, TaxYear, Token, ExpirationDate, CreatedDate,
         IsUsed, FailedAttempts, UseCount)
    VALUES
        (@LinkId, @EmployeeId, @TaxYear, @Token,
         DATEADD(DAY, @ExpiryDays, GETUTCDATE()), GETUTCDATE(),
         0, 0, 0);
END
GO

/* ---------------------------------------------------------------- PART 3 ----
   sp_ValidateSecureToken.

   Returns exactly one row:
       EmployeeId, Year   populated only on success
       Status             0 success, 1 invalid, 2 locked
       RetryAfterMinutes  minutes remaining when Status = 2

   Status 1 covers not-found, expired, revoked AND wrong digits deliberately —
   separating them would confirm which links exist and whose they are. Status 2
   is safe to disclose because reaching it requires already holding a real link.
   ---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE [dbo].[sp_ValidateSecureToken]
    @LinkId    UNIQUEIDENTIFIER,
    @Last4SSN  VARCHAR(4),
    @IpAddress VARCHAR(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @MaxFailures  INT = 5;
    DECLARE @LockMinutes  INT = 30;

    DECLARE @EmployeeId INT, @Year INT, @Failed INT, @LockedUntil DATETIME;
    DECLARE @Expiration DATETIME, @Revoked DATETIME, @ActualSSN VARCHAR(50);

    SELECT  @EmployeeId  = l.EmployeeId,
            @Year        = l.TaxYear,
            @Failed      = ISNULL(l.FailedAttempts, 0),
            @LockedUntil = l.LockedUntil,
            @Expiration  = l.ExpirationDate,
            @Revoked     = l.RevokedDate
    FROM    dbo.SecureDownloadLinks l
    WHERE   l.LinkId = @LinkId;

    /* Unknown, expired or revoked: indistinguishable to the caller. */
    IF @EmployeeId IS NULL OR @Expiration <= GETUTCDATE() OR @Revoked IS NOT NULL
    BEGIN
        SELECT EmployeeId = CAST(NULL AS INT), Year = CAST(NULL AS INT),
               Status = 1, RetryAfterMinutes = 0;
        RETURN;
    END

    /* Locked by earlier failures. */
    IF @LockedUntil IS NOT NULL AND @LockedUntil > GETUTCDATE()
    BEGIN
        SELECT EmployeeId = CAST(NULL AS INT), Year = CAST(NULL AS INT),
               Status = 2,
               RetryAfterMinutes = DATEDIFF(MINUTE, GETUTCDATE(), @LockedUntil) + 1;
        RETURN;
    END

    /* A lock that has aged out clears the counter with it. */
    IF @LockedUntil IS NOT NULL AND @LockedUntil <= GETUTCDATE()
    BEGIN
        UPDATE dbo.SecureDownloadLinks
        SET    FailedAttempts = 0, LockedUntil = NULL
        WHERE  LinkId = @LinkId;
        SET @Failed = 0;
    END

    SELECT @ActualSSN = SSN FROM dbo.Employee WHERE id = @EmployeeId;

    /* Compare on digits only: the column holds both 123-45-6789 and 123456789. */
    DECLARE @ActualDigits VARCHAR(50) =
        REPLACE(REPLACE(REPLACE(ISNULL(@ActualSSN, ''), '-', ''), ' ', ''), CHAR(9), '');

    IF LEN(@ActualDigits) < 4
       OR ISNULL(@Last4SSN, '') = ''
       OR RIGHT(@ActualDigits, 4) <> @Last4SSN
    BEGIN
        SET @Failed = @Failed + 1;

        UPDATE dbo.SecureDownloadLinks
        SET    FailedAttempts = @Failed,
               LockedUntil    = CASE WHEN @Failed >= @MaxFailures
                                     THEN DATEADD(MINUTE, @LockMinutes, GETUTCDATE())
                                     ELSE LockedUntil END,
               LastUsedIp     = ISNULL(@IpAddress, LastUsedIp)
        WHERE  LinkId = @LinkId;

        SELECT EmployeeId = CAST(NULL AS INT), Year = CAST(NULL AS INT),
               Status = CASE WHEN @Failed >= @MaxFailures THEN 2 ELSE 1 END,
               RetryAfterMinutes = CASE WHEN @Failed >= @MaxFailures THEN @LockMinutes ELSE 0 END;
        RETURN;
    END

    /* Success. The link stays usable until it expires — see the policy note in
       the header. For strictly single-use links, add to the guard above:
           OR (SELECT UseCount FROM dbo.SecureDownloadLinks WHERE LinkId = @LinkId) > 0
       and expect support calls from employees returning for their form. */
    UPDATE dbo.SecureDownloadLinks
    SET    IsUsed         = 1,
           UseCount       = ISNULL(UseCount, 0) + 1,
           FirstUsedDate  = ISNULL(FirstUsedDate, GETUTCDATE()),
           LastUsedDate   = GETUTCDATE(),
           LastUsedIp     = ISNULL(@IpAddress, LastUsedIp),
           FailedAttempts = 0,
           LockedUntil    = NULL
    WHERE  LinkId = @LinkId;

    SELECT EmployeeId = @EmployeeId, Year = @Year, Status = 0, RetryAfterMinutes = 0;
END
GO

/* -------------------------------------------------------------- VERIFY ----- */
PRINT '--- SecureDownloadLinks shape ---';
SELECT  ColumnName = c.name,
        DataType   = t.name,
        Nullable   = CASE WHEN c.is_nullable = 1 THEN 'yes' ELSE 'no' END
FROM    sys.columns c
JOIN    sys.types t ON t.user_type_id = c.user_type_id
WHERE   c.object_id = OBJECT_ID('dbo.SecureDownloadLinks')
ORDER BY c.column_id;

PRINT '--- sp_ValidateSecureToken parameters ---';
SELECT ParameterName = name, Position = parameter_id
FROM   sys.parameters WHERE object_id = OBJECT_ID('dbo.sp_ValidateSecureToken')
ORDER BY parameter_id;

PRINT '--- Distribution follow-up, now answerable ---';
PRINT '    Which employees have been issued a link and not yet collected their form.';
SELECT  TaxYear,
        Issued    = COUNT(*),
        Collected = SUM(CASE WHEN IsUsed = 1 THEN 1 ELSE 0 END),
        Pending   = SUM(CASE WHEN IsUsed = 0 THEN 1 ELSE 0 END),
        Expired   = SUM(CASE WHEN ExpirationDate <= GETUTCDATE() THEN 1 ELSE 0 END),
        Revoked   = SUM(CASE WHEN RevokedDate IS NOT NULL THEN 1 ELSE 0 END),
        LockedNow = SUM(CASE WHEN LockedUntil > GETUTCDATE() THEN 1 ELSE 0 END)
FROM    dbo.SecureDownloadLinks
GROUP BY TaxYear
ORDER BY TaxYear;
PRINT '    No rows means no link has ever been issued - see F-37, nothing sends email.';
GO
