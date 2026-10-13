/* =============================================================================
   067 - Rule "what-if" (idea #7)

   Before a rule is switched on or changed, the Rules page can count what it
   would flag in the most recent files whose rows are still in staging, next
   to what the saved rule flags there now.

   1. RuleWhatIfRuns / RuleWhatIfHits: the trial rule and its hits. A run's
      rows are deleted when it finishes.
   2. sp_Validation_ExecuteRules gets @WhatIfRunId. With it, only the trial
      rule runs and its hits go to RuleWhatIfHits, never to StagingRowErrors.
      Without it the procedure is exactly the current one (script 066).
   3. sp_Validation_RuleWhatIf: runs the trial over the files and returns
      (a) problems with the rule, (b) counts per file, (c) up to 25 sample rows.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.RuleWhatIfRuns') IS NULL
    CREATE TABLE dbo.RuleWhatIfRuns (
        RunId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RuleWhatIfRuns PRIMARY KEY,
        TargetTable    VARCHAR(100)  NOT NULL,
        TargetColumn   VARCHAR(100)  NOT NULL,
        ValidationType VARCHAR(50)   NOT NULL,
        RuleParameter1 VARCHAR(500)  NULL,
        RuleParameter2 VARCHAR(500)  NULL,
        CreatedBy      INT           NULL,
        CreatedAt      DATETIME2(0)  NOT NULL CONSTRAINT DF_RuleWhatIfRuns_CreatedAt DEFAULT SYSDATETIME()
    );
GO
IF OBJECT_ID('dbo.RuleWhatIfHits') IS NULL
BEGIN
    CREATE TABLE dbo.RuleWhatIfHits (
        FileLogId        INT           NOT NULL,
        StagingTableName NVARCHAR(128) NOT NULL,
        StagingRowId     BIGINT        NOT NULL,
        RuleId           INT           NOT NULL   -- minus the RunId
    );
    CREATE INDEX IX_RuleWhatIfHits_Rule ON dbo.RuleWhatIfHits (RuleId, FileLogId) INCLUDE (StagingRowId);
END
GO

-- 3. Procedures --------------------------------------------------------------
-- sp_Validation_ExecuteRules
/* ---------------------------------------------------------------- PART 1 ----
   sp_Validation_ExecuteRules - handlers for the eight missing types, repairs for
   the two malformed blocks, and the missing CUSTOM_LOGIC keyword.
   ---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE [dbo].[sp_Validation_ExecuteRules] @FileLogId INT,
    @WhatIfRunId INT = NULL   -- 067: run only that trial rule, and write its hits to RuleWhatIfHits
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RuleId INT, @TargetTable NVARCHAR(128), @TargetColumn NVARCHAR(128), @ValidationType NVARCHAR(50);
    DECLARE @Param1 NVARCHAR(500), @Param2 NVARCHAR(500), @DynamicSQL NVARCHAR(MAX), @PrimaryKeyColumn NVARCHAR(128);

    -- Quoted forms, safe to concatenate as identifiers. The unquoted @TargetTable
    -- is still needed for StagingRowErrors.StagingTableName, which stores the name.
    DECLARE @QTable NVARCHAR(258), @QColumn NVARCHAR(258), @QPk NVARCHAR(258), @QParam2 NVARCHAR(258);
    DECLARE @ShouldExecute BIT, @RejectReason NVARCHAR(400);
    /* F-08: parsed forms for the eight validation types that had no handler.
       19 active rules used them and silently produced nothing. */
    DECLARE @QParam1Col NVARCHAR(258), @QParam1Table NVARCHAR(258);
    DECLARE @QCondColumn NVARCHAR(258), @CondValue NVARCHAR(400), @CommaPos INT;

    DECLARE val_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT RuleId, TargetTable, TargetColumn, ValidationType, RuleParameter1, RuleParameter2
    FROM ValidationRules WHERE IsActive = 1 AND @WhatIfRunId IS NULL
    UNION ALL   -- 067: a rule being tried on the Rules page (not saved); its id is the run's, negated
    SELECT -RunId, TargetTable, TargetColumn, ValidationType, RuleParameter1, RuleParameter2
    FROM dbo.RuleWhatIfRuns WHERE RunId = @WhatIfRunId;

    OPEN val_cursor;
    FETCH NEXT FROM val_cursor INTO @RuleId, @TargetTable, @TargetColumn, @ValidationType, @Param1, @Param2;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Param1 = REPLACE(ISNULL(@Param1, ''), '''', '''''');
        SET @Param2 = REPLACE(ISNULL(@Param2, ''), '''', '''''');
        SET @ShouldExecute = 1;
        SET @RejectReason  = NULL;
        SET @DynamicSQL    = NULL;

        -- Get Primary Key for the table
        SELECT @PrimaryKeyColumn = PrimaryKeyColumn FROM StagingTableMetadata WHERE TableName = @TargetTable;

        /* ---- (a) identifier allow-list -------------------------------------
           TargetTable, TargetColumn and RuleParameter2 reach this procedure from
           a user-editable table and are concatenated into executable SQL. Accept
           only names that resolve to a registered staging table and a real
           column of it. */
        IF @PrimaryKeyColumn IS NULL
            SET @RejectReason = 'TargetTable "' + ISNULL(@TargetTable, '(null)') + '" is not registered in StagingTableMetadata.';
        ELSE IF NOT EXISTS (SELECT 1 FROM sys.columns
                            WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                              AND name = @TargetColumn)
            SET @RejectReason = 'TargetColumn "' + ISNULL(@TargetColumn, '(null)') + '" is not a column of ' + @TargetTable + '.';
        ELSE IF @ValidationType IN ('MAX_LENGTH', 'MIN_LENGTH') AND TRY_CAST(@Param1 AS INT) IS NULL
            SET @RejectReason = @ValidationType + ' requires an integer RuleParameter1; got "' + @Param1 + '".';
        ELSE IF @ValidationType = 'CUSTOM_LOGIC' AND @Param1 = 'COLUMNS_NOT_EQUAL'
                AND NOT EXISTS (SELECT 1 FROM sys.columns
                                WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                                  AND name = @Param2)
            SET @RejectReason = 'COLUMNS_NOT_EQUAL requires RuleParameter2 to be a column of ' + @TargetTable + '; got "' + @Param2 + '".';
        /* ---- F-08: identifier allow-listing for the newly handled types.
           Same principle as above: anything that reaches executable SQL as an
           identifier has to resolve to a real table or column first. */
        ELSE IF @ValidationType IN ('GREATER_THAN_COLUMN', 'REQUIRED_IF_NULL')
                AND NOT EXISTS (SELECT 1 FROM sys.columns
                                WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                                  AND name = @Param1)
            SET @RejectReason = @ValidationType + ' requires RuleParameter1 to be a column of '
                              + @TargetTable + '; got "' + @Param1 + '".';
        ELSE IF @ValidationType = 'EXISTS_IN_TABLE'
                AND NOT EXISTS (SELECT 1 FROM StagingTableMetadata WHERE TableName = @Param1)
            SET @RejectReason = 'EXISTS_IN_TABLE requires RuleParameter1 to be a staging table registered in '
                              + 'StagingTableMetadata; got "' + @Param1 + '".';
        ELSE IF @ValidationType = 'EXISTS_IN_TABLE'
                AND NOT EXISTS (SELECT 1 FROM sys.columns
                                WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@Param1))
                                  AND name = @Param2)
            SET @RejectReason = 'EXISTS_IN_TABLE requires RuleParameter2 to be a column of '
                              + @Param1 + '; got "' + ISNULL(@Param2, '(null)') + '".';
        ELSE IF @ValidationType = 'REQUIRED_IF_EQUALS'
                AND (CHARINDEX(',', @Param1) <= 1
                  OR NOT EXISTS (SELECT 1 FROM sys.columns
                                 WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                                   AND name = LEFT(@Param1, CHARINDEX(',', @Param1) - 1)))
            SET @RejectReason = 'REQUIRED_IF_EQUALS requires RuleParameter1 as "Column,Value" where Column '
                              + 'belongs to ' + @TargetTable + '; got "' + @Param1 + '".';
        ELSE IF @ValidationType = 'LENGTH_EQUALS' AND TRY_CAST(@Param1 AS INT) IS NULL
            SET @RejectReason = 'LENGTH_EQUALS requires an integer RuleParameter1; got "' + @Param1 + '".';
        ELSE IF @ValidationType IN ('GREATER_THAN_VALUE', 'LESS_THAN_VALUE')
                AND TRY_CAST(@Param1 AS DECIMAL(18,4)) IS NULL
                AND TRY_CAST(@Param1 AS DATE) IS NULL
            SET @RejectReason = @ValidationType + ' requires a numeric or date RuleParameter1; got "'
                              + @Param1 + '".';

        IF @RejectReason IS NOT NULL
        BEGIN
            INSERT INTO dbo.DB_Errors (UserName, ErrorNumber, ErrorState, ErrorSeverity, ErrorLine,
                                       ErrorProcedure, ErrorMessage, ErrorDateTime)
            VALUES (SUSER_SNAME(), 50001, 1, 16, 0, 'sp_Validation_ExecuteRules',
                    'Rule ' + CAST(@RuleId AS VARCHAR(20)) + ' skipped. ' + @RejectReason, GETDATE());

            FETCH NEXT FROM val_cursor INTO @RuleId, @TargetTable, @TargetColumn, @ValidationType, @Param1, @Param2;
            CONTINUE;
        END

        -- (b) safe identifier forms
        SET @QTable  = QUOTENAME(@TargetTable);
        SET @QColumn = QUOTENAME(@TargetColumn);
        SET @QPk     = QUOTENAME(@PrimaryKeyColumn);
        -- 055: for EXISTS_IN_TABLE, RuleParameter2 is a column of the table it looks in
        -- (RuleParameter1). Checked against the target, rules 12 and 74 never ran.
        SET @QParam2 = CASE WHEN @Param2 <> '' AND EXISTS (SELECT 1 FROM sys.columns
                                                           WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(CASE WHEN @ValidationType = 'EXISTS_IN_TABLE' THEN @Param1 ELSE @TargetTable END))
                                                             AND name = @Param2)
                            THEN QUOTENAME(@Param2) ELSE NULL END;
        -- (b2) F-08 derived identifier forms, all allow-listed above.
        SET @QParam1Col = CASE WHEN EXISTS (SELECT 1 FROM sys.columns
                                            WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                                              AND name = @Param1)
                               THEN QUOTENAME(@Param1) ELSE NULL END;
        SET @QParam1Table = CASE WHEN EXISTS (SELECT 1 FROM StagingTableMetadata WHERE TableName = @Param1)
                                 THEN QUOTENAME(@Param1) ELSE NULL END;
        SET @CommaPos    = CHARINDEX(',', @Param1);
        SET @QCondColumn = CASE WHEN @CommaPos > 1
                                 AND EXISTS (SELECT 1 FROM sys.columns
                                             WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                                               AND name = LEFT(@Param1, @CommaPos - 1))
                                THEN QUOTENAME(LEFT(@Param1, @CommaPos - 1)) ELSE NULL END;
        SET @CondValue   = CASE WHEN @CommaPos > 0
                                THEN LTRIM(RTRIM(SUBSTRING(@Param1, @CommaPos + 1, 400))) ELSE NULL END;

        -- Default Template
        SET @DynamicSQL = N'INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId)
            SELECT @FileLogId, ''' + @TargetTable + ''', S.' + @QPk + ', @RuleId
            FROM ' + @QTable + ' AS S WHERE S.FileLogId = @FileLogId AND ';

        -- Rule Logic Construction
        IF @ValidationType = 'REQUIRED' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') = ''''';
        ELSE IF @ValidationType = 'IS_NUMERIC' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND TRY_CAST(S.' + @QColumn + ' AS DECIMAL(18,2)) IS NULL';
        ELSE IF @ValidationType = 'IS_DATE' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND TRY_CAST(S.' + @QColumn + ' AS DATE) IS NULL';
        ELSE IF @ValidationType = 'MAX_LENGTH' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND LEN(S.' + @QColumn + ') > ' + CAST(TRY_CAST(@Param1 AS INT) AS NVARCHAR(20));
        ELSE IF @ValidationType = 'MIN_LENGTH' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND LEN(S.' + @QColumn + ') < ' + CAST(TRY_CAST(@Param1 AS INT) AS NVARCHAR(20));
        ELSE IF @ValidationType = 'REGEX_MATCH' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND S.' + @QColumn + ' NOT LIKE ''' + @Param1 + '''';
        ELSE IF @ValidationType = 'REGEX_CONTAINS' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND S.' + @QColumn + ' LIKE ''' + @Param1 + '''';
        ELSE IF @ValidationType = 'IN_LIST' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND S.' + @QColumn + ' NOT IN (SELECT value FROM STRING_SPLIT(''' + @Param1 + ''', '',''))';


        /* ==== F-08: handlers for the eight types that previously fell through ====
           Each was already seeded and active, so the file was reported clean while
           these checks produced nothing. Fifteen of the nineteen are Error
           severity, which is what let such a file reach approval. */

        -- Target must not be earlier than the column named in RuleParameter1.
        -- Dates and numbers both, decided per row: all five seeded rules compare
        -- dates, but the type does not promise that.
        ELSE IF @ValidationType = 'GREATER_THAN_COLUMN'
            SET @DynamicSQL = @DynamicSQL +
                'ISNULL(S.' + @QColumn + ', '''') <> '''' AND ISNULL(S.' + @QParam1Col + ', '''') <> '''' AND ((' +
                'TRY_CAST(S.' + @QColumn + ' AS DATE) IS NOT NULL AND TRY_CAST(S.' + @QParam1Col + ' AS DATE) IS NOT NULL ' +
                'AND TRY_CAST(S.' + @QColumn + ' AS DATE) < TRY_CAST(S.' + @QParam1Col + ' AS DATE)) OR (' +
                'TRY_CAST(S.' + @QColumn + ' AS DATE) IS NULL ' +
                'AND TRY_CAST(S.' + @QColumn + ' AS DECIMAL(18,4)) IS NOT NULL ' +
                'AND TRY_CAST(S.' + @QParam1Col + ' AS DECIMAL(18,4)) IS NOT NULL ' +
                'AND TRY_CAST(S.' + @QColumn + ' AS DECIMAL(18,4)) < TRY_CAST(S.' + @QParam1Col + ' AS DECIMAL(18,4))))';

        -- Target value must appear in RuleParameter2 of the table named in
        -- RuleParameter1, within the same upload.
        ELSE IF @ValidationType = 'EXISTS_IN_TABLE'
            SET @DynamicSQL = @DynamicSQL +
                'ISNULL(S.' + @QColumn + ', '''') <> '''' AND NOT EXISTS (SELECT 1 FROM ' + @QParam1Table + ' X ' +
                'WHERE X.FileLogId = S.FileLogId ' +
                'AND LTRIM(RTRIM(ISNULL(X.' + @QParam2 + ', ''''))) = LTRIM(RTRIM(S.' + @QColumn + ')))';

        -- RuleParameter1 is "Column,Value": target is required when that column
        -- holds that value. Compared case-insensitively and trimmed, because the
        -- values come from a spreadsheet.
        ELSE IF @ValidationType = 'REQUIRED_IF_EQUALS'
            SET @DynamicSQL = @DynamicSQL +
                'UPPER(LTRIM(RTRIM(ISNULL(S.' + @QCondColumn + ', '''')))) = UPPER(''' + @CondValue + ''') ' +
                'AND ISNULL(LTRIM(RTRIM(S.' + @QColumn + ')), '''') = ''''';

        -- Target is required when the column named in RuleParameter1 is empty.
        -- The seeded rule reads "provide either an SSN or a date of birth", so
        -- the error is both being absent.
        ELSE IF @ValidationType = 'REQUIRED_IF_NULL'
            SET @DynamicSQL = @DynamicSQL +
                'ISNULL(LTRIM(RTRIM(S.' + @QParam1Col + ')), '''') = '''' ' +
                'AND ISNULL(LTRIM(RTRIM(S.' + @QColumn + ')), '''') = ''''';

        -- Digits only: the seeded SSN rule states that dashes are ignored.
        ELSE IF @ValidationType = 'LENGTH_EQUALS'
            SET @DynamicSQL = @DynamicSQL +
                'ISNULL(S.' + @QColumn + ', '''') <> '''' AND LEN(REPLACE(REPLACE(REPLACE(S.' + @QColumn +
                ', ''-'', ''''), '' '', ''''), CHAR(9), '''')) <> ' + CAST(TRY_CAST(@Param1 AS INT) AS NVARCHAR(20));

        -- Numeric or date threshold, whichever RuleParameter1 parses as.
        ELSE IF @ValidationType = 'GREATER_THAN_VALUE'
            SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND ' +
                CASE WHEN TRY_CAST(@Param1 AS DECIMAL(18,4)) IS NOT NULL
                     THEN 'TRY_CAST(S.' + @QColumn + ' AS DECIMAL(18,4)) > ' + @Param1
                     ELSE 'TRY_CAST(S.' + @QColumn + ' AS DATE) > ''' + @Param1 + '''' END;

        ELSE IF @ValidationType = 'LESS_THAN_VALUE'
            SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND ' +
                CASE WHEN TRY_CAST(@Param1 AS DECIMAL(18,4)) IS NOT NULL
                     THEN 'TRY_CAST(S.' + @QColumn + ' AS DECIMAL(18,4)) < ' + @Param1
                     ELSE 'TRY_CAST(S.' + @QColumn + ' AS DATE) < ''' + @Param1 + '''' END;

        -- Informational: reports that the column is absent. The seeded rule flags
        -- dependents identified by date of birth rather than SSN.
        ELSE IF @ValidationType = 'IS_NULL'
            SET @DynamicSQL = @DynamicSQL + 'ISNULL(LTRIM(RTRIM(S.' + @QColumn + ')), '''') = ''''';
        ELSE IF @ValidationType = 'IS_UNIQUE' BEGIN
            SET @DynamicSQL = N';WITH Duplicates AS (
                SELECT ' + @QPk + ',
                COUNT(1) OVER(PARTITION BY ' + @QColumn + ') AS total_cnt
                FROM ' + @QTable + '
                WHERE FileLogId = @FileLogId AND ISNULL(' + @QColumn + ', '''') <> ''''
            )
            INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId)
            SELECT @FileLogId, ''' + @TargetTable + ''', ' + @QPk + ', @RuleId
            FROM Duplicates
            WHERE total_cnt > 1';
        END
        -- Custom Logic Blocks
        ELSE IF @ValidationType = 'CUSTOM_LOGIC' BEGIN
             IF @Param1 = 'Status_ActiveWithTermDate'
                SET @DynamicSQL = @DynamicSQL + 'TRY_CAST(S.TerminationDate AS DATE) IS NOT NULL AND (ISNULL(S.StatusEndDate, '''') = '''' OR TRY_CAST(S.StatusEndDate AS DATE) > TRY_CAST(S.TerminationDate AS DATE))';   -- 055: Status is 0/1, never ''Active''
             ELSE IF @Param1 = 'Affiliates_MustHaveEmployees' SET @DynamicSQL = @DynamicSQL + 'NOT EXISTS (SELECT 1 FROM Staging_Employees E WHERE E.FileLogId = S.FileLogId AND E.EINAssociatedWithEE = S.AffiliatedEIN)';
             ELSE IF @Param1 = 'AssociatedEIN_In_EmployerTab'   -- 055: rule 74; the main employer's own EIN counts too
                SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.EINAssociatedWithEE, '''') <> '''' AND NOT EXISTS (SELECT 1 FROM Staging_Employers X WHERE X.FileLogId = S.FileLogId AND REPLACE(LTRIM(RTRIM(S.EINAssociatedWithEE)), ''-'', '''') IN (REPLACE(LTRIM(RTRIM(ISNULL(X.AffiliatedEIN, ''''))), ''-'', ''''), REPLACE(LTRIM(RTRIM(ISNULL(X.PrimaryEIN, ''''))), ''-'', '''')))';
             ELSE IF @Param1 = 'SSN_InvalidStructure' SET @DynamicSQL = @DynamicSQL + '(S.' + @QColumn + ' LIKE ''000%'' OR S.' + @QColumn + ' LIKE ''666%'' OR S.' + @QColumn + ' LIKE ''9%'')';
             ELSE IF @Param1 = 'Premium_PercentIfBanded' SET @DynamicSQL = @DynamicSQL + 'S.[BandingType] = ''MONTHLY WITH PERCENT'' AND CHARINDEX(''%'', S.' + @QColumn + ') = 0';
             ELSE IF @Param1 = 'Dependent_On_FullyInsured_Plan' SET @DynamicSQL = @DynamicSQL + 'EXISTS (SELECT 1 FROM Staging_Employees E JOIN Staging_Plans P ON E.LowestCostPlanOffered = P.PlanName AND E.FileLogId = P.FileLogId WHERE E.EmployeeSSN = S.EmployeeSSN AND E.FileLogId = S.FileLogId AND P.FundingType = ''1'')';

             -- [NEW RULE ADDED HERE FOR REHIRES / OVERLAPPING SSN DATES]
             ELSE IF @Param1 = 'Check_Valid_Rehire_Timeline'
             SET @DynamicSQL = @DynamicSQL + 'EXISTS (SELECT 1 FROM ' + @QTable + ' S2 WHERE S2.FileLogId = S.FileLogId AND S2.EmployeeSSN = S.EmployeeSSN AND S2.' + @QPk + ' <> S.' + @QPk + ' AND ISDATE(S.StatusStartDate)=1 AND ISDATE(S2.StatusStartDate)=1 AND CAST(S.StatusStartDate AS DATE) <= CAST(ISNULL(S2.StatusEndDate, ''2099-12-31'') AS DATE) AND CAST(ISNULL(S.StatusEndDate, ''2099-12-31'') AS DATE) >= CAST(S2.StatusStartDate AS DATE))';

             ELSE IF @Param1 = 'DateEligible_Matches_WaitingPeriod'
             SET @DynamicSQL = @DynamicSQL + 'EXISTS (
                 SELECT 1 FROM Staging_Plans P
                 WHERE P.FileLogId = S.FileLogId
                 AND P.PlanName = S.LowestCostPlanOffered
                 AND ISDATE(S.DateEligibleForCoverage) = 1
                 AND ISDATE(S.HireDate) = 1
                 -- EXEMPTION 1: Ignore Legacy Employees (Hired > 1 year before Eligibility)
                 AND DATEDIFF(DAY, CAST(S.HireDate AS DATE), CAST(S.DateEligibleForCoverage AS DATE)) < 365
                 -- EXEMPTION 2: Ignore January 1st rollovers
                 AND NOT (MONTH(CAST(S.DateEligibleForCoverage AS DATE)) = 1 AND DAY(CAST(S.DateEligibleForCoverage AS DATE)) = 1)
                 -- VIOLATIONS
                 AND (
                     DATEDIFF(DAY, CAST(S.HireDate AS DATE), CAST(S.DateEligibleForCoverage AS DATE)) > 90
                     OR
                     DATEDIFF(DAY, CAST(S.HireDate AS DATE), CAST(S.DateEligibleForCoverage AS DATE)) > (ISNULL(TRY_CAST(P.WaitingPeriodNumberOfDays AS INT), 0) + 31)
                 )
             )';
             ELSE IF @Param1 = 'COLUMNS_NOT_EQUAL' SET @DynamicSQL = @DynamicSQL + 'S.' + @QColumn + ' = S.' + @QParam2;
             ELSE IF @Param1 = 'SSN_UnderOtherEmployer'
                /* 066: the SSN already belongs to an employee of another employer for the
                   file's plan year. Employers named in the file (primary or affiliated EIN)
                   and the file's own employer are not "another employer". */
                SET @DynamicSQL = @DynamicSQL +
                    'ISNULL(S.EmployeeSSN, '''') <> '''' ' +
                    'AND EXISTS (SELECT 1 FROM dbo.Employee E ' +
                    '    JOIN dbo.Employer R ON R.id = E.EmployerId ' +
                    '    JOIN dbo.UploadedFileLog L ON L.FileLogId = @FileLogId ' +
                    '    WHERE E.ssn IN (LTRIM(RTRIM(S.EmployeeSSN)), STUFF(STUFF(REPLACE(LTRIM(RTRIM(S.EmployeeSSN)), ''-'', ''''), 6, 0, ''-''), 4, 0, ''-'')) ' +
                    '      AND ISNULL(E.IsDeleted, 0) = 0 AND ISNULL(R.IsDeleted, 0) = 0 ' +
                    '      AND R.filingYear = L.PlanYear ' +
                    '      AND REPLACE(ISNULL(R.taxid, ''''), ''-'', '''') <> REPLACE(ISNULL(L.TaxID, ''''), ''-'', '''') ' +
                    '      AND NOT EXISTS (SELECT 1 FROM Staging_Employers X WHERE X.FileLogId = @FileLogId ' +
                    '            AND REPLACE(ISNULL(R.taxid, ''''), ''-'', '''') IN (REPLACE(ISNULL(X.PrimaryEIN, ''''), ''-'', ''''), REPLACE(ISNULL(X.AffiliatedEIN, ''''), ''-'', ''''))))';
             ELSE IF @Param1 = 'SSN_StartsWith9' SET @DynamicSQL = @DynamicSQL + 'S.' + @QColumn + ' LIKE ''9%''';
             ELSE IF @Param1 = 'CheckZipStateMismatch'   -- 055: every state, from dbo.ZipPrefixState (was NY, CA, TX, FL and IL by first digit)
                SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.ForeignAddress, ''0'') NOT IN (''1'', ''Y'', ''YES'', ''TRUE'') AND LEN(LTRIM(S.Zip)) >= 5 AND LEFT(LTRIM(S.Zip), 3) NOT LIKE ''%[^0-9]%'' AND EXISTS (SELECT 1 FROM dbo.ZipPrefixState Z WHERE Z.Prefix = LEFT(LTRIM(S.Zip), 3)) AND EXISTS (SELECT 1 FROM dbo.ZipPrefixState Z WHERE Z.State = UPPER(LTRIM(RTRIM(S.StateOrProvince)))) AND NOT EXISTS (SELECT 1 FROM dbo.ZipPrefixState Z WHERE Z.Prefix = LEFT(LTRIM(S.Zip), 3) AND Z.State = UPPER(LTRIM(RTRIM(S.StateOrProvince))))';
             ELSE IF @Param1 = 'CheckDateOverlap' SET @DynamicSQL = N'INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId) SELECT @FileLogId, ''' + @TargetTable + ''', S1.' + @QPk + ', @RuleId FROM ' + @QTable + ' S1 WHERE S1.FileLogId = @FileLogId AND EXISTS (SELECT 1 FROM ' + @QTable + ' S2 WHERE S2.FileLogId = S1.FileLogId AND S2.EmployeeSSN = S1.EmployeeSSN AND S2.' + @QPk + ' <> S1.' + @QPk + ' AND ISDATE(S1.CoverageStartDate)=1 AND ISDATE(S2.CoverageStartDate)=1 AND CAST(S1.CoverageStartDate AS DATE) <= CAST(ISNULL(S2.CoverageEndDate, ''2099-12-31'') AS DATE) AND CAST(ISNULL(S1.CoverageEndDate, ''2099-12-31'') AS DATE) >= CAST(S2.CoverageStartDate AS DATE))';

             ELSE IF @Param1 = 'DepStart_Before_EmpStart'
                SET @DynamicSQL = N'
                    INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId)
                    SELECT @FileLogId, ''' + @TargetTable + ''', D.' + @QPk + ', @RuleId
                    FROM ' + @QTable + ' D
                    CROSS APPLY (
                        -- An employee can hold several coverage rows (EIN transfer,
                        -- rehire, status change). Their coverage begins at the
                        -- earliest of them, not at whichever row the join reaches.
                        SELECT EarliestStart = MIN(CAST(E.CoverageStartDate AS DATE))
                        FROM   Staging_Employees E
                        WHERE  E.FileLogId  = D.FileLogId
                          AND  E.EmployeeSSN = D.EmployeeSSN
                          AND  ISDATE(E.CoverageStartDate) = 1
                    ) X
                    WHERE D.FileLogId = @FileLogId
                      AND ISDATE(D.DependentCoverageStartDate) = 1
                      AND X.EarliestStart IS NOT NULL
                      AND CAST(D.DependentCoverageStartDate AS DATE) < X.EarliestStart';

            ELSE IF @Param1 = 'DepEnd_After_EmpEnd'
                SET @DynamicSQL = N'
                    INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId)
                    SELECT @FileLogId, ''' + @TargetTable + ''', D.' + @QPk + ', @RuleId
                    FROM ' + @QTable + ' D
                    CROSS APPLY (
                        SELECT LatestEnd = MAX(CAST(E.CoverageEndDate AS DATE)),
                               -- A blank end date means coverage is still open, so
                               -- nothing can fall after it.
                               OpenEnded = MAX(CASE WHEN ISNULL(LTRIM(RTRIM(E.CoverageEndDate)), '''') = ''''
                                                    THEN 1 ELSE 0 END)
                        FROM   Staging_Employees E
                        WHERE  E.FileLogId  = D.FileLogId
                          AND  E.EmployeeSSN = D.EmployeeSSN
                          AND  ISNULL(LTRIM(RTRIM(E.CoverageStartDate)), '''') <> ''''
                    ) X
                    WHERE D.FileLogId = @FileLogId
                      AND ISDATE(D.DependentCoverageEndDate) = 1
                      AND X.OpenEnded = 0
                      AND X.LatestEnd IS NOT NULL
                      AND CAST(D.DependentCoverageEndDate AS DATE) > X.LatestEnd';

        
		ELSE IF @Param1 = 'CheckAffordability'
                SET @DynamicSQL = N'
                    -- Affordability percentage comes from FilingYear for the file''s
                    -- plan year, not a literal. 9.12% was the 2023 figure.
                    DECLARE @Pct DECIMAL(10,5) = (
                        SELECT fy.fpgPercent
                        FROM   dbo.UploadedFileLog fl
                        JOIN   dbo.FilingYear      fy ON fy.filingYear = fl.PlanYear
                        WHERE  fl.FileLogId = @FileLogId);

                    IF @Pct IS NULL SET @Pct = 0.0902;   -- last known good, if PlanYear is unset

                    -- 055: ages and years of service for age / years-of-service bands, on 1 January of the plan year.
                    DECLARE @Ref DATE = DATEFROMPARTS(ISNULL((SELECT PlanYear FROM dbo.UploadedFileLog WHERE FileLogId = @FileLogId), YEAR(GETDATE())), 1, 1);

                    INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId)
                    SELECT DISTINCT @FileLogId, ''Staging_Employees'', S.StagingEmployeeId, @RuleId
                    FROM   Staging_Employees S
                    CROSS APPLY (SELECT TRY_CAST(S.EmployeeBirthdate AS DATE) AS Birth, TRY_CAST(S.HireDate AS DATE) AS Hired) D
                    CROSS APPLY (SELECT CAST(DATEDIFF(YEAR, D.Birth, @Ref) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, D.Birth, @Ref), D.Birth) > @Ref THEN 1 ELSE 0 END AS DECIMAL(18,2)) AS AgeAtStart,
                                        CAST(DATEDIFF(YEAR, D.Hired, @Ref) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, D.Hired, @Ref), D.Hired) > @Ref THEN 1 ELSE 0 END AS DECIMAL(18,2)) AS YearsAtStart) X
                    JOIN   Staging_Premiums  P
                           ON  P.FileLogId = S.FileLogId
                           AND P.PlanName  = S.LowestCostPlanOffered
                           -- Match the employee to the band that actually applies to
                           -- them. Without this, a salary-banded plan tests every
                           -- employee against every band.
                           AND (
                                 UPPER(LTRIM(RTRIM(ISNULL(P.BandingType, '''')))) NOT IN
                                     (''AGE'', ''SALARY'', ''HOURLY'', ''YEARS OF SERVICE'')
                                 OR (
                                     TRY_CAST(P.[Start] AS DECIMAL(18,2)) IS NOT NULL
                                     -- 055: was the salary for every kind of band; ages, hourly rates and years of service are not salaries.
                                     AND (CASE UPPER(LTRIM(RTRIM(P.BandingType)))
                                               WHEN ''SALARY'' THEN TRY_CAST(S.W2IncomeOrAnnualSalary AS DECIMAL(18,2))
                                               WHEN ''HOURLY'' THEN TRY_CAST(S.HourlyRate AS DECIMAL(18,2))
                                               WHEN ''AGE'' THEN X.AgeAtStart
                                               WHEN ''YEARS OF SERVICE'' THEN X.YearsAtStart END)
                                         BETWEEN TRY_CAST(P.[Start] AS DECIMAL(18,2))
                                             AND TRY_CAST(P.[End]   AS DECIMAL(18,2))
                                 )
                               )
                    WHERE  S.FileLogId = @FileLogId
                      AND  TRY_CAST(S.W2IncomeOrAnnualSalary AS DECIMAL(18,2)) > 0
                      AND  TRY_CAST(P.EEMonthlyContribution AS DECIMAL(18,2)) IS NOT NULL
                      AND  (
                            CASE
                                -- Percent-banded: the contribution IS the percentage.
                                WHEN UPPER(LTRIM(RTRIM(ISNULL(P.BandingType, '''')))) = ''MONTHLY WITH PERCENT''
                                     THEN CASE WHEN TRY_CAST(P.EEMonthlyContribution AS DECIMAL(18,2)) / 100.0 > @Pct
                                               THEN 1 ELSE 0 END
                                -- Everything else: annualise the dollar contribution.
                                ELSE CASE WHEN (TRY_CAST(P.EEMonthlyContribution AS DECIMAL(18,2)) * 12)
                                             / TRY_CAST(S.W2IncomeOrAnnualSalary AS DECIMAL(18,2)) > @Pct
                                          THEN 1 ELSE 0 END
                            END
                           ) = 1';
             ELSE IF @Param1 = 'CheckCrossEmployeeDependents' SET @DynamicSQL = N';WITH DepCounts AS (SELECT DependentSSN, COUNT(*) as Cnt FROM Staging_Dependents WHERE FileLogId = @FileLogId AND ISNULL(DependentSSN, '''') <> '''' GROUP BY DependentSSN) INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId) SELECT @FileLogId, ''' + @TargetTable + ''', S.StagingDependentId, @RuleId FROM Staging_Dependents S JOIN DepCounts D ON S.DependentSSN = D.DependentSSN WHERE S.FileLogId = @FileLogId AND D.Cnt > 1';
             ELSE IF @Param1 = 'CheckSelfFundedDependents'
                /* F-08: the literals here were double-escaped, as if this block had
                   been copied out of a nested dynamic-SQL context. It emitted
                   FundingType = ''2'' -- an empty string, a stray 2, another empty
                   string -- which is a syntax error, so every run of this rule threw
                   and was swallowed by the per-rule CATCH below. Single-escaped now. */
                SET @DynamicSQL = @DynamicSQL +
                    'S.[FundingType] = ''2'' ' +
                    -- COBRA and Retiree plans are never a LowestCostPlanOffered, so
                    -- the NOT EXISTS below is always true for them.
                    'AND UPPER(LTRIM(RTRIM(ISNULL(S.PlanType, '''')))) = ''MEDICAL'' ' +
                    'AND NOT EXISTS (SELECT 1 FROM Staging_Employees E ' +
                    '    JOIN Staging_Dependents D ON E.EmployeeSSN = D.EmployeeSSN AND E.FileLogId = D.FileLogId ' +
                    '    WHERE E.LowestCostPlanOffered = S.PlanName AND E.FileLogId = S.FileLogId)';
             ELSE IF @Param1 = 'Cannot_Be_Negative'
                SET @DynamicSQL = @DynamicSQL + 'ISNUMERIC(S.' + @QColumn + ') = 1 AND CAST(S.' + @QColumn + ' AS DECIMAL(18,2)) < 0';

             ELSE IF @Param1 = 'PlanName_Must_Exist'
                SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND NOT EXISTS (SELECT 1 FROM Staging_Plans P WHERE P.FileLogId = S.FileLogId AND P.PlanName = S.' + @QColumn + ')';

             ELSE IF @Param1 = 'Start_After_End'
                SET @DynamicSQL = @DynamicSQL + 'ISDATE(S.StartDate) = 1 AND ISDATE(S.EndDate) = 1 AND CAST(S.StartDate AS DATE) > CAST(S.EndDate AS DATE)';

             ELSE IF @Param1 = 'Emp_Start_After_End'
                SET @DynamicSQL = @DynamicSQL + 'ISDATE(S.CoverageStartDate) = 1 AND ISDATE(S.CoverageEndDate) = 1 AND CAST(S.CoverageStartDate AS DATE) > CAST(S.CoverageEndDate AS DATE)';

             ELSE IF @Param1 = 'Month_1_to_12'
                SET @DynamicSQL = @DynamicSQL + '(ISNUMERIC(S.' + @QColumn + ') = 0 OR CAST(S.' + @QColumn + ' AS INT) NOT BETWEEN 1 AND 12)';

             ELSE IF @Param1 = 'Valid_US_ZIP'
                SET @DynamicSQL = @DynamicSQL + '(ISNULL(S.Country, '''') = ''USA'' OR ISNULL(S.Country, '''') = '''') AND S.' + @QColumn + ' NOT LIKE ''[0-9][0-9][0-9][0-9][0-9]'' AND S.' + @QColumn + ' NOT LIKE ''[0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9]''';

             ELSE IF @Param1 = 'CheckDependentAge26'
                /* F-08: this had the same double-escaping fault as
                   CheckSelfFundedDependents, and additionally emitted the literal
                   text  S.' + @QColumn + '  because the concatenation was inside the
                   string rather than around it. Both fixed. The column is
                   DependentBirthdate, and the test is age at 31 December of the
                   plan year, which is how the age-26 rule is applied. */
                SET @DynamicSQL = @DynamicSQL +
                    'ISNULL(LTRIM(RTRIM(S.Relationship)), '''') NOT IN ('''', ''Spouse'', ''Domestic Partner'') ' +
                    'AND TRY_CAST(S.' + @QColumn + ' AS DATE) IS NOT NULL ' +
                    'AND DATEDIFF(YEAR, TRY_CAST(S.' + @QColumn + ' AS DATE), ' +
                    '    DATEFROMPARTS(ISNULL((SELECT PlanYear FROM UploadedFileLog WHERE FileLogId = @FileLogId), YEAR(GETDATE())), 12, 31)) >= 26';

             ELSE IF @Param1 = 'DependentCoverage_Within_EmployeeCoverage'
                /* F-08: no handler existed for this keyword, so the rule was
                   skipped. Complements DepStart_Before_EmpStart and
                   DepEnd_After_EmpEnd by catching the case those two cannot: a
                   dependent carrying coverage for an employee who has none. */
                SET @DynamicSQL = @DynamicSQL +
                    'ISNULL(LTRIM(RTRIM(S.DependentCoverageStartDate)), '''') <> '''' ' +
                    'AND NOT EXISTS (SELECT 1 FROM Staging_Employees E ' +
                    '    WHERE E.FileLogId = S.FileLogId AND E.EmployeeSSN = S.EmployeeSSN ' +
                    '      AND ISNULL(LTRIM(RTRIM(E.CoverageStartDate)), '''') <> '''')';
             ELSE
             BEGIN
                -- (f) unrecognised CUSTOM_LOGIC keyword: does not fire, but say so
                SET @ShouldExecute = 0;
                INSERT INTO dbo.DB_Errors (UserName, ErrorNumber, ErrorState, ErrorSeverity, ErrorLine,
                                           ErrorProcedure, ErrorMessage, ErrorDateTime)
                VALUES (SUSER_SNAME(), 50002, 1, 10, 0, 'sp_Validation_ExecuteRules',
                        'Rule ' + CAST(@RuleId AS VARCHAR(20)) + ' did not run: no handler for CUSTOM_LOGIC keyword "' + @Param1 + '".', GETDATE());
             END
        END
        ELSE
        BEGIN
            -- (f) unrecognised ValidationType. 19 seeded rules land here today.
            SET @ShouldExecute = 0;
            INSERT INTO dbo.DB_Errors (UserName, ErrorNumber, ErrorState, ErrorSeverity, ErrorLine,
                                       ErrorProcedure, ErrorMessage, ErrorDateTime)
            VALUES (SUSER_SNAME(), 50003, 1, 10, 0, 'sp_Validation_ExecuteRules',
                    'Rule ' + CAST(@RuleId AS VARCHAR(20)) + ' did not run: no handler for ValidationType "' + @ValidationType + '".', GETDATE());
        END

        -- 055: a rule whose SQL came out empty (a parameter naming no column) used to be skipped silently.
        IF @ShouldExecute = 1 AND @DynamicSQL IS NULL
            INSERT INTO dbo.DB_Errors (UserName, ErrorNumber, ErrorState, ErrorSeverity, ErrorLine,
                                       ErrorProcedure, ErrorMessage, ErrorDateTime)
            VALUES (SUSER_SNAME(), 50004, 1, 10, 0, 'sp_Validation_ExecuteRules',
                    'Rule ' + CAST(@RuleId AS VARCHAR(20)) + ' did not run: its SQL could not be built (check its parameters).', GETDATE());
        IF @ShouldExecute = 1 AND @DynamicSQL IS NOT NULL
        BEGIN
            -- Critical optimization: Force Single Core for INSERTs to avoid CXSYNC_PORT
            SET @DynamicSQL = @DynamicSQL + ' OPTION (MAXDOP 1)';
            -- 067: a trial run never touches the file's real errors
            IF @WhatIfRunId IS NOT NULL
                SET @DynamicSQL = REPLACE(@DynamicSQL, 'INSERT INTO StagingRowErrors (', 'INSERT INTO dbo.RuleWhatIfHits (');

            -- (g) per-rule isolation. Remove this TRY/CATCH to restore fail-fast.
            BEGIN TRY
                EXEC sp_executesql @DynamicSQL, N'@FileLogId INT, @RuleId INT', @FileLogId, @RuleId;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() = -1 THROW;
                INSERT INTO dbo.DB_Errors (UserName, ErrorNumber, ErrorState, ErrorSeverity, ErrorLine,
                                           ErrorProcedure, ErrorMessage, ErrorDateTime)
                VALUES (SUSER_SNAME(), ERROR_NUMBER(), ERROR_STATE(), ERROR_SEVERITY(), ERROR_LINE(),
                        'sp_Validation_ExecuteRules',
                        'Rule ' + CAST(@RuleId AS VARCHAR(20)) + ' failed: ' + ERROR_MESSAGE(), GETDATE());
            END CATCH
        END

        FETCH NEXT FROM val_cursor INTO @RuleId, @TargetTable, @TargetColumn, @ValidationType, @Param1, @Param2;
    END
    CLOSE val_cursor; DEALLOCATE val_cursor;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_Validation_RuleWhatIf
    @UserId         INT,
    @ExistingRuleId INT          = NULL,   -- the saved rule being changed, to compare against
    @TargetTable    VARCHAR(100),
    @TargetColumn   VARCHAR(100),
    @ValidationType VARCHAR(50),
    @RuleParameter1 VARCHAR(500) = NULL,
    @RuleParameter2 VARCHAR(500) = NULL,
    @MaxFiles       INT          = 20
AS
BEGIN
    SET NOCOUNT ON;
    SET @MaxFiles = CASE WHEN @MaxFiles BETWEEN 1 AND 100 THEN @MaxFiles ELSE 20 END;

    DECLARE @Problems TABLE (Message NVARCHAR(4000));
    DECLARE @Pk SYSNAME = (SELECT PrimaryKeyColumn FROM dbo.StagingTableMetadata WHERE TableName = @TargetTable);

    IF @Pk IS NULL
        INSERT INTO @Problems VALUES (N'"' + ISNULL(@TargetTable, '') + N'" is not a staging table.');
    ELSE IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable)) AND name = @TargetColumn)
        INSERT INTO @Problems VALUES (N'"' + ISNULL(@TargetColumn, '') + N'" is not a column of ' + @TargetTable + N'.');

    IF EXISTS (SELECT 1 FROM @Problems)
    BEGIN
        SELECT Message FROM @Problems;
        SELECT CAST(NULL AS INT) AS FileLogId WHERE 1 = 0;
        SELECT CAST(NULL AS INT) AS FileLogId WHERE 1 = 0;
        RETURN;
    END

    DECLARE @RunId INT, @Started DATETIME = DATEADD(SECOND, -1, GETDATE());
    INSERT INTO dbo.RuleWhatIfRuns (TargetTable, TargetColumn, ValidationType, RuleParameter1, RuleParameter2, CreatedBy)
    VALUES (@TargetTable, @TargetColumn, @ValidationType, @RuleParameter1, @RuleParameter2, @UserId);
    SET @RunId = SCOPE_IDENTITY();

    BEGIN TRY
        -- The most recent files the user may see that still have rows in the target table.
        CREATE TABLE #Files (FileLogId INT PRIMARY KEY, TotalRows INT NOT NULL);
        DECLARE @Sql NVARCHAR(MAX) = N'
            INSERT INTO #Files (FileLogId, TotalRows)
            SELECT TOP (@MaxFiles) S.FileLogId, COUNT(*)
            FROM ' + QUOTENAME(@TargetTable) + N' S
            JOIN dbo.UploadedFileLog L ON L.FileLogId = S.FileLogId AND ISNULL(L.IsDeleted, 0) = 0
            CROSS APPLY dbo.fn_FileVisibleToUser(S.FileLogId, @UserId) V
            WHERE V.IsVisible = 1
            GROUP BY S.FileLogId
            ORDER BY S.FileLogId DESC;';
        EXEC sp_executesql @Sql, N'@MaxFiles INT, @UserId INT', @MaxFiles, @UserId;

        DECLARE @F INT;
        DECLARE files CURSOR LOCAL FAST_FORWARD FOR SELECT FileLogId FROM #Files ORDER BY FileLogId DESC;
        OPEN files;
        FETCH NEXT FROM files INTO @F;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            EXEC dbo.sp_Validation_ExecuteRules @FileLogId = @F, @WhatIfRunId = @RunId;
            FETCH NEXT FROM files INTO @F;
        END
        CLOSE files; DEALLOCATE files;

        -- What the engine said about the trial rule (skipped, could not be built, failed).
        INSERT INTO @Problems (Message)
        SELECT DISTINCT REPLACE(ErrorMessage, 'Rule ' + CAST(-@RunId AS VARCHAR(20)) + ' ', 'The rule ')
        FROM   dbo.DB_Errors
        WHERE  ErrorProcedure = 'sp_Validation_ExecuteRules'
          AND  ErrorDateTime >= @Started
          AND  ErrorMessage LIKE 'Rule ' + CAST(-@RunId AS VARCHAR(20)) + ' %';
        DELETE FROM dbo.DB_Errors
        WHERE  ErrorProcedure = 'sp_Validation_ExecuteRules'
          AND  ErrorDateTime >= @Started
          AND  ErrorMessage LIKE 'Rule ' + CAST(-@RunId AS VARCHAR(20)) + ' %';

        SELECT Message FROM @Problems;

        ;WITH Hits AS (
            SELECT DISTINCT FileLogId, StagingRowId FROM dbo.RuleWhatIfHits
            WHERE RuleId = -@RunId AND StagingTableName = @TargetTable
        ), NowRows AS (
            SELECT DISTINCT E.FileLogId, E.StagingRowId FROM dbo.StagingRowErrors E
            JOIN #Files F ON F.FileLogId = E.FileLogId
            WHERE E.RuleId = @ExistingRuleId AND E.StagingTableName = @TargetTable
        )
        SELECT F.FileLogId, L.OriginalFileName AS FileName, L.EmployerName, L.PlanYear, L.UploadedAt,
               F.TotalRows,
               (SELECT COUNT(*) FROM NowRows N WHERE N.FileLogId = F.FileLogId) AS NowFlagged,
               (SELECT COUNT(*) FROM Hits H WHERE H.FileLogId = F.FileLogId)    AS WouldFlag,
               (SELECT COUNT(*) FROM Hits H WHERE H.FileLogId = F.FileLogId
                  AND NOT EXISTS (SELECT 1 FROM NowRows N WHERE N.FileLogId = H.FileLogId AND N.StagingRowId = H.StagingRowId)) AS NewlyFlagged,
               (SELECT COUNT(*) FROM NowRows N WHERE N.FileLogId = F.FileLogId
                  AND NOT EXISTS (SELECT 1 FROM Hits H WHERE H.FileLogId = N.FileLogId AND H.StagingRowId = N.StagingRowId)) AS NoLongerFlagged
        FROM   #Files F
        JOIN   dbo.UploadedFileLog L ON L.FileLogId = F.FileLogId
        ORDER  BY F.FileLogId DESC;

        -- A sample of the rows the trial flags. SSN columns are shown by their last four digits.
        SET @Sql = N'
            SELECT TOP 25 H.FileLogId, S.RowNumber,
                   CASE WHEN @Column LIKE ''%SSN%'' AND LEN(CAST(S.' + QUOTENAME(@TargetColumn) + N' AS NVARCHAR(400))) >= 4
                        THEN ''XXX-XX-'' + RIGHT(CAST(S.' + QUOTENAME(@TargetColumn) + N' AS NVARCHAR(400)), 4)
                        ELSE CAST(S.' + QUOTENAME(@TargetColumn) + N' AS NVARCHAR(400)) END AS Value,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.StagingRowErrors E WHERE E.RuleId = @ExistingRuleId
                                          AND E.FileLogId = H.FileLogId AND E.StagingTableName = @Table
                                          AND E.StagingRowId = H.StagingRowId) THEN 1 ELSE 0 END AS BIT) AS NowFlagged
            FROM (SELECT DISTINCT FileLogId, StagingRowId FROM dbo.RuleWhatIfHits
                  WHERE RuleId = -@RunId AND StagingTableName = @Table) H
            JOIN ' + QUOTENAME(@TargetTable) + N' S ON S.' + QUOTENAME(@Pk) + N' = H.StagingRowId
            ORDER BY H.FileLogId DESC, S.RowNumber;';
        EXEC sp_executesql @Sql, N'@RunId INT, @ExistingRuleId INT, @Table NVARCHAR(128), @Column SYSNAME',
                           @RunId, @ExistingRuleId, @TargetTable, @TargetColumn;
    END TRY
    BEGIN CATCH
        DELETE FROM dbo.RuleWhatIfHits WHERE RuleId = -@RunId;
        DELETE FROM dbo.RuleWhatIfRuns WHERE RunId = @RunId;
        THROW;
    END CATCH

    DELETE FROM dbo.RuleWhatIfHits WHERE RuleId = -@RunId;
    DELETE FROM dbo.RuleWhatIfRuns WHERE RunId = @RunId;
END
GO

PRINT '067: rule what-if (sp_Validation_RuleWhatIf).';
GO
