/* =============================================================================
   006_F-08_validation_rules_that_never_fire.sql

   FIXES   F-08 — about one active validation rule in five produces no result
           under any input, and the file is still reported clean.

   WHY     Three separate causes, all silent:

           1. EIGHT ValidationTypes had no handler in sp_Validation_ExecuteRules.
              Nineteen active rules use them. The procedure's own comment
              acknowledged it: "19 seeded rules land here today."

                  GREATER_THAN_COLUMN  5 rules    REQUIRED_IF_NULL   1
                  EXISTS_IN_TABLE      5          LENGTH_EQUALS      1
                  REQUIRED_IF_EQUALS   3          IS_NULL            1
                  LESS_THAN_VALUE      2          GREATER_THAN_VALUE 1

              FIFTEEN of those nineteen are Error severity. A file failing them
              validates clean and moves to approval.

           2. TWO CUSTOM_LOGIC blocks built syntactically invalid SQL. Their
              literals were double-escaped, as if copied out of a nested dynamic
              SQL context, so they threw at sp_executesql every time and were
              swallowed by the per-rule CATCH.

                  CheckSelfFundedDependents  emitted  FundingType = ''2''
                  CheckDependentAge26        emitted  ISDATE(S.' + @QColumn + ')
                                             as literal text, plus the same
                                             double-escaping

           3. ONE CUSTOM_LOGIC keyword had no handler at all:
              DependentCoverage_Within_EmployeeCoverage (rule 56).

   WHAT THE NINETEEN ACTUALLY CHECK

           They are not marginal. Among them: termination before hire date,
           premium end before start, dependent coverage starting before the
           dependent was born, an employee's plan not existing on the Plan tab,
           a dependent whose employee is not on the Employee tab, a premium plan
           that does not exist, termination date missing on a terminated
           employee, coverage start missing when coverage was elected, and SSN
           length.

   WHAT    Adds handlers for all eight types, with the same identifier
           allow-listing the existing types already get — RuleParameter1 and
           RuleParameter2 reach executable SQL as identifiers, so each is checked
           against sys.columns or StagingTableMetadata before use, and a rule
           naming something that does not resolve is skipped with a reason rather
           than concatenated blindly.

           Repairs the two malformed CUSTOM_LOGIC blocks and adds the missing
           keyword.

   THE ENGINE IS STILL FAIL-OPEN

           Skipped rules are recorded in dbo.DB_Errors and nothing surfaces them
           to the analyst, so the next gap will be just as quiet as this one. That
           is a UI change rather than a migration; PART 2 gives you the query to
           run in the meantime, and it is worth putting on the triage screen.

   AFTER RUNNING THIS

           Re-validate any file still in triage. Rules that never fired will now
           produce findings, so files previously shown as clean may not be —
           that is the point, but expect the error counts to move.

               EXEC dbo.sp_ExecuteValidationEngine @FileLogId = <id>;

           PART 2 lists the files this applies to.

   SAFE TO RE-RUN
           Yes. ALTER PROCEDURE only.

   DRIFT WARNING
           Replaces the whole body of sp_Validation_ExecuteRules, reproduced from
           the 20 Aug snapshot. The diff was checked: the only lines removed are
           the CREATE header and the six malformed literal lines in the two broken
           blocks. Everything else is additive.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- PART 1 ----
   sp_Validation_ExecuteRules — handlers for the eight missing types, repairs for
   the two malformed blocks, and the missing CUSTOM_LOGIC keyword.
   ---------------------------------------------------------------------------- */
ALTER PROCEDURE [dbo].[sp_Validation_ExecuteRules] @FileLogId INT AS
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
    FROM ValidationRules WHERE IsActive = 1;

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
        SET @QParam2 = CASE WHEN @Param2 <> '' AND EXISTS (SELECT 1 FROM sys.columns
                                                           WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
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
                SET @DynamicSQL = @DynamicSQL + '((S.[Status] = ''Active'') AND ISNULL(S.TerminationDate, '''') <> '''')';
             ELSE IF @Param1 = 'Affiliates_MustHaveEmployees' SET @DynamicSQL = @DynamicSQL + 'NOT EXISTS (SELECT 1 FROM Staging_Employees E WHERE E.FileLogId = S.FileLogId AND E.EINAssociatedWithEE = S.AffiliatedEIN)';
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
             ELSE IF @Param1 = 'SSN_StartsWith9' SET @DynamicSQL = @DynamicSQL + 'S.' + @QColumn + ' LIKE ''9%''';
             ELSE IF @Param1 = 'CheckZipStateMismatch' SET @DynamicSQL = @DynamicSQL + 'LEN(S.Zip) >= 5 AND ((S.StateOrProvince = ''NY'' AND LEFT(S.Zip, 1) NOT IN (''1'', ''0'')) OR (S.StateOrProvince = ''CA'' AND LEFT(S.Zip, 1) <> ''9'') OR (S.StateOrProvince = ''TX'' AND LEFT(S.Zip, 1) <> ''7'') OR (S.StateOrProvince = ''FL'' AND LEFT(S.Zip, 1) <> ''3'') OR (S.StateOrProvince = ''IL'' AND LEFT(S.Zip, 1) <> ''6''))';
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

                    INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId)
                    SELECT DISTINCT @FileLogId, ''Staging_Employees'', S.StagingEmployeeId, @RuleId
                    FROM   Staging_Employees S
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
                                     AND TRY_CAST(S.W2IncomeOrAnnualSalary AS DECIMAL(18,2))
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

        IF @ShouldExecute = 1 AND @DynamicSQL IS NOT NULL
        BEGIN
            -- Critical optimization: Force Single Core for INSERTs to avoid CXSYNC_PORT
            SET @DynamicSQL = @DynamicSQL + ' OPTION (MAXDOP 1)';

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

/* ---------------------------------------------------------------- PART 2 ----
   Diagnostics. The first query is the one worth keeping: it answers "did every
   rule actually run", which nothing in the application currently asks.
   ---------------------------------------------------------------------------- */

PRINT '--- Rules skipped or failed on recent runs (from DB_Errors) ---';
PRINT '    Empty is the healthy result. Anything here did not produce findings.';
SELECT TOP 200
        e.ErrorDateTime,
        e.ErrorNumber,
        Reason = CASE e.ErrorNumber
                    WHEN 50001 THEN 'identifier rejected'
                    WHEN 50002 THEN 'no handler for CUSTOM_LOGIC keyword'
                    WHEN 50003 THEN 'no handler for ValidationType'
                    ELSE            'rule threw at execution'
                 END,
        e.ErrorMessage
FROM    dbo.DB_Errors e
WHERE   e.ErrorProcedure = 'sp_Validation_ExecuteRules'
ORDER BY e.ErrorDateTime DESC;

PRINT '--- ValidationTypes in use by active rules ---';
PRINT '    Cross-check against the handler chain after editing ValidationRules.';
PRINT '    A type listed here with no ELSE IF branch is a rule that does nothing.';
SELECT  vr.ValidationType,
        ActiveRules = COUNT(*),
        Errors      = SUM(CASE WHEN vr.Severity = 'Error' THEN 1 ELSE 0 END),
        Warnings    = SUM(CASE WHEN vr.Severity = 'Warning' THEN 1 ELSE 0 END),
        Other       = SUM(CASE WHEN vr.Severity NOT IN ('Error','Warning') THEN 1 ELSE 0 END)
FROM    dbo.ValidationRules vr
WHERE   vr.IsActive = 1
GROUP BY vr.ValidationType
ORDER BY COUNT(*) DESC;

PRINT '--- CUSTOM_LOGIC keywords in use by active rules ---';
SELECT  Keyword     = vr.RuleParameter1,
        ActiveRules = COUNT(*),
        Tables      = STUFF((SELECT DISTINCT ', ' + v2.TargetTable
                             FROM   dbo.ValidationRules v2
                             WHERE  v2.IsActive = 1
                               AND  v2.ValidationType = 'CUSTOM_LOGIC'
                               AND  v2.RuleParameter1 = vr.RuleParameter1
                             FOR XML PATH('')), 1, 2, '')
FROM    dbo.ValidationRules vr
WHERE   vr.IsActive = 1 AND vr.ValidationType = 'CUSTOM_LOGIC'
GROUP BY vr.RuleParameter1
ORDER BY vr.RuleParameter1;

PRINT '--- Files still in triage: re-validate these ---';
PRINT '    Rules that never fired will now produce findings, so counts will move.';
SELECT  fl.FileLogId,
        fl.OriginalFileName,
        fl.EmployerName,
        fl.PlanYear,
        fl.ValidationStatusId,
        fl.WorkflowStatusId,
        CurrentErrors = (SELECT COUNT(*) FROM dbo.StagingRowErrors sre WHERE sre.FileLogId = fl.FileLogId),
        Command       = 'EXEC dbo.sp_ExecuteValidationEngine @FileLogId = ' + CAST(fl.FileLogId AS VARCHAR(10)) + ';'
FROM    dbo.UploadedFileLog fl
WHERE   ISNULL(fl.IsDeleted, 0) = 0
  AND   fl.WorkflowStatusId BETWEEN 200 AND 249   -- in flight, not yet imported
ORDER BY fl.FileLogId;
GO
