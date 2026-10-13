/* =============================================================================
   021_portal_security_and_schema_gaps.sql

   Three independent fixes found reviewing the 22_09_2026 snapshot. Each is safe
   to run alone; together they close out everything from that review except the
   plaintext TempPassword issue, which is a C# change (see the message alongside
   this script -- there is nothing for the database to do there, since
   TempPassword is an opaque nvarchar(255) that can hold a bcrypt hash exactly
   as it already holds plaintext, with no schema change needed).

   ---------------------------------------------------------------------------
   PART 1   Backfill two orphaned Premium rows.

            Premium ids 40 and 44 have FileLogId and EmployerId both NULL, even
            though their parent EmployerPlan rows (23 and 4) have both values
            set. This is exactly the gap script 018's one-time backfill was
            written to close; these two rows are consistent with that backfill
            never having been run, or having run before these two rows existed.
            Safe to re-run: only touches rows still NULL.

   PART 2   Add the two missing foreign keys.

            EmployeeConsent.EmployeeId and EmployeePortalUser.EmployeeId have no
            FK to Employee.id, unlike SecureDownloadLinks.EmployeeId -- an
            existing table doing the same job (an employee-facing link/token)
            -- which does. Verified first: every EmployeeId value currently in
            both tables already exists in Employee, so this adds real integrity
            protection going forward with zero data cleanup required.

   PART 3   Enforce the account lockout the schema already tracks for.

            EmployeePortalUser.FailedLoginCount is incremented on every failed
            login (sp_IncrementPortalFailedLogin) and reset on success, but
            nothing anywhere checks it against a threshold. This adds that
            check to the same procedure, flipping IsActive off once the count
            reaches 5.

            ONE APP-SIDE EFFECT, NO APP-SIDE CHANGE NEEDED: locking this way
            reuses IsActive, which EmployeePortalAuthService.ValidateLoginAsync
            already checks first ("if (!user.IsActive) return InactiveAccount").
            The tradeoff is that a self-locked account and an admin-deactivated
            account now show the same message to the employee. Re-running
            sp_ActivatePortalUser for that employee clears it (it sets
            IsActive = 1 and FailedLoginCount = 0 as part of its normal upsert),
            so an admin unlock is just "Activate" again -- no new admin action
            needed. If you want the two states distinguished in the UI later,
            that needs a small C# change (a new PortalLoginStatus value); this
            script does not do that.

            5 attempts is this script's default. Change @LockoutThreshold below
            before running if you want a different number.

   SAFE TO RE-RUN: yes, throughout.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* =============================================================================
   PART 1 -- Backfill orphaned Premium rows
   ============================================================================= */

UPDATE  p
SET     p.FileLogId  = pl.FileLogId,
        p.EmployerId = pl.employerId
FROM    dbo.Premium p
JOIN    dbo.EmployerPlan pl ON pl.id = p.EmployerPlanId
WHERE   p.FileLogId IS NULL
  AND   pl.FileLogId IS NOT NULL;

PRINT '--- Part 1: backfilled ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' Premium row(s).';

SELECT StillNull = COUNT(*) FROM dbo.Premium WHERE FileLogId IS NULL;
PRINT '    Any row still NULL belongs to an EmployerPlan that itself has no';
PRINT '    FileLogId -- that one is unrecoverable from this data alone.';
GO

/* =============================================================================
   PART 2 -- Add the missing foreign keys
   ============================================================================= */

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_EmployeeConsent_Employee')
BEGIN
    ALTER TABLE dbo.EmployeeConsent WITH CHECK
        ADD CONSTRAINT FK_EmployeeConsent_Employee
        FOREIGN KEY (EmployeeId) REFERENCES dbo.Employee(id);
    PRINT '--- Part 2: added FK_EmployeeConsent_Employee.';
END
ELSE
    PRINT '--- Part 2: FK_EmployeeConsent_Employee already exists, skipped.';

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_EmployeePortalUser_Employee')
BEGIN
    ALTER TABLE dbo.EmployeePortalUser WITH CHECK
        ADD CONSTRAINT FK_EmployeePortalUser_Employee
        FOREIGN KEY (EmployeeId) REFERENCES dbo.Employee(id);
    PRINT '--- Part 2: added FK_EmployeePortalUser_Employee.';
END
ELSE
    PRINT '--- Part 2: FK_EmployeePortalUser_Employee already exists, skipped.';
GO

/* =============================================================================
   PART 3 -- Enforce the failed-login lockout
   ============================================================================= */

CREATE OR ALTER PROCEDURE [dbo].[sp_IncrementPortalFailedLogin]
    @Email NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LockoutThreshold INT = 5;

    UPDATE dbo.EmployeePortalUser
       SET FailedLoginCount = FailedLoginCount + 1,
           IsActive = CASE WHEN FailedLoginCount + 1 >= @LockoutThreshold THEN 0 ELSE IsActive END
     WHERE Email = @Email;
END
GO

/* -------------------------------------------------------------- VERIFY ----- */

PRINT '';
PRINT '=== 1. No more orphaned Premium rows (or none recoverable from this data) ===';
SELECT  Total = COUNT(*),
        StillNull = SUM(CASE WHEN FileLogId IS NULL THEN 1 ELSE 0 END)
FROM    dbo.Premium;

PRINT '=== 2. Both foreign keys are in place ===';
SELECT  name, OBJECT_NAME(parent_object_id) AS TableName
FROM    sys.foreign_keys
WHERE   name IN ('FK_EmployeeConsent_Employee', 'FK_EmployeePortalUser_Employee');

PRINT '=== 3. Lockout is wired up ===';
SELECT Result = CASE
    WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_IncrementPortalFailedLogin')) LIKE '%LockoutThreshold%'
    THEN 'PASS' ELSE 'FAIL' END;

PRINT '=== 4. Current portal accounts, for a sanity check ===';
SELECT  Id, EmployeeId, Email, IsActive, RequirePasswordReset, FailedLoginCount,
        HasPasswordHash = CASE WHEN PasswordHash IS NOT NULL THEN 1 ELSE 0 END,
        HasTempPassword = CASE WHEN TempPassword IS NOT NULL THEN 1 ELSE 0 END
FROM    dbo.EmployeePortalUser
ORDER BY Id;
GO
