/* =============================================================================
   056 - Error Triage fixes

   1. A value changed in the Correction Editor is kept: who, when, before and after.
      StagingAutoCorrections gets Source ('Auto' or 'Manual') and ChangedBy.
   2. The editor can change only data columns of a staging table: not the row id,
      FileLogId, RowNumber, IsValid or ErrorMessages, and only on a row of that file.
      A blank value is stored as NULL. An unchanged value is not written.
   3. Re-validating a file (every save in the editor does) no longer forgets
      - the auto-corrections of earlier runs (sp_Validation_AutoCorrect deleted them,
        and values already corrected are not corrected again);
      - the warnings that were skipped (sp_Validation_Cleanup deletes every issue and
        the rules create them again as New). Skips are kept in StagingIssueSkips and
        applied again after the rules run, for as long as the issue still occurs.
   4. An issue's status can be changed only through its own file, and only a warning
      or an information note can be skipped.
   5. dbo.sp_GetFileCorrections lists a file's corrections for the triage page.

   sp_Validation_AutoCorrect is the database's current one (script 055) with only
   the changes marked 056. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.StagingAutoCorrections', 'Source') IS NULL
    ALTER TABLE dbo.StagingAutoCorrections
        ADD Source NVARCHAR(10) NOT NULL CONSTRAINT DF_StagingAutoCorrections_Source DEFAULT N'Auto';
GO
IF COL_LENGTH('dbo.StagingAutoCorrections', 'ChangedBy') IS NULL
    ALTER TABLE dbo.StagingAutoCorrections ADD ChangedBy NVARCHAR(256) NULL;
GO

IF OBJECT_ID('dbo.StagingIssueSkips') IS NULL
BEGIN
    CREATE TABLE dbo.StagingIssueSkips
    (
        StagingIssueSkipId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StagingIssueSkips PRIMARY KEY,
        FileLogId          INT            NOT NULL,
        StagingTableName   NVARCHAR(128)  NOT NULL,
        StagingRowId       BIGINT         NOT NULL,
        RuleId             INT            NOT NULL,
        ColumnName         NVARCHAR(128)  NOT NULL CONSTRAINT DF_StagingIssueSkips_ColumnName DEFAULT N'',
        SkippedBy          NVARCHAR(256)  NULL,
        SkippedAt          DATETIME2(0)   NOT NULL CONSTRAINT DF_StagingIssueSkips_SkippedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_StagingIssueSkips UNIQUE (FileLogId, StagingTableName, StagingRowId, RuleId, ColumnName)
    );

    -- The skips in place now.
    INSERT dbo.StagingIssueSkips (FileLogId, StagingTableName, StagingRowId, RuleId, ColumnName)
    SELECT DISTINCT FileLogId, StagingTableName, StagingRowId, RuleId, ISNULL(ColumnName, N'')
    FROM   dbo.StagingRowErrors
    WHERE  Status = 'Skipped' AND RuleId IS NOT NULL;
END
GO

------------------------------------------------------------
-- Skip (or un-skip) one issue, through its file.
------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_UpdateStagingRowErrorStatus
    @StagingRowErrorId BIGINT,
    @Status            NVARCHAR(20),
    @FileLogId         INT           = NULL,
    @ChangedBy         NVARCHAR(256) = NULL
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

    UPDATE dbo.StagingRowErrors SET Status = @Status WHERE StagingRowErrorId = @StagingRowErrorId;

    IF @Status = 'Skipped'
    BEGIN
        IF @Rule IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.StagingIssueSkips
                                             WHERE FileLogId = @F AND StagingTableName = @T AND StagingRowId = @R
                                               AND RuleId = @Rule AND ColumnName = @C)
            INSERT dbo.StagingIssueSkips (FileLogId, StagingTableName, StagingRowId, RuleId, ColumnName, SkippedBy)
            VALUES (@F, @T, @R, @Rule, @C, @ChangedBy);
    END
    ELSE
        DELETE dbo.StagingIssueSkips
        WHERE FileLogId = @F AND StagingTableName = @T AND StagingRowId = @R AND RuleId = @Rule AND ColumnName = @C;

    SELECT CAST(1 AS INT) AS Updated;
END
GO

------------------------------------------------------------
-- After the rules run: the issues skipped before are skipped again. A skip whose
-- issue no longer occurs is forgotten.
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

    DELETE S
    FROM   dbo.StagingIssueSkips S
    WHERE  S.FileLogId = @FileLogId
      AND  NOT EXISTS (SELECT 1 FROM dbo.StagingRowErrors E
                       WHERE E.FileLogId = S.FileLogId AND E.StagingTableName = S.StagingTableName
                         AND E.StagingRowId = S.StagingRowId AND E.RuleId = S.RuleId
                         AND ISNULL(E.ColumnName, N'') = S.ColumnName);
END
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_ExecuteValidationEngine] --1
    @FileLogId INT
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        PRINT '1. Cleanup...';
        EXEC sp_Validation_Cleanup @FileLogId;

        PRINT '2. Normalize...';
        EXEC sp_Validation_Normalize @FileLogId;

        PRINT '3. AutoCorrect...';
        EXEC sp_Validation_AutoCorrect @FileLogId;

        PRINT '4. Execute Rules...';
        EXEC sp_Validation_ExecuteRules @FileLogId;

        PRINT '4b. Skipped issues...';   -- 056
        EXEC sp_Validation_ReapplySkips @FileLogId;

        PRINT '5. Update Status...';
        EXEC sp_Validation_UpdateStatus @FileLogId;

        PRINT '6. Finalize...';
        EXEC sp_Validation_Finalize @FileLogId;

        PRINT 'Complete.';
    END TRY
    BEGIN CATCH
        -- Safety Cleanup of temp tables if open
        IF OBJECT_ID('tempdb..#BadEmployees') IS NOT NULL DROP TABLE #BadEmployees;
        IF OBJECT_ID('tempdb..#BadEmployers') IS NOT NULL DROP TABLE #BadEmployers;
        IF OBJECT_ID('tempdb..#BadPlans') IS NOT NULL DROP TABLE #BadPlans;
        IF OBJECT_ID('tempdb..#BadPremiums') IS NOT NULL DROP TABLE #BadPremiums;
        IF OBJECT_ID('tempdb..#BadDependents') IS NOT NULL DROP TABLE #BadDependents;

        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();

        PRINT 'Critical Error: ' + @ErrorMessage;
        RAISERROR (@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END
GO

------------------------------------------------------------
-- One value changed in the Correction Editor, kept with who changed it.
-- Returns Changed = 1 when the value was different.
------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_UpdateStagingField_NoValidation
    @FileLogId        INT,
    @StagingTableName NVARCHAR(128),
    @StagingRowId     BIGINT,
    @ColumnName       NVARCHAR(128),
    @NewValue         NVARCHAR(MAX),
    @ChangedBy        NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET DEADLOCK_PRIORITY LOW;

    DECLARE @Table SYSNAME, @Pk SYSNAME;
    SELECT @Table = TableName, @Pk = PrimaryKeyColumn FROM dbo.StagingTableMetadata WHERE TableName = @StagingTableName;
    IF @Pk IS NULL
        THROW 51010, 'Not a staging table.', 1;
    IF @ColumnName IS NULL OR @ColumnName IN (@Pk, N'FileLogId', N'RowNumber', N'IsValid', N'ErrorMessages')
       OR COL_LENGTH(N'dbo.' + QUOTENAME(@Table), @ColumnName) IS NULL
        THROW 51011, 'This column cannot be changed.', 1;

    SET @NewValue = NULLIF(@NewValue, N'');

    DECLARE @Old NVARCHAR(MAX), @Found BIT = 0, @SQL NVARCHAR(MAX);
    SET @SQL = N'SELECT @Old = CONVERT(NVARCHAR(MAX), ' + QUOTENAME(@ColumnName) + N'), @Found = 1
                 FROM dbo.' + QUOTENAME(@Table) + N' WHERE ' + QUOTENAME(@Pk) + N' = @RowId AND FileLogId = @FileLogId;';
    EXEC sp_executesql @SQL, N'@RowId BIGINT, @FileLogId INT, @Old NVARCHAR(MAX) OUTPUT, @Found BIT OUTPUT',
         @StagingRowId, @FileLogId, @Old OUTPUT, @Found OUTPUT;
    IF @Found = 0
        THROW 51012, 'The row is not in this file.', 1;

    IF ISNULL(@Old, N'') COLLATE Latin1_General_BIN2 = ISNULL(@NewValue, N'') COLLATE Latin1_General_BIN2
    BEGIN
        SELECT CAST(0 AS BIT) AS Changed;
        RETURN;
    END

    SET @SQL = N'UPDATE dbo.' + QUOTENAME(@Table) + N' WITH (ROWLOCK)
                 SET ' + QUOTENAME(@ColumnName) + N' = @NewValue, IsValid = 1
                 WHERE ' + QUOTENAME(@Pk) + N' = @RowId AND FileLogId = @FileLogId;';
    EXEC sp_executesql @SQL, N'@NewValue NVARCHAR(MAX), @RowId BIGINT, @FileLogId INT', @NewValue, @StagingRowId, @FileLogId;

    INSERT dbo.StagingAutoCorrections (FileLogId, StagingTableName, StagingRowId, ColumnName, RuleId, OldValue, NewValue, Source, ChangedBy)
    VALUES (@FileLogId, @Table, @StagingRowId, @ColumnName, NULL, LEFT(@Old, 4000), LEFT(@NewValue, 4000), N'Manual', @ChangedBy);

    SELECT CAST(1 AS BIT) AS Changed;
END
GO

------------------------------------------------------------
-- A file's corrections: 1) counts by source, rule and column; 2) one page of them.
------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_GetFileCorrections
    @FileLogId  INT,
    @Source     NVARCHAR(10)  = NULL,
    @RuleId     INT           = NULL,
    @ColumnName NVARCHAR(128) = NULL,
    @Search     NVARCHAR(200) = NULL,
    @PageIndex  INT           = 1,
    @PageSize   INT           = 25
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageIndex IS NULL OR @PageIndex < 1 SET @PageIndex = 1;
    IF @PageSize IS NULL OR @PageSize NOT BETWEEN 1 AND 500 SET @PageSize = 25;
    SET @Search = NULLIF(LTRIM(RTRIM(@Search)), N'');

    SELECT C.Source, C.RuleId, R.RuleName, C.StagingTableName, C.ColumnName, COUNT(*) AS Changes
    FROM   dbo.StagingAutoCorrections C
    LEFT   JOIN dbo.ValidationRules R ON R.RuleId = C.RuleId
    WHERE  C.FileLogId = @FileLogId
    GROUP  BY C.Source, C.RuleId, R.RuleName, C.StagingTableName, C.ColumnName
    ORDER  BY Changes DESC, C.ColumnName;

    ;WITH X AS
    (
        SELECT C.StagingAutoCorrectionId, C.Source, C.RuleId, R.RuleName, C.StagingTableName, C.StagingRowId,
               C.ColumnName, C.OldValue, C.NewValue, C.ChangedBy, C.CorrectedAt,
               COALESCE(EE.RowNumber, ER.RowNumber, PL.RowNumber, PR.RowNumber, DP.RowNumber) AS RowNumber,
               COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(EE.EmployeeLegalFirstName, N' ', EE.EmployeeLegalLastName))), N''),
                        ER.EmployerName, PL.PlanName, PR.PlanName,
                        NULLIF(LTRIM(RTRIM(CONCAT(DP.DependentLegalFirstName, N' ', DP.DependentLegalLastName))), N'')) AS RowLabel
        FROM   dbo.StagingAutoCorrections C
        LEFT   JOIN dbo.ValidationRules R     ON R.RuleId = C.RuleId
        LEFT   JOIN dbo.Staging_Employees  EE ON C.StagingTableName = N'Staging_Employees'  AND EE.StagingEmployeeId  = C.StagingRowId
        LEFT   JOIN dbo.Staging_Employers  ER ON C.StagingTableName = N'Staging_Employers'  AND ER.StagingEmployerId  = C.StagingRowId
        LEFT   JOIN dbo.Staging_Plans      PL ON C.StagingTableName = N'Staging_Plans'      AND PL.StagingPlanId      = C.StagingRowId
        LEFT   JOIN dbo.Staging_Premiums   PR ON C.StagingTableName = N'Staging_Premiums'   AND PR.StagingPremiumId   = C.StagingRowId
        LEFT   JOIN dbo.Staging_Dependents DP ON C.StagingTableName = N'Staging_Dependents' AND DP.StagingDependentId = C.StagingRowId
        WHERE  C.FileLogId = @FileLogId
          AND  (@Source IS NULL OR C.Source = @Source)
          AND  (@RuleId IS NULL OR C.RuleId = @RuleId)
          AND  (@ColumnName IS NULL OR C.ColumnName = @ColumnName)
    )
    SELECT *, COUNT(*) OVER () AS TotalCount
    FROM   X
    WHERE  @Search IS NULL
       OR  X.ColumnName LIKE N'%' + @Search + N'%' OR X.OldValue LIKE N'%' + @Search + N'%'
       OR  X.NewValue LIKE N'%' + @Search + N'%' OR X.RowLabel LIKE N'%' + @Search + N'%'
       OR  X.ChangedBy LIKE N'%' + @Search + N'%'
    ORDER  BY CASE WHEN X.Source = N'Manual' THEN 0 ELSE 1 END, X.CorrectedAt DESC, X.RowNumber, X.ColumnName
    OFFSET (@PageIndex - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

------------------------------------------------------------
-- sp_Validation_AutoCorrect (script 055's, with the 056 changes)
------------------------------------------------------------
-- sp_Validation_AutoCorrect
CREATE OR ALTER PROCEDURE [dbo].[sp_Validation_AutoCorrect]
(
    @FileLogId INT
)
AS
BEGIN

SET NOCOUNT ON;

DECLARE
    @TargetTable SYSNAME,
    @TargetColumn SYSNAME,
    @ValidationType NVARCHAR(50),
    @Param1 NVARCHAR(500),
    @AutoCorrectReplacement NVARCHAR(100),
    @SQL NVARCHAR(MAX);

------------------------------------------------------------
-- STEP 1: GLOBAL NORMALIZATION
------------------------------------------------------------
-- 1a. FIX EIN FORMATTING (Add hyphen to 9-digit strings)
-- For Employers
UPDATE Staging_Employers
SET PrimaryEIN = LEFT(PrimaryEIN, 2) + '-' + RIGHT(PrimaryEIN, 7)
WHERE FileLogId = @FileLogId
  AND LEN(PrimaryEIN) = 9
  AND PrimaryEIN NOT LIKE '%-%';

-- For Employees (Primary EIN and Associated EIN)
UPDATE Staging_Employees
SET PrimaryEIN = LEFT(PrimaryEIN, 2) + '-' + RIGHT(PrimaryEIN, 7)
WHERE FileLogId = @FileLogId
  AND LEN(PrimaryEIN) = 9
  AND PrimaryEIN NOT LIKE '%-%';

UPDATE Staging_Employees
SET EINAssociatedWithEE = LEFT(EINAssociatedWithEE, 2) + '-' + RIGHT(EINAssociatedWithEE, 7)
WHERE FileLogId = @FileLogId
  AND LEN(EINAssociatedWithEE) = 9
  AND EINAssociatedWithEE NOT LIKE '%-%';

UPDATE Staging_Employers
SET Zip = '0' + CAST(Zip AS VARCHAR(5))
WHERE FileLogId = @FileLogId
  AND LEN(CAST(Zip AS VARCHAR(5))) = 4
  AND Zip NOT LIKE '%[^0-9]%';

UPDATE Staging_Employees
SET Zip = '0' + CAST(Zip AS VARCHAR(5))
WHERE FileLogId = @FileLogId
  AND LEN(CAST(Zip AS VARCHAR(5))) = 4
  AND Zip NOT LIKE '%[^0-9]%';

UPDATE Staging_Employers
SET Phone = REPLACE(REPLACE(REPLACE(REPLACE(Phone,'(',''),')',''),'-',''),' ','')
WHERE FileLogId=@FileLogId;

UPDATE Staging_Employers
SET
EmployerName = UPPER(ISNULL(EmployerName,'')),
Address = UPPER(ISNULL(Address,'')),
Address2 = UPPER(ISNULL(Address2,'')),
City = UPPER(ISNULL(City,'')),
Contact = UPPER(ISNULL(Contact,'')),
Title = UPPER(ISNULL(Title,''))
WHERE FileLogId=@FileLogId;

UPDATE Staging_Employees
SET
EmployeeLegalFirstName = UPPER(ISNULL(EmployeeLegalFirstName,'')),
EmployeeLegalLastName = UPPER(ISNULL(EmployeeLegalLastName,'')),
City = UPPER(ISNULL(City,''))
WHERE FileLogId=@FileLogId;

UPDATE Staging_Dependents
SET
DependentLegalFirstName = UPPER(ISNULL(DependentLegalFirstName,'')),
DependentLegalLastName = UPPER(ISNULL(DependentLegalLastName,'')),
DependentMiddleInitial = UPPER(ISNULL(DependentMiddleInitial,''))
WHERE FileLogId=@FileLogId;

------------------------------------------------------------
-- 055: THE VALUES THE STEPS BELOW MAY CHANGE, to record what changed. Taken
-- after step 1's formatting (upper case, EIN hyphen, ZIP leading zero, phone
-- digits), which is not reported.
------------------------------------------------------------
-- 056: earlier runs' corrections stay on record (the values they corrected are not corrected
-- again, so deleting them lost the record). Only rows no longer in the file are forgotten.
DELETE C FROM dbo.StagingAutoCorrections C
WHERE C.FileLogId = @FileLogId
  AND NOT EXISTS (SELECT 1 FROM dbo.Staging_Employees  S WHERE C.StagingTableName = N'Staging_Employees'  AND S.StagingEmployeeId  = C.StagingRowId AND S.FileLogId = @FileLogId
                  UNION ALL
                  SELECT 1 FROM dbo.Staging_Employers  S WHERE C.StagingTableName = N'Staging_Employers'  AND S.StagingEmployerId  = C.StagingRowId AND S.FileLogId = @FileLogId
                  UNION ALL
                  SELECT 1 FROM dbo.Staging_Plans      S WHERE C.StagingTableName = N'Staging_Plans'      AND S.StagingPlanId      = C.StagingRowId AND S.FileLogId = @FileLogId
                  UNION ALL
                  SELECT 1 FROM dbo.Staging_Premiums   S WHERE C.StagingTableName = N'Staging_Premiums'   AND S.StagingPremiumId   = C.StagingRowId AND S.FileLogId = @FileLogId
                  UNION ALL
                  SELECT 1 FROM dbo.Staging_Dependents S WHERE C.StagingTableName = N'Staging_Dependents' AND S.StagingDependentId = C.StagingRowId AND S.FileLogId = @FileLogId);
IF OBJECT_ID('tempdb..#AcBefore') IS NOT NULL DROP TABLE #AcBefore;
CREATE TABLE #AcBefore (TableName SYSNAME NOT NULL, RowId BIGINT NOT NULL, ColumnName SYSNAME NOT NULL, RuleId INT NULL,
                        OldValue NVARCHAR(4000) NULL, PRIMARY KEY (TableName, ColumnName, RowId));
DECLARE @AcTable SYSNAME, @AcColumn SYSNAME, @AcRule INT, @AcCondition NVARCHAR(MAX), @AcPk SYSNAME;
DECLARE @AcCandidates TABLE (Seq INT IDENTITY PRIMARY KEY, TableName SYSNAME, ColumnName SYSNAME, RuleId INT NULL, Condition NVARCHAR(MAX));
-- The auto-correct rules, with the condition under which each changes a value.
INSERT @AcCandidates (TableName, ColumnName, RuleId, Condition)
SELECT TargetTable, TargetColumn, RuleId,
       CASE ValidationType
            WHEN 'REGEX_CONTAINS' THEN QUOTENAME(TargetColumn) + N' LIKE N''' + REPLACE(ISNULL(RuleParameter1, ''), '''', '''''') + N''''
            WHEN 'MAX_LENGTH'     THEN N'LEN(' + QUOTENAME(TargetColumn) + N') > ' + CAST(TRY_CAST(RuleParameter1 AS INT) AS NVARCHAR(10))
            ELSE N'ISNULL(' + QUOTENAME(TargetColumn) + N', '''') = '''''
       END
FROM   ValidationRules
WHERE  IsActive = 1 AND ISNULL(IsAutoCorrect, 0) = 1
  AND  ValidationType IN ('REGEX_CONTAINS', 'MAX_LENGTH', 'REQUIRED', 'REQUIRED_IF_EQUALS')
  AND  (ValidationType <> 'MAX_LENGTH' OR TRY_CAST(RuleParameter1 AS INT) IS NOT NULL)
  AND  EXISTS (SELECT 1 FROM StagingTableMetadata M WHERE M.TableName = TargetTable)
  AND  EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(TargetTable)) AND c.name = TargetColumn);
-- Step 2's name cleaning and steps 3-4's employer-name cleaning.
INSERT @AcCandidates (TableName, ColumnName, RuleId, Condition) VALUES
    ('Staging_Employees', 'EmployeeLegalFirstName', NULL, N'[EmployeeLegalFirstName] LIKE N''%[^A-Za-z ''''-]%'''),
    ('Staging_Employees', 'EmployeeLegalLastName',  NULL, N'[EmployeeLegalLastName] LIKE N''%[^A-Za-z ''''-]%'''),
    ('Staging_Employees', 'EmployeeMiddleInitial',  NULL, N'[EmployeeMiddleInitial] LIKE N''%[^A-Za-z ''''-]%'''),
    ('Staging_Employers', 'EmployerName',           NULL, N'[EmployerName] LIKE N''%[^A-Z0-9()&'''' -]%'' OR [EmployerName] LIKE N''%  %''');
DECLARE @AcSeq INT = 1, @AcMax INT = (SELECT MAX(Seq) FROM @AcCandidates);
WHILE @AcSeq <= @AcMax
BEGIN
    SELECT @AcTable = TableName, @AcColumn = ColumnName, @AcRule = RuleId, @AcCondition = Condition FROM @AcCandidates WHERE Seq = @AcSeq;
    SELECT @AcPk = PrimaryKeyColumn FROM StagingTableMetadata WHERE TableName = @AcTable;
    SET @SQL = N'INSERT INTO #AcBefore (TableName, RowId, ColumnName, RuleId, OldValue)
        SELECT @T, T.' + QUOTENAME(@AcPk) + N', @C, @R, CONVERT(NVARCHAR(4000), T.' + QUOTENAME(@AcColumn) + N')
        FROM ' + QUOTENAME(@AcTable) + N' T
        WHERE T.FileLogId = @FileLogId AND (' + @AcCondition + N')
          AND NOT EXISTS (SELECT 1 FROM #AcBefore B WHERE B.TableName = @T AND B.ColumnName = @C AND B.RowId = T.' + QUOTENAME(@AcPk) + N');';
    EXEC sp_executesql @SQL, N'@FileLogId INT, @T SYSNAME, @C SYSNAME, @R INT', @FileLogId, @AcTable, @AcColumn, @AcRule;
    SET @AcSeq += 1;
END

------------------------------------------------------------
-- STEP 2: STRICT NAME CLEANING (THE BUG FIX)
------------------------------------------------------------
-- This ensures we only keep Letters, Spaces, and Hyphens. 
-- The CASE statements ensure we don't return NULL when STUFF fails.
DECLARE @NamePattern NVARCHAR(100) = '%[^A-Za-z ''-]%'; 

WHILE EXISTS (SELECT 1 FROM Staging_Employees WHERE FileLogId = @FileLogId 
              AND (EmployeeLegalFirstName LIKE @NamePattern 
                   OR EmployeeLegalLastName LIKE @NamePattern 
                   OR EmployeeMiddleInitial LIKE @NamePattern))
BEGIN
    UPDATE TOP (5000) Staging_Employees
    SET EmployeeLegalFirstName = CASE 
            WHEN PATINDEX(@NamePattern, EmployeeLegalFirstName) > 0 
            THEN STUFF(EmployeeLegalFirstName, PATINDEX(@NamePattern, EmployeeLegalFirstName), 1, '') 
            ELSE EmployeeLegalFirstName END,
        
        EmployeeLegalLastName = CASE 
            WHEN PATINDEX(@NamePattern, EmployeeLegalLastName) > 0 
            THEN STUFF(EmployeeLegalLastName, PATINDEX(@NamePattern, EmployeeLegalLastName), 1, '') 
            ELSE EmployeeLegalLastName END,
            
        EmployeeMiddleInitial = CASE 
            WHEN PATINDEX(@NamePattern, EmployeeMiddleInitial) > 0 
            THEN STUFF(EmployeeMiddleInitial, PATINDEX(@NamePattern, EmployeeMiddleInitial), 1, '') 
            ELSE EmployeeMiddleInitial END
    WHERE FileLogId = @FileLogId 
      AND (EmployeeLegalFirstName LIKE @NamePattern OR EmployeeLegalLastName LIKE @NamePattern OR EmployeeMiddleInitial LIKE @NamePattern);
END

------------------------------------------------------------
-- STEP 3: AUTO CORRECT RULE LOOP
------------------------------------------------------------
DECLARE rule_cursor CURSOR FAST_FORWARD FOR
SELECT TargetTable, TargetColumn, ValidationType, ISNULL(RuleParameter1,''), ISNULL(AutoCorrectReplacement,'')
FROM ValidationRules WHERE IsActive=1 AND ISNULL(IsAutoCorrect,0)=1 ORDER BY RuleId;

OPEN rule_cursor;
FETCH NEXT FROM rule_cursor INTO @TargetTable, @TargetColumn, @ValidationType, @Param1, @AutoCorrectReplacement;

WHILE @@FETCH_STATUS=0
BEGIN

SET @Param1 = REPLACE(@Param1,'''','''''');
SET @AutoCorrectReplacement = REPLACE(@AutoCorrectReplacement,'''','''''');

IF @ValidationType='TRIM'
BEGIN
    SET @SQL='UPDATE T SET '+QUOTENAME(@TargetColumn)+' = UPPER(LTRIM(RTRIM('+QUOTENAME(@TargetColumn)+'))) FROM '+QUOTENAME(@TargetTable)+' T WHERE FileLogId=@FileLogId';
    EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId;
END
ELSE IF @ValidationType='REQUIRED'
BEGIN
    SET @SQL='UPDATE T SET '+QUOTENAME(@TargetColumn)+' = UPPER('''+ISNULL(@AutoCorrectReplacement,'UNKNOWN')+''') FROM '+QUOTENAME(@TargetTable)+' T WHERE FileLogId=@FileLogId AND ISNULL('+QUOTENAME(@TargetColumn)+','''')=''''';
    EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId;
END
ELSE IF @ValidationType='MAX_LENGTH' AND ISNUMERIC(@Param1)=1
BEGIN
    SET @SQL='UPDATE T SET '+QUOTENAME(@TargetColumn)+' = UPPER(LEFT('+QUOTENAME(@TargetColumn)+','+@Param1+')) FROM '+QUOTENAME(@TargetTable)+' T WHERE FileLogId=@FileLogId AND LEN('+QUOTENAME(@TargetColumn)+')>'+@Param1;
    EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId;
END
ELSE IF @ValidationType='REGEX_CONTAINS'
BEGIN
    SET @SQL='
    DECLARE @Rows INT=1;
    WHILE @Rows>0
    BEGIN
        UPDATE TOP (5000) T
        SET '+QUOTENAME(@TargetColumn)+' = UPPER(STUFF('+QUOTENAME(@TargetColumn)+', PATINDEX('''+@Param1+''','+QUOTENAME(@TargetColumn)+'), 1, '''+ISNULL(@AutoCorrectReplacement,'')+'''))
        FROM '+QUOTENAME(@TargetTable)+' T
        WHERE FileLogId=@FileLogId AND '+QUOTENAME(@TargetColumn)+' LIKE '''+@Param1+'''
        SET @Rows=@@ROWCOUNT;
    END';
    EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId;
END

IF @TargetColumn='ForeignAddressInd' AND @ValidationType='REQUIRED_IF_EQUALS'
BEGIN
    SET @SQL='
    UPDATE '+QUOTENAME(@TargetTable)+' SET ForeignAddressInd=''0'' WHERE FileLogId=@FileLogId AND (ForeignAddressInd IS NULL OR ForeignAddressInd='''') AND UPPER(Country)=''US'';
    UPDATE '+QUOTENAME(@TargetTable)+' SET ForeignAddressInd=''1'' WHERE FileLogId=@FileLogId AND (ForeignAddressInd IS NULL OR ForeignAddressInd='''') AND Country<>''US'' AND Country IS NOT NULL;';
    EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId;
END

FETCH NEXT FROM rule_cursor INTO @TargetTable, @TargetColumn, @ValidationType, @Param1, @AutoCorrectReplacement;
END

CLOSE rule_cursor;
DEALLOCATE rule_cursor;

------------------------------------------------------------
-- STEP 3: STRICT CLEANING (ENTERPRISE SAFE)
------------------------------------------------------------
DECLARE @StrictEmp NVARCHAR(100)='%[^A-Z0-9()&'' -]%';

WHILE EXISTS (SELECT 1 FROM Staging_Employers WHERE FileLogId=@FileLogId AND EmployerName LIKE @StrictEmp)
BEGIN
    UPDATE TOP (5000) Staging_Employers
    SET EmployerName = UPPER(STUFF(EmployerName, PATINDEX(@StrictEmp,EmployerName), 1, ''))
    WHERE FileLogId=@FileLogId;
END

------------------------------------------------------------
-- STEP 4: FINAL SPACE CLEANUP
------------------------------------------------------------
UPDATE Staging_Employers
SET EmployerName = REPLACE(REPLACE(REPLACE(EmployerName, '   ',' '), '  ',' '), '  ',' ')
WHERE FileLogId=@FileLogId;


------------------------------------------------------------
-- 055: RECORD WHAT CHANGED, and show each change as an Information note on
-- its row (Information notes do not affect the import or the file's status).
------------------------------------------------------------
DECLARE ac_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT DISTINCT TableName, ColumnName FROM #AcBefore;
OPEN ac_cursor;
FETCH NEXT FROM ac_cursor INTO @AcTable, @AcColumn;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @AcPk = PrimaryKeyColumn FROM StagingTableMetadata WHERE TableName = @AcTable;
    SET @SQL = N'INSERT INTO dbo.StagingAutoCorrections (FileLogId, StagingTableName, StagingRowId, ColumnName, RuleId, OldValue, NewValue)
        SELECT @FileLogId, B.TableName, B.RowId, B.ColumnName, B.RuleId, B.OldValue, CONVERT(NVARCHAR(4000), T.' + QUOTENAME(@AcColumn) + N')
        FROM #AcBefore B JOIN ' + QUOTENAME(@AcTable) + N' T ON T.' + QUOTENAME(@AcPk) + N' = B.RowId
        WHERE B.TableName = @T AND B.ColumnName = @C
          AND UPPER(LTRIM(RTRIM(ISNULL(B.OldValue, N'''')))) COLLATE Latin1_General_BIN2
              <> UPPER(LTRIM(RTRIM(ISNULL(CONVERT(NVARCHAR(4000), T.' + QUOTENAME(@AcColumn) + N'), N'''')))) COLLATE Latin1_General_BIN2;';
    EXEC sp_executesql @SQL, N'@FileLogId INT, @T SYSNAME, @C SYSNAME', @FileLogId, @AcTable, @AcColumn;
    FETCH NEXT FROM ac_cursor INTO @AcTable, @AcColumn;
END
CLOSE ac_cursor;
DEALLOCATE ac_cursor;
DROP TABLE #AcBefore;

INSERT INTO dbo.StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId, Status, ColumnName, Detail)
SELECT C.FileLogId, C.StagingTableName, C.StagingRowId, R.RuleId, 'New', C.ColumnName,
       LEFT(N'Corrected automatically: "' + ISNULL(C.OldValue, N'') + N'" became "' + ISNULL(C.NewValue, N'') + N'".', 500)
FROM   dbo.StagingAutoCorrections C
CROSS JOIN (SELECT TOP 1 RuleId FROM dbo.ValidationRules WHERE RuleCode = 'AUTO_CORRECTED' ORDER BY RuleId) R
WHERE  C.FileLogId = @FileLogId
  AND  C.Source = N'Auto';   -- 056: changes made in the Correction Editor are listed, not noted
END
GO
