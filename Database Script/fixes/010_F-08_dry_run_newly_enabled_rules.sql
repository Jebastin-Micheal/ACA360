/* =============================================================================
   010_F-08_dry_run_newly_enabled_rules.sql

   RUN AFTER 006.

   PURPOSE  Answer the question 006 left open: now that twenty-two dead validation
            rules actually run, what do they find in the data already imported?

   WHY THIS IS NOT A RE-VALIDATION

            006 said to re-validate files still in triage. There are none. Both
            UploadedFileLog rows sit at WorkflowStatusId 260 — "Complete. Import
            finished successfully. Terminal success state." — with
            ValidationStatusId 80 (Clean). The triage queue is empty, so the
            command list in 006 part 2 returns nothing.

            Re-validating them for real would be wrong: sp_Validation_UpdateStatus
            would recompute ValidationStatusId from the new findings and could mark
            an already-imported file as failed, which is neither true nor useful.

            But their staging rows are still present, and that staging data is what
            produced the live 2025 and 2026 filings. Running the rules against it
            says whether those filings carry problems the old engine could not see.

   HOW IT STAYS READ-ONLY

            Each file is validated inside a transaction that is always rolled back.
            Findings are captured into TABLE VARIABLES first, because those survive
            a rollback where a #temp table would not. Nothing is left behind:
            StagingRowErrors, UploadedFileLog and DB_Errors are all restored.

            If you want the findings persisted, run
                EXEC dbo.sp_ExecuteValidationEngine @FileLogId = <id>;
            directly — but see the warning above about file status.

   RUNTIME  The engine is not cheap; the application allows it an hour. Two files
            over ~1,600 staging rows should be quick, but do not run this against a
            large estate during business hours.

   SAFE TO RE-RUN
            Yes. It changes nothing by construction.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- PART 1 ----
   Confirm the triage queue really is empty before concluding anything from it.
   ---------------------------------------------------------------------------- */
PRINT '--- Files in triage (WorkflowStatusId 200-249). Expect none. ---';
SELECT  fl.FileLogId, fl.OriginalFileName, fl.EmployerName, fl.PlanYear,
        WorkflowStatus   = ws.StatusName,
        ValidationStatus = vs.StatusName
FROM    dbo.UploadedFileLog fl
LEFT JOIN dbo.WorkflowStatuses   ws ON ws.WorkflowStatusId   = fl.WorkflowStatusId
LEFT JOIN dbo.ValidationStatuses vs ON vs.ValidationStatusId = fl.ValidationStatusId
WHERE   ISNULL(fl.IsDeleted, 0) = 0
  AND   fl.WorkflowStatusId BETWEEN 200 AND 249
ORDER BY fl.FileLogId;

PRINT '--- All files and their current state ---';
SELECT  fl.FileLogId, fl.OriginalFileName, fl.EmployerName, fl.PlanYear,
        WorkflowStatus   = ws.StatusName,
        ValidationStatus = vs.StatusName,
        fl.FatalErrorCount, fl.WarningCount,
        StagingErrorsNow = (SELECT COUNT(*) FROM dbo.StagingRowErrors s WHERE s.FileLogId = fl.FileLogId)
FROM    dbo.UploadedFileLog fl
LEFT JOIN dbo.WorkflowStatuses   ws ON ws.WorkflowStatusId   = fl.WorkflowStatusId
LEFT JOIN dbo.ValidationStatuses vs ON vs.ValidationStatusId = fl.ValidationStatusId
WHERE   ISNULL(fl.IsDeleted, 0) = 0
ORDER BY fl.FileLogId;
GO

/* ---------------------------------------------------------------- PART 2 ----
   Dry run. One batch, because table variables do not cross a GO.
   ---------------------------------------------------------------------------- */
DECLARE @Baseline TABLE (FileLogId INT, RuleId INT, Rows INT);
DECLARE @After    TABLE (FileLogId INT, RuleId INT, StagingTableName NVARCHAR(128), Rows INT);
DECLARE @Skipped  TABLE (FileLogId INT, ErrorNumber INT, ErrorMessage NVARCHAR(4000));
DECLARE @Failed   TABLE (FileLogId INT, ErrorMessage NVARCHAR(4000));

DECLARE @FileLogId INT, @DbErrHighWater INT;

/* Rows already recorded, so the dry run can report only what is NEW. */
INSERT INTO @Baseline (FileLogId, RuleId, Rows)
SELECT FileLogId, RuleId, COUNT(*) FROM dbo.StagingRowErrors GROUP BY FileLogId, RuleId;

SELECT @DbErrHighWater = ISNULL(MAX(ErrorID), 0) FROM dbo.DB_Errors;

DECLARE dry_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT FileLogId FROM dbo.UploadedFileLog WHERE ISNULL(IsDeleted, 0) = 0 ORDER BY FileLogId;

OPEN dry_cursor;
FETCH NEXT FROM dry_cursor INTO @FileLogId;

WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        EXEC dbo.sp_ExecuteValidationEngine @FileLogId = @FileLogId;

        /* Captured into table variables so the rollback below cannot discard them. */
        INSERT INTO @After (FileLogId, RuleId, StagingTableName, Rows)
        SELECT @FileLogId, s.RuleId, s.StagingTableName, COUNT(*)
        FROM   dbo.StagingRowErrors s
        WHERE  s.FileLogId = @FileLogId
        GROUP BY s.RuleId, s.StagingTableName;

        INSERT INTO @Skipped (FileLogId, ErrorNumber, ErrorMessage)
        SELECT @FileLogId, e.ErrorNumber, e.ErrorMessage
        FROM   dbo.DB_Errors e
        WHERE  e.ErrorID > @DbErrHighWater
          AND  e.ErrorProcedure = 'sp_Validation_ExecuteRules';

        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        PRINT '  dry run complete for FileLogId ' + CAST(@FileLogId AS VARCHAR(10)) + ' (rolled back)';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        INSERT INTO @Failed (FileLogId, ErrorMessage) VALUES (@FileLogId, ERROR_MESSAGE());
        PRINT '  FAILED for FileLogId ' + CAST(@FileLogId AS VARCHAR(10)) + ': ' + ERROR_MESSAGE();
    END CATCH

    FETCH NEXT FROM dry_cursor INTO @FileLogId;
END

CLOSE dry_cursor;
DEALLOCATE dry_cursor;

/* ---------------------------------------------------------------- PART 3 ----
   What the previously dead rules would flag in already-imported data.
   ---------------------------------------------------------------------------- */
PRINT '';
PRINT '=== NEW findings, by rule. These are real problems in live filings. ===';
SELECT  a.FileLogId,
        fl.EmployerName,
        fl.PlanYear,
        vr.RuleCode,
        vr.RuleName,
        vr.Severity,
        vr.ValidationType,
        a.StagingTableName,
        RowsFlagged   = a.Rows,
        PreviouslyDead = CASE WHEN vr.ValidationType IN
                                  ('GREATER_THAN_COLUMN','EXISTS_IN_TABLE','REQUIRED_IF_EQUALS',
                                   'LESS_THAN_VALUE','REQUIRED_IF_NULL','LENGTH_EQUALS',
                                   'IS_NULL','GREATER_THAN_VALUE')
                               OR (vr.ValidationType = 'CUSTOM_LOGIC'
                                   AND vr.RuleParameter1 IN ('CheckDependentAge26',
                                                             'CheckSelfFundedDependents',
                                                             'DependentCoverage_Within_EmployeeCoverage'))
                             THEN 'yes' ELSE 'no' END,
        vr.ErrorMessage
FROM    @After a
JOIN    dbo.ValidationRules vr ON vr.RuleId = a.RuleId
JOIN    dbo.UploadedFileLog fl ON fl.FileLogId = a.FileLogId
LEFT JOIN @Baseline b ON b.FileLogId = a.FileLogId AND b.RuleId = a.RuleId
WHERE   ISNULL(b.Rows, 0) < a.Rows
ORDER BY CASE vr.Severity WHEN 'Error' THEN 0 WHEN 'Warning' THEN 1 ELSE 2 END,
         a.FileLogId, vr.RuleCode;

PRINT '';
PRINT '=== Totals, before and after ===';
SELECT  FileLogId = f.FileLogId,
        Before    = ISNULL((SELECT SUM(Rows) FROM @Baseline b WHERE b.FileLogId = f.FileLogId), 0),
        After     = ISNULL((SELECT SUM(Rows) FROM @After    a WHERE a.FileLogId = f.FileLogId), 0)
FROM   (SELECT DISTINCT FileLogId FROM dbo.UploadedFileLog WHERE ISNULL(IsDeleted, 0) = 0) f
ORDER BY f.FileLogId;

/* ---------------------------------------------------------------- PART 4 ----
   Did every rule run? Anything here is a rule still producing nothing.
   ---------------------------------------------------------------------------- */
PRINT '';
PRINT '=== Rules that did NOT run. Empty is the healthy result. ===';
PRINT '    50001 identifier rejected | 50002 no CUSTOM_LOGIC handler | 50003 no ValidationType handler';
SELECT DISTINCT
        ErrorNumber,
        Meaning = CASE ErrorNumber
                     WHEN 50001 THEN 'identifier rejected'
                     WHEN 50002 THEN 'no handler for CUSTOM_LOGIC keyword'
                     WHEN 50003 THEN 'no handler for ValidationType'
                     ELSE            'rule threw at execution' END,
        ErrorMessage
FROM    @Skipped
ORDER BY ErrorNumber, ErrorMessage;

IF EXISTS (SELECT 1 FROM @Failed)
BEGIN
    PRINT '';
    PRINT '=== Files whose dry run failed outright ===';
    SELECT FileLogId, ErrorMessage FROM @Failed ORDER BY FileLogId;
END

PRINT '';
PRINT '=== Nothing was changed. All transactions were rolled back. ===';
GO

/* -------------------------------------------------------------- VERIFY ----- */
PRINT '--- Confirm the dry run left no trace ---';
SELECT  StagingRowErrors = (SELECT COUNT(*) FROM dbo.StagingRowErrors),
        DB_Errors        = (SELECT COUNT(*) FROM dbo.DB_Errors);
PRINT '    Compare against the counts you had before running this: both should be identical.';
GO
