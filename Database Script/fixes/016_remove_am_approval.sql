/* =============================================================================
   016_remove_am_approval.sql

   WHY     The client does not want files sent to an Account Manager for approval.
           The step is removed end to end.

   THE OLD FLOW
           upload -> assign to DA -> DA clears triage -> DA "Send for Approval"
           -> AM "Approve" -> import

   THE NEW FLOW
           upload -> assign to DA -> DA clears triage -> import

           These workflow statuses are now unreachable. The lookup rows are kept so
           historical files still render a meaningful status:

               240  Ready for Approval
               241  AM Rejected
               250  Queued for Import

   WHO CAN DO WHAT NOW
           Upload and Import: SuperAdmin, Admin, ACA Director, DA Supervisor,
           Account Manager Supervisor, Data Analyst, Account Manager.

           Broker, Employer, StaffAC and StaffAU keep read access to the file log
           but can no longer upload. That is enforced in the application, on the
           Upload / GetSasUploadUrl / FinalizeUpload / SmartDetectEmployer actions.

   WHAT REPLACES THE APPROVAL GATE
           The AM sign-off was the ONLY check standing between a validated file and
           the live tables — ImportData never looked at the validation result at all.
           FileUploadController.ImportData now refuses any file whose
           ValidationStatusId is not 70 (Valid with Warnings) or 80 (Clean).
           Removing the approval step without this would have made it possible to
           import a file full of fatal errors.

   TWO PRE-EXISTING BUGS FIXED WHILE IN HERE
           Both are unrelated to the approval step but sit in the same procedures.

           1. sp_GetDataAnalystStats filters on ValidationStatusId 1-5. The current
              scheme is 10-99. Every counter and the chart on the Data Analyst
              dashboard have therefore been returning zero.

           2. sp_GetFileDashboardStats counts "Completed" as WorkflowStatusId = 250.
              250 is Queued for Import; Complete is 260. Its "Processing" bucket
              also counts 245, which is not a status in this system, and misses
              251 (Importing).

   SAFE TO RE-RUN
           Yes. CREATE OR ALTER throughout; the data migration and the permission
           inserts are both guarded.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* =============================================================================
   PART 1 — sp_Validation_Finalize: land a clean file on 220, not 240.

   This is the procedure that put files into the AM queue. With approval gone it
   must leave the file with the analyst. Only the two status assignments in
   section 3 differ from the existing procedure.
   ============================================================================= */

CREATE OR ALTER PROCEDURE [dbo].[sp_Validation_Finalize]
(
    @FileLogId INT
)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE
        @FatalErrorRowCount BIGINT = 0,
        @WarningRowCount BIGINT = 0,
        @TotalRowsValidated BIGINT = 0,

        @FinalValidationStatusId INT,
        @FinalWorkflowStatusId INT,

        @EmpCount BIGINT = 0,
        @HasPlans BIT = 0,
        @HasPremiums BIT = 0;

    ------------------------------------------------------------
    -- 1. GATHER COUNTS (Dynamic Denominator)
    ------------------------------------------------------------
    SELECT @EmpCount = COUNT_BIG(*) FROM Staging_Employees WHERE FileLogId = @FileLogId OPTION (MAXDOP 1);

    -- Ensure the tables here match the tables processed in the Error Counts below
    SET @TotalRowsValidated = @EmpCount
        + (SELECT COUNT_BIG(*) FROM Staging_Employers WHERE FileLogId = @FileLogId)
        + (SELECT COUNT_BIG(*) FROM Staging_Plans     WHERE FileLogId = @FileLogId)
        + (SELECT COUNT_BIG(*) FROM Staging_Premiums  WHERE FileLogId = @FileLogId)
        + (SELECT COUNT_BIG(*) FROM Staging_Dependents WHERE FileLogId = @FileLogId);

    -- Check existence for Summary flags
    IF EXISTS (SELECT 1 FROM Staging_Plans WHERE FileLogId = @FileLogId) SET @HasPlans = 1;
    IF EXISTS (SELECT 1 FROM Staging_Premiums WHERE FileLogId = @FileLogId) SET @HasPremiums = 1;

    ------------------------------------------------------------
    -- 2. DEDUPLICATED ERROR COUNTS (The "Negative Math" Fix)
    ------------------------------------------------------------
    -- This counts UNIQUE rows that failed, preventing 1 row with 10 errors from breaking the math
    ;WITH DistinctFailedRows AS (
        SELECT DISTINCT E.StagingTableName, E.StagingRowId, R.Severity
        FROM StagingRowErrors E
        INNER JOIN ValidationRules R ON E.RuleId = R.RuleId
        WHERE E.FileLogId = @FileLogId
          -- CRITICAL: Only count rows for the tables we included in TotalRowsValidated
          AND E.StagingTableName IN ('Staging_Employers', 'Staging_Employees', 'Staging_Dependents', 'Staging_Plans', 'Staging_Premiums')
    )
    SELECT
        @FatalErrorRowCount = ISNULL(SUM(CASE WHEN Severity = 'Error' THEN 1 ELSE 0 END), 0),
        @WarningRowCount = ISNULL(SUM(CASE WHEN Severity = 'Warning' THEN 1 ELSE 0 END), 0)
    FROM DistinctFailedRows
    OPTION (MAXDOP 1);

    ------------------------------------------------------------
    -- 3. APPLY DATA DICTIONARY RULES
    ------------------------------------------------------------
    IF @FatalErrorRowCount > 0
    BEGIN
        SET @FinalValidationStatusId = 50; -- Data Error
        SET @FinalWorkflowStatusId = 230;  -- Needs Correction
    END
    ELSE IF @WarningRowCount > 0
    BEGIN
        SET @FinalValidationStatusId = 70; -- Valid with Warnings
        /* CHANGED (016): was 240 Ready for Approval. The approval step no longer
           exists, so the file stays with the Data Analyst, who imports it. */
        SET @FinalWorkflowStatusId = 220;  -- Processing / DA remediation
    END
    ELSE
    BEGIN
        SET @FinalValidationStatusId = 80; -- Valid / Clean
        /* CHANGED (016): was 240 Ready for Approval. */
        SET @FinalWorkflowStatusId = 220;  -- Processing / DA remediation
    END

    ------------------------------------------------------------
    -- 4. UPDATE LOG (Safe Percentage Math)
    ------------------------------------------------------------
    UPDATE UploadedFileLog
    SET
        AnalysisDate = SYSUTCDATETIME(),
        ValidationStatusId = @FinalValidationStatusId,
        WorkflowStatusId = @FinalWorkflowStatusId,
        TotalRowsValidated = @TotalRowsValidated,
        FatalErrorCount = @FatalErrorRowCount,
        WarningCount = @WarningRowCount,
        ErrorPercentage = CASE
            WHEN @TotalRowsValidated > 0
            -- We use the count of Failed ROWS / Total ROWS for an accurate health percentage
            THEN (CAST(@FatalErrorRowCount AS DECIMAL(18,2)) / CAST(@TotalRowsValidated AS DECIMAL(18,2))) * 100.0
            ELSE 0
        END
    WHERE FileLogId = @FileLogId
    OPTION (MAXDOP 1);

    ------------------------------------------------------------
    -- 5. UPDATE SUMMARY
    ------------------------------------------------------------
    MERGE ImportValidationSummary AS T
    USING (SELECT @FileLogId AS FileLogId) AS S
    ON T.FileLogId = S.FileLogId
    WHEN MATCHED THEN
        UPDATE SET
            TotalEmployees = @EmpCount,
            HasPlanData = @HasPlans,
            HasPayrollData = @HasPremiums,
            ValidationMessage = CASE WHEN @FatalErrorRowCount > 0 THEN 'Errors Found' ELSE 'Clean' END
    WHEN NOT MATCHED THEN
        INSERT (FileLogId, TotalEmployees, HasPlanData, HasPayrollData, ValidationMessage)
        VALUES (@FileLogId, @EmpCount, @HasPlans, @HasPremiums,
                CASE WHEN @FatalErrorRowCount > 0 THEN 'Errors Found' ELSE 'Clean' END)
    OPTION (MAXDOP 1);

END
GO

/* =============================================================================
   PART 2 — sp_GetDataAnalystStats: correct the status numbering and retire the
            "Awaiting AM" counter.

   PRE-EXISTING BUG. The procedure filtered on ValidationStatusId 1, 2, 3, 4 and 5.
   Those values do not exist in this database — the scheme is:

       10 Pending      20 PreProcessing   30 Validating    40 StructureError
       41 MappingReqd  50 DataError       60 Partial       70 ValidWithWarnings
       80 Clean        90 Imported        99 Failed

   Every counter and the status chart on the Data Analyst dashboard have therefore
   been showing zero since the renumbering.

   The ReadyForApproval column is renamed ReadyToImport. DashboardService reads
   these columns by name, so the application change and this script must go
   together.
   ============================================================================= */

CREATE OR ALTER PROCEDURE [dbo].[sp_GetDataAnalystStats]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Today      DATE          = CAST(GETUTCDATE() AS DATE);
    DECLARE @UserIdStr  NVARCHAR(50)  = CAST(@UserId AS NVARCHAR(50));

    /* Inbox: files of mine that still need a human. Fatal data errors, a broken
       structure, an unmapped template, or an explicit Needs Correction. */
    DECLARE @PendingTriage INT = (
        SELECT COUNT(*)
        FROM   UploadedFileLog
        WHERE  (AssignedToUserId = @UserIdStr OR UploadedByUserId = @UserIdStr)
          AND  IsDeleted = 0
          AND (ValidationStatusId IN (40, 41, 50) OR WorkflowStatusId = 230));

    /* Outbox: cleared validation and waiting on me to import. 250 is included so
       that any file left in the old approval queue still surfaces. */
    DECLARE @ReadyToImport INT = (
        SELECT COUNT(*)
        FROM   UploadedFileLog
        WHERE  (AssignedToUserId = @UserIdStr OR UploadedByUserId = @UserIdStr)
          AND  IsDeleted = 0
          AND  ValidationStatusId IN (70, 80)
          AND  WorkflowStatusId IN (211, 220, 250));

    /* Velocity: imported today. Keyed on the workflow status, not the validation
       status, because Complete is the state that means the data actually landed. */
    DECLARE @CompletedToday INT = (
        SELECT COUNT(*)
        FROM   UploadedFileLog
        WHERE  (AssignedToUserId = @UserIdStr OR UploadedByUserId = @UserIdStr)
          AND  IsDeleted = 0
          AND  WorkflowStatusId = 260
          AND  CAST(ModifiedOn AS DATE) = @Today);

    -- RS1: Chart data — aliased to match ChartDataPoint (Label, Value)
    SELECT
        s.StatusName       AS Label,
        COUNT(f.FileLogId) AS Value
    FROM UploadedFileLog f
    JOIN ValidationStatuses s ON f.ValidationStatusId = s.ValidationStatusId
    WHERE (f.AssignedToUserId = @UserIdStr OR f.UploadedByUserId = @UserIdStr)
      AND f.IsDeleted = 0
      -- Live, in-flight files only: everything short of Imported (90) and Failed (99)
      AND f.ValidationStatusId IN (10, 20, 30, 40, 41, 50, 60, 70, 80)
    GROUP BY s.StatusName;

    -- RS2: Scalar counters
    SELECT
        @PendingTriage   AS PendingTriage,
        @ReadyToImport   AS ReadyToImport,
        @CompletedToday  AS CompletedToday;
END
GO

/* =============================================================================
   PART 3 — sp_GetFileDashboardStats: correct the status numbering.

   PRE-EXISTING BUG. The buckets were written against an older scheme:

       "Queued for Import (240)"  240 is Ready for Approval; 250 was the queue
       "Importing (245)"          245 does not exist; Importing is 251
       "Complete/Imported (250)"  250 is Queued for Import; Complete is 260

   So the Completed tile counted files that had been approved but never imported,
   and the Processing tile missed every file that was actually importing.
   ============================================================================= */

CREATE OR ALTER PROCEDURE [dbo].[sp_GetFileDashboardStats]
    @UserId VARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        -- 1. Total Uploads (Current Month)
        COUNT(*) AS TotalUploads,

        -- 2. Action Required — waiting on a person
        --    40 StructureError, 41 MappingRequired, 50 DataError
        --    209 SystemRejected, 210 PendingAssignment, 230 NeedsCorrection,
        --    231 AwaitingClientInput, 261 ImportFailed
        SUM(CASE WHEN
                ValidationStatusId IN (40, 41, 50)
                OR WorkflowStatusId IN (209, 210, 230, 231, 261)
            THEN 1 ELSE 0 END) AS ActionRequired,

        -- 3. Processing — in flight
        --    20 PreProcessing, 30 Validating
        --    200 PreProcessing, 211 DAReviewing, 220 Processing, 251 Importing
        SUM(CASE WHEN
                ValidationStatusId IN (20, 30)
                OR WorkflowStatusId IN (200, 211, 220, 251)
            THEN 1 ELSE 0 END) AS Processing,

        -- 4. Completed — 260 Complete. Was 250, which is Queued for Import.
        SUM(CASE WHEN WorkflowStatusId = 260 THEN 1 ELSE 0 END) AS Completed

    FROM dbo.UploadedFileLog
    WHERE IsDeleted = 0
      AND UploadedAt >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)
      AND (@UserId IS NULL OR UploadedByUserId = @UserId OR AssignedToUserId = @UserId);
END
GO

/* =============================================================================
   PART 4 — Release files stranded in the approval queue.

   Any file sitting at 240 (Ready for Approval) or 250 (Queued for Import) is now
   in a state nothing can act on: the buttons that moved it are gone. Move it back
   to 220 so the analyst can finish it, and record why in the lifecycle history.

   Files at 241 (AM Rejected) are deliberately left alone — 241 was already a
   terminal-ish state and moving them would misrepresent what happened.
   ============================================================================= */

DECLARE @Stranded TABLE (FileLogId INT, PreviousStatusId INT);

INSERT INTO @Stranded (FileLogId, PreviousStatusId)
SELECT FileLogId, WorkflowStatusId
FROM   dbo.UploadedFileLog
WHERE  WorkflowStatusId IN (240, 250)
  AND  IsDeleted = 0;

IF EXISTS (SELECT 1 FROM @Stranded)
BEGIN
    UPDATE l
    SET    l.WorkflowStatusId = 220,
           l.ModifiedOn       = GETUTCDATE()
    FROM   dbo.UploadedFileLog l
    INNER JOIN @Stranded s ON s.FileLogId = l.FileLogId;

    INSERT INTO dbo.FileLifecycleHistory
        (FileLogId, PreviousStatusId, NewStatusId, ActionName,
         PerformedByUserId, PerformedByUserName, [Timestamp], [Message])
    SELECT s.FileLogId, s.PreviousStatusId, 220, 'Approval Step Removed',
           NULL, 'System (script 016)', SYSUTCDATETIME(),
           'The Account Manager approval step was removed. File returned to the Data Analyst to import.'
    FROM   @Stranded s;

    PRINT '--- Released ' + CAST((SELECT COUNT(*) FROM @Stranded) AS VARCHAR(10))
        + ' file(s) from the retired approval queue back to 220 (Processing).';
END
ELSE
BEGIN
    PRINT '--- No files were stranded at 240 or 250. Nothing to release.';
END
GO

/* =============================================================================
   PART 5 — Menu access for the two supervisor roles.

   sp_GetUserMenu shows a menu to a role if that role has ANY enabled row in
   tbl_AccessPermissions for it. Menu 17 is "Files" (controller FileUpload).

   Today menu 17 is granted to:
       2  SuperAdmin        3  Admin         4  ACA Director
       10 Broker           11 Data Analyst  16 Account Manager

   Missing, and both named in the client's list:
       5  DA Supervisor     6  Account Manager Supervisor

   Neither supervisor can currently see the Files menu at all. Granting the same
   five actions the other roles already hold: 7 Index, 1 View, 2 Add, 3 Edit,
   4 Delete. These are the actions declared for menu 17 in tbl_MenuActions.

   NOTE  There is no "Import" row in tbl_ActionTypes, and FileUploadController does
         not use [AuthorizePermission] — it authorises on role. So the import
         privilege itself is enforced in the application
         (FileUploadWorkflowService.CanImport); these rows are what make the screen
         reachable. Both must be right for the privilege to be usable.
   ============================================================================= */

;WITH Wanted AS (
    SELECT RoleId, ActionID
    FROM  (VALUES (5), (6)) AS r(RoleId)
    CROSS JOIN (VALUES (7), (1), (2), (3), (4)) AS a(ActionID)
)
INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT w.RoleId, 17, w.ActionID, 1, GETDATE(), 'script 016'
FROM   Wanted w
WHERE  NOT EXISTS (
           SELECT 1 FROM dbo.tbl_AccessPermissions p
           WHERE  p.RoleId = w.RoleId AND p.MenuId = 17 AND p.ActionID = w.ActionID);

PRINT '--- Granted ' + CAST(@@ROWCOUNT AS VARCHAR(10))
    + ' new permission row(s) for menu 17 (Files).';

/* Re-enable any row that exists but was switched off. */
UPDATE dbo.tbl_AccessPermissions
SET    IsEnabled = 1, Date_Modified = GETDATE(), Modified_by = 'script 016'
WHERE  MenuId = 17
  AND  RoleId IN (2, 3, 4, 5, 6, 11, 16)
  AND  ISNULL(IsEnabled, 0) = 0;

PRINT '--- Re-enabled ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' previously disabled row(s).';
GO

/* =============================================================================
   PART 6 — Role descriptions that still promise an approval step.

   These strings are shown on the Roles screen. Two of them describe a workflow
   that no longer exists.
   ============================================================================= */

UPDATE dbo.tbl_Roles
SET    Description = 'Processes assigned files. Runs validations, uses the Error Triage Dashboard to correct errors and skip warnings, then imports the cleared file into the live tables.'
WHERE  RoleName = 'Data Analyst'
  AND  Description LIKE '%final review%';

UPDATE dbo.tbl_Roles
SET    Description = 'The primary owner of the employer relationship. Uploads client files and assigns file processing tasks to Data Analysts.'
WHERE  RoleName = 'Account Manager'
  AND  Description LIKE '%final approval%';
GO

/* -------------------------------------------------------------- VERIFY ----- */

PRINT '';
PRINT '=== 1. sp_Validation_Finalize no longer parks files at 240 ===';
SELECT  Result = CASE
            WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Validation_Finalize')) LIKE '%@FinalWorkflowStatusId = 240%'
            THEN 'FAIL - still sets 240'
            ELSE 'PASS' END;

PRINT '=== 2. sp_GetDataAnalystStats returns ReadyToImport ===';
SELECT  Result = CASE
            WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_GetDataAnalystStats')) LIKE '%ReadyToImport%'
             AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_GetDataAnalystStats')) NOT LIKE '%ValidationStatusId IN (1, 2, 3, 4)%'
            THEN 'PASS'
            ELSE 'FAIL' END;

PRINT '=== 3. sp_GetFileDashboardStats counts Complete as 260 ===';
SELECT  Result = CASE
            WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_GetFileDashboardStats')) LIKE '%WorkflowStatusId = 260 THEN 1%'
            THEN 'PASS'
            ELSE 'FAIL' END;

PRINT '=== 4. No file left in the retired approval queue ===';
SELECT  Result = CASE WHEN COUNT(*) = 0 THEN 'PASS' ELSE 'FAIL - ' + CAST(COUNT(*) AS VARCHAR(10)) + ' still at 240/250' END
FROM    dbo.UploadedFileLog
WHERE   WorkflowStatusId IN (240, 250) AND IsDeleted = 0;

PRINT '=== 5. Every role that may upload can see the Files menu ===';
SELECT  RoleId   = r.ID,
        RoleName = r.RoleName,
        SeesFilesMenu = CASE WHEN EXISTS (
                            SELECT 1 FROM dbo.tbl_AccessPermissions p
                            WHERE p.RoleId = r.ID AND p.MenuId = 17 AND p.IsEnabled = 1)
                        THEN 'yes' ELSE 'NO - investigate' END
FROM    dbo.tbl_Roles r
WHERE   r.ID IN (2, 3, 4, 5, 6, 11, 16)   -- the seven roles that may upload and import
ORDER BY r.ID;

PRINT '=== 6. Current spread of workflow statuses ===';
SELECT  l.WorkflowStatusId,
        StatusName = ISNULL(w.StatusName, '(no lookup row)'),
        Files      = COUNT(*)
FROM    dbo.UploadedFileLog l
LEFT JOIN dbo.WorkflowStatuses w ON w.WorkflowStatusId = l.WorkflowStatusId
WHERE   l.IsDeleted = 0
GROUP BY l.WorkflowStatusId, w.StatusName
ORDER BY l.WorkflowStatusId;

PRINT '';
PRINT '=== STILL OUTSTANDING — needs a decision, not fixed here ===';
PRINT '  sp_GetDropdownDataService carries a copy of the sp_GetFileDashboardStats';
PRINT '  block with the same wrong status numbers (240/245/250). It is a 4,000-line';
PRINT '  procedure and rewriting it wholesale to patch a copied fragment is not';
PRINT '  worth the risk. Either that block is dead, in which case delete it, or it';
PRINT '  feeds a second dashboard that has been wrong for as long as the first one.';
PRINT '  Worth confirming which before anyone acts on those numbers.';
GO
