/* =============================================================================
   061 - Acknowledged warnings

   Skipping a warning in Error Triage becomes "acknowledging" it, with a reason
   (required by the app). The acknowledgement is kept for the employer and filing
   year, not just for the file: when a later file for the same employer EIN and
   year raises the same warning (same rule and column) on the same subject (the
   same employee SSN, dependent, plan, premium or employer), validation
   acknowledges it again, with the reason, and says which file it came from.

   - StagingIssueSkips: Reason, CarriedFromFileLogId.
   - dbo.IssueAcknowledgements: the acknowledgements kept across files.
   - dbo.fn_StagingRowSubject: what a staging row is about (the subject key).
   - sp_UpdateStagingRowErrorStatus: takes @Reason; acknowledging records it in
     both tables, un-acknowledging removes it from both.
   - sp_Validation_ReapplySkips: after the file's own skips, carries the
     employer's earlier acknowledgements over.
   - sp_GetIssueSkipReasons: a file's acknowledgements with their reasons.

   Only warnings and information notes can be acknowledged (as in 056).
   Also: the status is stored as 'Skipped' whatever its case (the app sent
   'SKIPPED', and the list did not show those as skipped); stored ones repaired.
   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.StagingIssueSkips', 'Reason') IS NULL
    ALTER TABLE dbo.StagingIssueSkips ADD Reason NVARCHAR(500) NULL;
GO
IF COL_LENGTH('dbo.StagingIssueSkips', 'CarriedFromFileLogId') IS NULL
    ALTER TABLE dbo.StagingIssueSkips ADD CarriedFromFileLogId INT NULL;
GO

IF OBJECT_ID('dbo.IssueAcknowledgements') IS NULL
BEGIN
    CREATE TABLE dbo.IssueAcknowledgements
    (
        IssueAcknowledgementId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IssueAcknowledgements PRIMARY KEY,
        Ein              VARCHAR(20)    NOT NULL,   -- the file's employer EIN, digits only
        PlanYear         INT            NOT NULL,
        StagingTableName NVARCHAR(128)  NOT NULL,
        SubjectKey       NVARCHAR(400)  NOT NULL,   -- see dbo.fn_StagingRowSubject
        RuleId           INT            NOT NULL,
        ColumnName       NVARCHAR(128)  NOT NULL CONSTRAINT DF_IssueAcknowledgements_Column DEFAULT N'',
        Reason           NVARCHAR(500)  NOT NULL,
        AcknowledgedBy   NVARCHAR(256)  NULL,
        AcknowledgedAt   DATETIME2(0)   NOT NULL CONSTRAINT DF_IssueAcknowledgements_At DEFAULT SYSUTCDATETIME(),
        FileLogId        INT            NOT NULL,   -- the file it was acknowledged in
        CONSTRAINT UQ_IssueAcknowledgements UNIQUE (Ein, PlanYear, StagingTableName, SubjectKey, RuleId, ColumnName)
    );
END
GO

-- Issues saved as 'SKIPPED' before this script: stored the way the app reads them.
UPDATE dbo.StagingRowErrors SET Status = 'Skipped' WHERE Status = 'Skipped' AND Status COLLATE Latin1_General_CS_AS <> 'Skipped';
GO

------------------------------------------------------------
-- What a staging row is about, the same in every file of an employer:
-- employee = SSN digits; dependent = employee SSN + dependent SSN (or name and
-- birth date); plan = name; premium = plan, start date and band; employer = EIN.
------------------------------------------------------------
CREATE OR ALTER FUNCTION dbo.fn_StagingRowSubject (@Table NVARCHAR(128), @RowId BIGINT)
RETURNS TABLE
AS RETURN
(
    SELECT N'E:' + REPLACE(REPLACE(ISNULL(S.EmployeeSSN, N''), N'-', N''), N' ', N'') AS SubjectKey
    FROM dbo.Staging_Employees S WHERE @Table = N'Staging_Employees' AND S.StagingEmployeeId = @RowId
    UNION ALL
    SELECT N'D:' + REPLACE(REPLACE(ISNULL(S.EmployeeSSN, N''), N'-', N''), N' ', N'') + N':'
           + COALESCE(NULLIF(REPLACE(REPLACE(ISNULL(S.DependentSSN, N''), N'-', N''), N' ', N''), N''),
                      UPPER(LTRIM(RTRIM(CONCAT(S.DependentLegalFirstName, N' ', S.DependentLegalLastName, N' ', S.DependentBirthdate)))))
    FROM dbo.Staging_Dependents S WHERE @Table = N'Staging_Dependents' AND S.StagingDependentId = @RowId
    UNION ALL
    SELECT N'P:' + UPPER(LTRIM(RTRIM(ISNULL(S.PlanName, N''))))
    FROM dbo.Staging_Plans S WHERE @Table = N'Staging_Plans' AND S.StagingPlanId = @RowId
    UNION ALL
    SELECT N'R:' + UPPER(LTRIM(RTRIM(ISNULL(S.PlanName, N'')))) + N':' + LTRIM(RTRIM(ISNULL(S.StartDate, N''))) + N':' + LTRIM(RTRIM(ISNULL(S.Start, N'')))
    FROM dbo.Staging_Premiums S WHERE @Table = N'Staging_Premiums' AND S.StagingPremiumId = @RowId
    UNION ALL
    SELECT N'ER:' + REPLACE(REPLACE(COALESCE(NULLIF(LTRIM(RTRIM(S.AffiliatedEIN)), N''), S.PrimaryEIN, N''), N'-', N''), N' ', N'')
    FROM dbo.Staging_Employers S WHERE @Table = N'Staging_Employers' AND S.StagingEmployerId = @RowId
);
GO

------------------------------------------------------------
-- Acknowledge (skip) or un-acknowledge one issue, through its file.
------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_UpdateStagingRowErrorStatus
    @StagingRowErrorId BIGINT,
    @Status            NVARCHAR(20),
    @FileLogId         INT           = NULL,
    @ChangedBy         NVARCHAR(256) = NULL,
    @Reason            NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @F INT, @T NVARCHAR(128), @R BIGINT, @Rule INT, @C NVARCHAR(128), @Severity NVARCHAR(50);

    SELECT @F = E.FileLogId, @T = E.StagingTableName, @R = E.StagingRowId, @Rule = E.RuleId,
           @C = ISNULL(E.ColumnName, N''), @Severity = R.Severity
    FROM   dbo.StagingRowErrors E
    LEFT   JOIN dbo.ValidationRules R ON R.RuleId = E.RuleId
    WHERE  E.StagingRowErrorId = @StagingRowErrorId
      AND  (@FileLogId IS NULL OR E.FileLogId = @FileLogId);

    IF @F IS NULL OR (@Status = 'Skipped' AND ISNULL(@Severity, 'Error') NOT IN ('Warning', 'Information', 'Info'))
    BEGIN
        SELECT CAST(0 AS INT) AS Updated;
        RETURN;
    END

    -- The app sent 'SKIPPED' (every JSON string was upper-cased), which the list then did not show as skipped.
    IF @Status = 'Skipped' SET @Status = 'Skipped';
    SET @Reason = NULLIF(LTRIM(RTRIM(@Reason)), N'');
    DECLARE @Ein VARCHAR(20), @Year INT, @Subject NVARCHAR(400);
    SELECT @Ein = REPLACE(REPLACE(ISNULL(TaxID, ''), '-', ''), ' ', ''), @Year = PlanYear FROM dbo.UploadedFileLog WHERE FileLogId = @F;
    SELECT TOP (1) @Subject = SubjectKey FROM dbo.fn_StagingRowSubject(@T, @R);

    UPDATE dbo.StagingRowErrors SET Status = @Status WHERE StagingRowErrorId = @StagingRowErrorId;

    IF @Status = 'Skipped'
    BEGIN
        IF @Rule IS NOT NULL
        BEGIN
            MERGE dbo.StagingIssueSkips AS S
            USING (SELECT @F AS FileLogId, @T AS StagingTableName, @R AS StagingRowId, @Rule AS RuleId, @C AS ColumnName) AS K
               ON S.FileLogId = K.FileLogId AND S.StagingTableName = K.StagingTableName AND S.StagingRowId = K.StagingRowId
              AND S.RuleId = K.RuleId AND S.ColumnName = K.ColumnName
            WHEN MATCHED THEN UPDATE SET Reason = COALESCE(@Reason, S.Reason), SkippedBy = @ChangedBy, SkippedAt = SYSUTCDATETIME(), CarriedFromFileLogId = NULL
            WHEN NOT MATCHED THEN INSERT (FileLogId, StagingTableName, StagingRowId, RuleId, ColumnName, SkippedBy, Reason)
                                  VALUES (K.FileLogId, K.StagingTableName, K.StagingRowId, K.RuleId, K.ColumnName, @ChangedBy, @Reason);

            -- Kept for the employer and year, so a later file is acknowledged the same way.
            IF @Reason IS NOT NULL AND @Subject IS NOT NULL AND @Ein <> '' AND @Year IS NOT NULL
                MERGE dbo.IssueAcknowledgements AS A
                USING (SELECT @Ein AS Ein, @Year AS PlanYear, @T AS StagingTableName, @Subject AS SubjectKey, @Rule AS RuleId, @C AS ColumnName) AS K
                   ON A.Ein = K.Ein AND A.PlanYear = K.PlanYear AND A.StagingTableName = K.StagingTableName
                  AND A.SubjectKey = K.SubjectKey AND A.RuleId = K.RuleId AND A.ColumnName = K.ColumnName
                WHEN MATCHED THEN UPDATE SET Reason = @Reason, AcknowledgedBy = @ChangedBy, AcknowledgedAt = SYSUTCDATETIME(), FileLogId = @F
                WHEN NOT MATCHED THEN INSERT (Ein, PlanYear, StagingTableName, SubjectKey, RuleId, ColumnName, Reason, AcknowledgedBy, FileLogId)
                                      VALUES (K.Ein, K.PlanYear, K.StagingTableName, K.SubjectKey, K.RuleId, K.ColumnName, @Reason, @ChangedBy, @F);
        END
    END
    ELSE
    BEGIN
        DELETE dbo.StagingIssueSkips
        WHERE FileLogId = @F AND StagingTableName = @T AND StagingRowId = @R AND RuleId = @Rule AND ColumnName = @C;
        DELETE dbo.IssueAcknowledgements
        WHERE Ein = @Ein AND PlanYear = @Year AND StagingTableName = @T AND SubjectKey = @Subject AND RuleId = @Rule AND ColumnName = @C;
    END

    SELECT CAST(1 AS INT) AS Updated;
END
GO

------------------------------------------------------------
-- After the rules run: the file's own skips again, then the employer's earlier
-- acknowledgements carried over. A skip whose issue no longer occurs is forgotten.
------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_Validation_ReapplySkips
    @FileLogId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE E SET Status = 'Skipped'
    FROM   dbo.StagingRowErrors E
    JOIN   dbo.StagingIssueSkips S
           ON S.FileLogId = E.FileLogId AND S.StagingTableName = E.StagingTableName
          AND S.StagingRowId = E.StagingRowId AND S.RuleId = E.RuleId AND S.ColumnName = ISNULL(E.ColumnName, N'')
    WHERE  E.FileLogId = @FileLogId AND E.Status <> 'Skipped';

    -- 061: acknowledgements from earlier files of the same employer and year.
    DECLARE @Ein VARCHAR(20), @Year INT;
    SELECT @Ein = REPLACE(REPLACE(ISNULL(TaxID, ''), '-', ''), ' ', ''), @Year = PlanYear FROM dbo.UploadedFileLog WHERE FileLogId = @FileLogId;
    IF @Ein <> '' AND EXISTS (SELECT 1 FROM dbo.IssueAcknowledgements WHERE Ein = @Ein AND PlanYear = @Year AND FileLogId <> @FileLogId)
    BEGIN
        IF OBJECT_ID('tempdb..#Carry') IS NOT NULL DROP TABLE #Carry;
        SELECT E.StagingRowErrorId, E.StagingTableName, E.StagingRowId, E.RuleId, ISNULL(E.ColumnName, N'') AS ColumnName,
               A.Reason, A.AcknowledgedBy, A.FileLogId AS SourceFileLogId
        INTO   #Carry
        FROM   dbo.StagingRowErrors E
        JOIN   dbo.ValidationRules R ON R.RuleId = E.RuleId AND R.Severity IN ('Warning', 'Information', 'Info')
        CROSS  APPLY dbo.fn_StagingRowSubject(E.StagingTableName, E.StagingRowId) K
        JOIN   dbo.IssueAcknowledgements A
               ON A.Ein = @Ein AND A.PlanYear = @Year AND A.StagingTableName = E.StagingTableName AND A.SubjectKey = K.SubjectKey
              AND A.RuleId = E.RuleId AND A.ColumnName = ISNULL(E.ColumnName, N'') AND A.FileLogId <> @FileLogId
        WHERE  E.FileLogId = @FileLogId AND E.Status <> 'Skipped';

        UPDATE E SET Status = 'Skipped'
        FROM dbo.StagingRowErrors E JOIN #Carry C ON C.StagingRowErrorId = E.StagingRowErrorId;

        INSERT dbo.StagingIssueSkips (FileLogId, StagingTableName, StagingRowId, RuleId, ColumnName, SkippedBy, Reason, CarriedFromFileLogId)
        SELECT DISTINCT @FileLogId, C.StagingTableName, C.StagingRowId, C.RuleId, C.ColumnName, C.AcknowledgedBy, C.Reason, C.SourceFileLogId
        FROM   #Carry C
        WHERE  NOT EXISTS (SELECT 1 FROM dbo.StagingIssueSkips S
                           WHERE S.FileLogId = @FileLogId AND S.StagingTableName = C.StagingTableName AND S.StagingRowId = C.StagingRowId
                             AND S.RuleId = C.RuleId AND S.ColumnName = C.ColumnName);
        DROP TABLE #Carry;
    END

    DELETE S
    FROM   dbo.StagingIssueSkips S
    WHERE  S.FileLogId = @FileLogId
      AND  NOT EXISTS (SELECT 1 FROM dbo.StagingRowErrors E
                       WHERE E.FileLogId = S.FileLogId AND E.StagingTableName = S.StagingTableName
                         AND E.StagingRowId = S.StagingRowId AND E.RuleId = S.RuleId
                         AND ISNULL(E.ColumnName, N'') = S.ColumnName);
END
GO

------------------------------------------------------------
-- A file's acknowledged issues: who, when, why, and the file it was carried from.
------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_GetIssueSkipReasons
    @FileLogId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT E.StagingRowErrorId, S.Reason, S.SkippedBy, S.SkippedAt, S.CarriedFromFileLogId
    FROM   dbo.StagingIssueSkips S
    JOIN   dbo.StagingRowErrors E
           ON E.FileLogId = S.FileLogId AND E.StagingTableName = S.StagingTableName AND E.StagingRowId = S.StagingRowId
          AND E.RuleId = S.RuleId AND ISNULL(E.ColumnName, N'') = S.ColumnName
    WHERE  S.FileLogId = @FileLogId;
END
GO
