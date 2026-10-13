/* =============================================================================
   033_drop_unused_procedures.sql

   Drops 101 stored procedures nothing calls any more. Clean-up step 4 of the
   unused-code inventory (steps 1-3 removed the unused views, types and C# methods
   that were the last callers of some of these).

   How "unused" was decided (September 2026, against master + db_ACA360)
     - No C#, Razor or JavaScript file in the solution names the procedure, and no
       procedure / function / view / trigger that IS used names it (followed
       through, so a procedure only called by another unused one is unused too).
     - No procedure name is built at run time in the code, no script in
       "Database Script" executes one, and no SQL Agent job step names one.
     - None of them had run since SQL Server last started (sys.dm_exec_procedure_stats),
       while 89 others had.
     - sp_RunDatabaseBackup / sp_CleanupOldBackups are older copies: the app calls
       usp_RunDatabaseBackup / usp_CleanupOldBackups.

   Kept on purpose: sp_ResetOperationalData (manual admin tool, not called by the app).

   Safety
     1. Stops before changing anything if a procedure / function / view / trigger that
        is NOT being dropped still mentions one of these names.
     2. Saves every definition to dbo.tbl_DroppedProcedures_Backup_033 first.
     3. Drops inside one transaction. Procedures already gone are skipped.
   Safe to re-run.

   To restore one procedure:
       DECLARE @d NVARCHAR(MAX) = (SELECT Definition FROM dbo.tbl_DroppedProcedures_Backup_033
                                   WHERE ProcName = N'sp_Name');
       EXEC sys.sp_executesql @d;
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @Drop TABLE (ProcName SYSNAME PRIMARY KEY, Grp VARCHAR(40) NOT NULL);
INSERT INTO @Drop (ProcName, Grp) VALUES
    -- Old per-role dashboard statistics (replaced by the shared dashboard, scripts 029-032)
    (N'sp_GetDashboardStats', 'Old dashboards'),
    (N'sp_GetDirectorStats', 'Old dashboards'),
    (N'sp_GetSupervisorStats', 'Old dashboards'),
    (N'sp_GetDataAnalystStats', 'Old dashboards'),
    (N'sp_GetBrokerStats', 'Old dashboards'),
    (N'sp_GetAccountingStats', 'Old dashboards'),
    (N'sp_GetAdvancedSuperAdminStats', 'Old dashboards'),
    (N'sp_GetFileDashboardStats', 'Old dashboards'),
    (N'sp_GetPortfolioRisk', 'Old dashboards'),

    -- Settings: older versions of procedures the Settings pages now call under other names
    (N'sp_GetDatabaseSettings', 'Settings'),
    (N'sp_GetGeneralSettings', 'Settings'),
    (N'sp_GetIrsSettings', 'Settings'),
    (N'sp_GetSecuritySettings', 'Settings'),
    (N'sp_GetSmtpSettings', 'Settings'),
    (N'sp_GetSettingValue', 'Settings'),
    (N'sp_GetIrsPenaltyRates', 'Settings'),
    (N'sp_SaveSecuritySettings', 'Settings'),
    (N'sp_UpdateSetting', 'Settings'),
    (N'sp_UpdateSystemSetting', 'Settings'),
    (N'sp_RunDatabaseBackup', 'Settings'),
    (N'sp_CleanupOldBackups', 'Settings'),
    (N'sp_FilingYear_Delete', 'Settings'),

    -- Legacy staff / account-manager / user management and assignments
    (N'sp_AccountManagerList', 'Staff and users'),
    (N'sp_GetAccountManagerById', 'Staff and users'),
    (N'usp_AccountManager_GetById', 'Staff and users'),
    (N'usp_AccountManager_Insert', 'Staff and users'),
    (N'usp_AccountManager_Update', 'Staff and users'),
    (N'sp_AssignEmployersToAccountManager', 'Staff and users'),
    (N'sp_RemoveEmployersFromAccountManager', 'Staff and users'),
    (N'sp_StaffList', 'Staff and users'),
    (N'sp_GetStaffById', 'Staff and users'),
    (N'sp_CreateStaff', 'Staff and users'),
    (N'sp_UpdateStaff', 'Staff and users'),
    (N'sp_DeleteStaff', 'Staff and users'),
    (N'sp_AssignEmployersToStaff', 'Staff and users'),
    (N'sp_RemoveEmployersFromStaff', 'Staff and users'),
    (N'sp_GetAssignableUsers_ByRole', 'Staff and users'),
    (N'sp_GetEmployerCurrentAssignments', 'Staff and users'),
    (N'sp_AssignManagerToEmployer', 'Staff and users'),
    (N'sp_GetCurrentAssignmentForEmployer', 'Staff and users'),
    (N'sp_IsEmployerAssignedToBroker', 'Staff and users'),
    (N'usp_User_List', 'Staff and users'),
    (N'usp_User_GetById', 'Staff and users'),
    (N'usp_User_Delete', 'Staff and users'),
    (N'sp_UpdateUserPasswordHash', 'Staff and users'),

    -- Old audit-log screens
    (N'sp_GetAuditLogs', 'Audit'),
    (N'sp_GetPagedAuditLogs', 'Audit'),
    (N'sp_GetAuditStats', 'Audit'),
    (N'sp_GetAuditUsers', 'Audit'),

    -- Employer / employee / plan / ACA calculation: older versions and removed features
    (N'sp_select_Employer', 'Employer, employee, plan'),
    (N'sp_EmployerInfoMainDisplay', 'Employer, employee, plan'),
    (N'sp_GetEmployerCommonDetailsByEINAndFilingYear', 'Employer, employee, plan'),
    (N'sp_GetEmployerDetailsByEINAndFilingYear', 'Employer, employee, plan'),
    (N'sp_HardDeleteEmployer', 'Employer, employee, plan'),
    (N'sp_EmployeeList', 'Employer, employee, plan'),
    (N'sp_PlanListThird', 'Employer, employee, plan'),
    (N'sp_GetFlaggedEmployees_GridAndFlags_demo', 'Employer, employee, plan'),
    (N'sp_BulkUpdateEmployeeCodes', 'Employer, employee, plan'),
    (N'sp_GenerateCoveredIndividualCodes', 'Employer, employee, plan'),
    (N'sp_LogEmployeeCodeImportAudit', 'Employer, employee, plan'),
    (N'sp_FetchCalculationData', 'Employer, employee, plan'),
    (N'sp_Calculate1094Counts', 'Employer, employee, plan'),
    (N'sp_GetEmployeesForPreview', 'Employer, employee, plan'),

    -- Tracker services / processes: older versions
    (N'sp_Tracker_ManageEmployer', 'Tracker'),
    (N'sp_SaveEmployerService', 'Tracker'),
    (N'sp_UpdateEmployerService', 'Tracker'),
    (N'sp_ServiceList', 'Tracker'),
    (N'sp_Process', 'Tracker'),
    (N'sp_ProcessPagenation', 'Tracker'),

    -- File upload, validation and import analysis: older pipeline steps
    (N'sp_AddFileLog', 'File upload'),
    (N'sp_UpdateFileStatus', 'File upload'),
    (N'sp_UpdateFileLogPeriod', 'File upload'),
    (N'sp_GetAllFileLogs', 'File upload'),
    (N'sp_GetFileLogsByUserId', 'File upload'),
    (N'sp_AssignFileToUser', 'File upload'),
    (N'sp_GetPendingFilesForUser', 'File upload'),
    (N'sp_GetSimpleErrorsByFileLogId', 'File upload'),
    (N'sp_GetStagingErrorsByFileLogId', 'File upload'),
    (N'sp_GetErrorSummaryByTable', 'File upload'),
    (N'sp_CheckForStagingErrors', 'File upload'),
    (N'sp_CheckForRuleEngineErrors', 'File upload'),
    (N'sp_UpdateAndRevalidateSingleField', 'File upload'),
    (N'sp_Validation_UpdateProgress', 'File upload'),
    (N'sp_Validation_UpdateRowValidity', 'File upload'),
    (N'sp_Validation_FinalizeSummary', 'File upload'),
    (N'sp_AnalyzeImport_Preview', 'File upload'),
    (N'sp_ExecuteImportImpactAnalysis', 'File upload'),
    (N'usp_UpdateAutomationStatus', 'File upload'),

    -- IRS submissions, communication log, payments, flags, FAQ, notifications
    (N'sp_GetSubmissionsByYear', 'Other'),
    (N'sp_GetSubmissionsForAm', 'Other'),
    (N'sp_InsertSubmissionError', 'Other'),
    (N'sp_DeleteSubmissionErrors', 'Other'),
    (N'sp_CreateCommunicationLog', 'Other'),
    (N'sp_LogCommunication', 'Other'),
    (N'sp_GetCommunicationLogsForEmployer', 'Other'),
    (N'sp_CreatePayment', 'Other'),
    (N'sp_GetPaymentsForEmployer', 'Other'),
    (N'sp_GetFlagByCode', 'Other'),
    (N'sp_DeleteFlag', 'Other'),
    (N'sp_GetActiveFaqs', 'Other'),
    (N'sp_GetAllNotifications', 'Other');

-- 1. Stop if something we keep still mentions one of these names. A quick substring
--    search finds candidates; the whole-word match ('_' escaped, it is a LIKE
--    wildcard) then runs on those only.
DECLARE @Candidate TABLE (ProcName SYSNAME, ObjectId INT);
INSERT INTO @Candidate (ProcName, ObjectId)
SELECT d.ProcName, m.object_id
FROM @Drop d
JOIN sys.sql_modules m ON CHARINDEX(d.ProcName, m.definition) > 0
WHERE OBJECT_NAME(m.object_id) NOT IN (SELECT ProcName FROM @Drop);

DECLARE @StillUsed TABLE (ProcName SYSNAME, UsedBy NVARCHAR(300));
INSERT INTO @StillUsed (ProcName, UsedBy)
SELECT c.ProcName, OBJECT_SCHEMA_NAME(m.object_id) + N'.' + OBJECT_NAME(m.object_id)
FROM @Candidate c
JOIN sys.sql_modules m ON m.object_id = c.ObjectId
WHERE N' ' + m.definition + N' ' LIKE N'%[^A-Za-z0-9_]' + REPLACE(c.ProcName, N'_', N'[_]') + N'[^A-Za-z0-9_]%';

IF EXISTS (SELECT 1 FROM @StillUsed)
BEGIN
    SELECT ProcName, UsedBy AS StillMentionedBy FROM @StillUsed ORDER BY ProcName, UsedBy;
    RAISERROR(N'033: a kept object still mentions a procedure on the drop list (see result above). Nothing was dropped.', 16, 1);
    RETURN;
END;

-- 2. Save the definitions (first run only for each procedure).
IF OBJECT_ID(N'dbo.tbl_DroppedProcedures_Backup_033', N'U') IS NULL
    CREATE TABLE dbo.tbl_DroppedProcedures_Backup_033 (
        ProcName   SYSNAME        NOT NULL PRIMARY KEY,
        Grp        VARCHAR(40)    NOT NULL,
        Definition NVARCHAR(MAX)  NOT NULL,
        CreatedAt  DATETIME       NULL,
        ModifiedAt DATETIME       NULL,
        BackedUpAt DATETIME       NOT NULL DEFAULT GETDATE()
    );

INSERT INTO dbo.tbl_DroppedProcedures_Backup_033 (ProcName, Grp, Definition, CreatedAt, ModifiedAt)
SELECT d.ProcName, d.Grp, m.definition, o.create_date, o.modify_date
FROM @Drop d
JOIN sys.objects o ON o.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(d.ProcName)) AND o.type = 'P'
JOIN sys.sql_modules m ON m.object_id = o.object_id
WHERE NOT EXISTS (SELECT 1 FROM dbo.tbl_DroppedProcedures_Backup_033 b WHERE b.ProcName = d.ProcName);

-- 3. Drop.
BEGIN TRANSACTION;

DECLARE @Name SYSNAME, @Sql NVARCHAR(400), @Dropped INT = 0;
DECLARE drop_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT d.ProcName FROM @Drop d
    WHERE OBJECT_ID(N'dbo.' + QUOTENAME(d.ProcName), N'P') IS NOT NULL
    ORDER BY d.ProcName;
OPEN drop_cursor;
FETCH NEXT FROM drop_cursor INTO @Name;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Sql = N'DROP PROCEDURE dbo.' + QUOTENAME(@Name) + N';';
    EXEC sys.sp_executesql @Sql;
    SET @Dropped += 1;
    FETCH NEXT FROM drop_cursor INTO @Name;
END;
CLOSE drop_cursor;
DEALLOCATE drop_cursor;

COMMIT TRANSACTION;

-- 4. Report.
SELECT @Dropped AS DroppedNow,
       (SELECT COUNT(*) FROM @Drop) AS OnList,
       (SELECT COUNT(*) FROM @Drop d WHERE OBJECT_ID(N'dbo.' + QUOTENAME(d.ProcName), N'P') IS NOT NULL) AS StillPresent,
       (SELECT COUNT(*) FROM dbo.tbl_DroppedProcedures_Backup_033) AS InBackupTable;
GO
