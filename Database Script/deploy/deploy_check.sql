/* =============================================================================
   deploy_check.sql

   READ-ONLY. Reports, for each script in Database Script/fixes from 023 to 075,
   whether this database already has it. Nothing is changed.

   Run it before deploy_combined.sql (to see what is missing) and after
   it (everything should say Yes, except 035, which is run separately later).
   045 and 046 are local data only (developer database) and are not checked here.

   Each row is judged by something the script leaves behind: an object it
   creates, a backup table, a parameter, a line in a procedure, or the data it
   corrects.
   ============================================================================= */
SET NOCOUNT ON;

DECLARE @r TABLE (Script CHAR(3), Applied VARCHAR(8), Detail NVARCHAR(400));

-- 014 e-mail delivery status (before 023, but found missing on a database that had 023 on)
INSERT @r SELECT '014',
    CASE WHEN EXISTS (SELECT 1 FROM sys.parameters WHERE object_id = OBJECT_ID('dbo.sp_LogEmail') AND name = '@Status') THEN 'Yes' ELSE 'No' END,
    'sp_LogEmail takes @Status; without it every 1095-C e-mail is sent but reported failed and never logged. Run fixes/014 separately.';

-- 023 server-side auth sessions
INSERT @r SELECT '023',
    CASE WHEN OBJECT_ID('dbo.tbl_AuthSessions', 'U') IS NOT NULL AND OBJECT_ID('dbo.sp_AuthSession_Create', 'P') IS NOT NULL
              AND OBJECT_ID('dbo.sp_AuthSession_Revoke', 'P') IS NOT NULL AND OBJECT_ID('dbo.sp_AuthSession_Purge', 'P') IS NOT NULL
         THEN 'Yes' ELSE 'No' END,
    'tbl_AuthSessions and sp_AuthSession_Create / Revoke / Purge exist';

-- 024 file status buckets
INSERT @r SELECT '024',
    CASE WHEN OBJECT_ID('dbo.fn_FileStatusBucket') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'dbo.fn_FileStatusBucket exists';

-- 025 file progress updates
INSERT @r SELECT '025',
    CASE WHEN OBJECT_ID('dbo.sp_UpdateFileProgress', 'P') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'dbo.sp_UpdateFileProgress exists';

-- 026 status filter by bucket
INSERT @r SELECT '026',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_SearchFileLogsPaginated')) LIKE '%StatusBucket = @FilterType%' THEN 'Yes' ELSE 'No' END,
    'sp_SearchFileLogsPaginated filters by any single bucket';

-- 027 admin password reset
INSERT @r SELECT '027',
    CASE WHEN (SELECT max_length FROM sys.parameters WHERE object_id = OBJECT_ID('dbo.sp_UpdateUserAccounts') AND name = '@UserAccounts_Password') >= 200
         THEN 'Yes' ELSE 'No' END,
    'sp_UpdateUserAccounts takes a 100-character password (BCrypt hash fits)';

-- 028 block deleted user login
INSERT @r SELECT '028',
    CASE WHEN OBJECT_ID('dbo.fn_UserLoginAllowed') IS NOT NULL
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Login_access')) LIKE '%fn_UserLoginAllowed%'
         THEN 'Yes' ELSE 'No' END,
    'fn_UserLoginAllowed exists and sp_Login_access uses it';

-- 029 dashboard scope and account managers
INSERT @r SELECT '029',
    CASE WHEN OBJECT_ID('dbo.fn_DashboardEmployerScope') IS NOT NULL AND OBJECT_ID('dbo.sp_GetDashboardAccountManagers', 'P') IS NOT NULL
              AND OBJECT_ID('dbo.sp_GetPortfolioDashboardCounts', 'P') IS NOT NULL AND OBJECT_ID('dbo.sp_GetPortfolioProcessSteps', 'P') IS NOT NULL
         THEN 'Yes' ELSE 'No' END,
    'fn_DashboardEmployerScope and the portfolio / account-manager procedures exist';

-- 030 align role permissions (keeps a backup of the permissions it changed)
INSERT @r SELECT '030',
    CASE WHEN OBJECT_ID('dbo.tbl_AccessPermissions_Backup_030', 'U') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'backup table tbl_AccessPermissions_Backup_030 exists';

-- 031 dashboard file counts
INSERT @r SELECT '031',
    CASE WHEN OBJECT_ID('dbo.sp_GetDashboardFileCounts', 'P') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'dbo.sp_GetDashboardFileCounts exists';

-- 032 dashboard system stats
INSERT @r SELECT '032',
    CASE WHEN OBJECT_ID('dbo.sp_GetDashboardSystemStats', 'P') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'dbo.sp_GetDashboardSystemStats exists';

-- 033 drop unused procedures (keeps their definitions in a backup table)
INSERT @r SELECT '033',
    CASE WHEN OBJECT_ID('dbo.tbl_DroppedProcedures_Backup_033', 'U') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'backup table tbl_DroppedProcedures_Backup_033 exists';

-- 034 archive unused tables (moves 22 tables out of dbo) / 035 drop the archive schema
DECLARE @archiveTables INT = (SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'archive');
DECLARE @stillInDbo INT = (SELECT COUNT(*) FROM sys.tables t
                           WHERE t.schema_id = SCHEMA_ID('dbo')
                             AND t.name IN ('tbl_Medtracker_Account_Manager_History', 'tbl_Medtracker_Brokertbl_Medtracker_Contact',
                                            'tbl_Medtracker_Data_Analyst_History', 'tbl_Medtracker_DataAnalyst', 'tbl_Medtracker_Firm',
                                            'tbl_Medtracker_Location', 'tbl_Medtracker_ImplementationCategory', 'tbl_Medtracker_Permission',
                                            'CommunicationsLog', 'tbl_AccountManager', 'Payments', 'UserLoginHistory',
                                            'ServiceEngagements', 'WorkflowItems', 'WorkflowTemplates', 'EmployerACAProfile',
                                            'ImportDiffLog', 'tbl_FileImportImpactAnalysis', 'Tracker_BillingContact',
                                            'tbl_IntegrationSettings', 'mcMailServer', '_ParamMigration_Backup'));
INSERT @r SELECT '034',
    CASE WHEN @stillInDbo = 0 THEN 'Yes' ELSE 'No' END,
    CONCAT(@stillInDbo, ' of the 22 unused table(s) still in dbo; ', @archiveTables, ' in the archive schema');
INSERT @r SELECT '035',
    CASE WHEN @archiveTables > 0 THEN 'Not yet' WHEN @stillInDbo = 0 THEN 'Yes' ELSE 'n/a' END,
    'run separately, after a full backup: drops the archive schema (not in the combined script)';

-- 036 fix menu rows
DECLARE @badMenus INT = (SELECT COUNT(*) FROM dbo.tbl_Menus
                         WHERE Controller <> LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(Controller, CHAR(9), ''), CHAR(13), ''), CHAR(10), '')))
                            OR (LTRIM(RTRIM(Controller)) = 'FilingYear' AND IsActive = 1));
INSERT @r SELECT '036',
    CASE WHEN @badMenus = 0 THEN 'Yes' ELSE 'No' END,
    CONCAT(@badMenus, ' menu row(s) with whitespace in Controller or an active FilingYear menu');

-- 037 switch off duplicate staff logins (same rule as the script's section 1)
DECLARE @dupes INT = (
    SELECT COUNT(*) FROM dbo.tbl_User u
    JOIN dbo.StaffAccounts s ON s.Staff_ID = u.Ref_ID
    WHERE ISNULL(u.Role_ID, 0) NOT IN (10, 13) AND u.Ref_ID IS NOT NULL
      AND u.[User_Name] <> s.Staff_Email AND ISNULL(u.Flag, 1) <> 0
      AND EXISTS (SELECT 1 FROM dbo.tbl_User k
                  WHERE k.Ref_ID = u.Ref_ID AND k.User_ID <> u.User_ID
                    AND ISNULL(k.Role_ID, 0) NOT IN (10, 13) AND k.[User_Name] = s.Staff_Email));
INSERT @r SELECT '037',
    CASE WHEN @dupes = 0 THEN 'Yes' ELSE 'No' END,
    CONCAT(@dupes, ' active duplicate staff login(s) left');

-- 038 Employee.EmployerId on manual add
DECLARE @noEmployer INT = (SELECT COUNT(*) FROM dbo.Employee e
                           WHERE e.EmployerId IS NULL AND ISNULL(e.IsDeleted, 0) = 0
                             AND EXISTS (SELECT 1 FROM dbo.EmployeeCode ec WHERE ec.employeeId = e.id));
INSERT @r SELECT '038',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.usp_employee_basic_details_action')) LIKE '%kept in step with EmployeeCode.employerId%'
              AND @noEmployer = 0 THEN 'Yes' ELSE 'No' END,
    CONCAT('usp_employee_basic_details_action sets Employee.EmployerId; ', @noEmployer, ' employee(s) still without one');

-- 039 Tracker list employer scope
INSERT @r SELECT '039',
    CASE WHEN EXISTS (SELECT 1 FROM sys.parameters WHERE object_id = OBJECT_ID('dbo.sp_Tracker_GetEmployerList') AND name = '@EmployerIDs')
         THEN 'Yes' ELSE 'No' END,
    'sp_Tracker_GetEmployerList has the @EmployerIDs parameter (needed before the new web app)';

-- 040 switch off logins with no account record (same rule as the script)
DECLARE @orphans INT = (SELECT COUNT(*) FROM dbo.tbl_User u
                        WHERE ISNULL(u.Flag, 1) <> 0
                          AND ((u.Role_ID = 10 AND NOT EXISTS (SELECT 1 FROM dbo.BrokerAccounts b WHERE b.Broker_ID = u.Ref_ID))
                            OR (u.Role_ID = 13 AND NOT EXISTS (SELECT 1 FROM dbo.Employer e WHERE e.id = u.Ref_ID))));
INSERT @r SELECT '040',
    CASE WHEN @orphans = 0 THEN 'Yes' ELSE 'No' END,
    CONCAT(@orphans, ' active Broker / Employer login(s) without an account record');

-- 041 file access rule
INSERT @r SELECT '041',
    CASE WHEN OBJECT_ID('dbo.fn_FileVisibleToUser') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'dbo.fn_FileVisibleToUser exists (needed before the web app version that uses it)';

-- 042 User Accounts list: login on/off state
INSERT @r SELECT '042',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_UserAccountsList')) LIKE '%LoginEnabled%' THEN 'Yes' ELSE 'No' END,
    'sp_UserAccountsList returns LoginEnabled (switched-off logins no longer listed as Active)';

-- 043 Tracker list filing year
INSERT @r SELECT '043',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Tracker_GetEmployerList')) LIKE '%AS FilingYear%' THEN 'Yes' ELSE 'No' END,
    'sp_Tracker_GetEmployerList returns FilingYear (shown next to the EIN in the Tracker list)';

-- 044 Employer logins made only from Tracker
INSERT @r SELECT '044',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_CreateUserAccounts')) LIKE '%THROW 50044%'
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_UserAccountsList')) LIKE '%EmployerPortal%' THEN 'Yes' ELSE 'No' END,
    'sp_CreateUserAccounts refuses the Employer role; sp_UserAccountsList lists Employer logins from their employer';

-- 047 Broker employer scope
INSERT @r SELECT '047',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_GetEmployersByStaffId')) LIKE '%tbl_Employer_Broker_Assignment%'
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_EmployersOnlyList')) LIKE '%tbl_Employer_Broker_Assignment%' THEN 'Yes' ELSE 'No' END,
    'Brokers reach their assigned employers; the Select Employer list is scoped for Broker and Employer logins';

-- 048 Import with a blank Affiliated EIN; transmitter address; unused procedure dropped
INSERT @r SELECT '048',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_ImportData_FromStaging')) LIKE '%048: MemberEIN%'
              AND COL_LENGTH('dbo.TransmitterConfig', 'AddressLine1') IS NOT NULL
              AND OBJECT_ID('dbo.sp_GetEmployerFormCount') IS NULL THEN 'Yes' ELSE 'No' END,
    'Import skips rows without an Affiliated EIN; TransmitterConfig has a mailing address (needed by the IRS manifest)';

-- 049 IRS record list, acknowledgement errors, employee date of birth
INSERT @r SELECT '049',
    CASE WHEN OBJECT_ID('dbo.IrsSubmissionRecord') IS NOT NULL
              AND OBJECT_ID('dbo.sp_AddSubmissionError') IS NOT NULL
              AND COL_LENGTH('dbo.IrsSubmissionErrors', 'UniqueRecordId') IS NOT NULL
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_GetEmployeeCodesList')) LIKE '%AS BirthDate%' THEN 'Yes' ELSE 'No' END,
    'IRS record list (Record IDs 1, 2, 3...), acknowledgement errors by employee, employee date of birth for ICHRA ages';

-- 050 IRS replacements and as-filed values for corrections
INSERT @r SELECT '050',
    CASE WHEN COL_LENGTH('dbo.IrsSubmissionLog', 'ReplacesReceiptId') IS NOT NULL
              AND COL_LENGTH('dbo.IrsSubmissionRecord', 'FiledTin') IS NOT NULL
              AND OBJECT_ID('dbo.sp_GetChildSubmissions') IS NOT NULL
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_UpdateSubmissionFiles')) LIKE '%@ReplacesReceiptId%' THEN 'Yes' ELSE 'No' END,
    'Rejected filings are re-sent as replacements; corrections name the records as filed';

-- 051 Supervisors and StaffAC see every employer
INSERT @r SELECT '051',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_EmployersOnlyList')) LIKE '%@RoleName = ''Account Manager''%'
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_GetAllEmployers')) LIKE '%051: StaffAC included%' THEN 'Yes' ELSE 'No' END,
    'Supervisors and StaffAC see every employer in the Select Employer list and at sign-in';

-- 052 Employee page indexes
INSERT @r SELECT '052',
    CASE WHEN (SELECT COUNT(*) FROM sys.indexes
               WHERE name IN ('IX_FlagGrid_YearEmployer', 'IX_FlagGrid_CodeFlag', 'IX_FlagGrid_HireCode', 'IX_FlagGrid_PayrollCode',
                              'IX_FlagGrid_StatusCode', 'IX_ACA360_EmployeeCode_Advanced', 'IX_ACA360_Employee_Core_Filters')) >= 7 THEN 'Yes' ELSE 'No' END,
    'Indexes the Employee page queries rely on';

-- 053 Warnings do not exclude rows from the import
INSERT @r SELECT '053',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Validation_UpdateStatus')) LIKE '%053: warnings and%' THEN 'Yes' ELSE 'No' END,
    'A row is kept out of the import only by an error, not by a warning or an information note';

-- 054 Administration > Dummy Data menu item
INSERT @r SELECT '054',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tbl_Menus m JOIN dbo.tbl_AccessPermissions p ON p.MenuId = m.Id AND p.IsEnabled = 1
                      JOIN dbo.tbl_Roles r ON r.ID = p.RoleId AND r.RoleName = 'SuperAdmin'
                      WHERE m.Controller = 'DummyData' AND m.IsActive = 1 AND m.Flag = 0 AND m.IsVisible = 1) THEN 'Yes' ELSE 'No' END,
    'Administration > Dummy Data menu item for SuperAdmin';

-- 055 Validation rule fixes; auto-corrections recorded
INSERT @r SELECT '055',
    CASE WHEN COL_LENGTH('dbo.StagingRowErrors', 'Detail') IS NOT NULL
              AND OBJECT_ID('dbo.StagingAutoCorrections') IS NOT NULL
              AND EXISTS (SELECT 1 FROM dbo.ZipPrefixState)
              AND EXISTS (SELECT 1 FROM dbo.ValidationRules WHERE RuleCode = 'AUTO_CORRECTED')
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Validation_ExecuteRules')) LIKE '%AssociatedEIN_In_EmployerTab%'
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Validation_AutoCorrect')) LIKE '%StagingAutoCorrections%' THEN 'Yes' ELSE 'No' END,
    'Lookup rules 12/74, rule 19, affordability by band, ZIP by state; auto-corrections recorded and shown';

-- 056 Error Triage fixes
INSERT @r SELECT '056',
    CASE WHEN COL_LENGTH('dbo.StagingAutoCorrections', 'Source') IS NOT NULL
              AND OBJECT_ID('dbo.StagingIssueSkips') IS NOT NULL
              AND OBJECT_ID('dbo.sp_GetFileCorrections') IS NOT NULL
              AND EXISTS (SELECT 1 FROM sys.parameters WHERE object_id = OBJECT_ID('dbo.sp_UpdateStagingField_NoValidation') AND name = '@ChangedBy')
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_ExecuteValidationEngine')) LIKE '%sp_Validation_ReapplySkips%'
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Validation_AutoCorrect')) LIKE '%056: earlier runs%' THEN 'Yes' ELSE 'No' END,
    'Correction Editor changes kept with who made them; skips and auto-corrections survive re-validation';

-- 057 Import Analysis: What changed
INSERT @r SELECT '057',
    CASE WHEN OBJECT_ID('dbo.sp_ImportAnalysis_Changes') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'What an import would change, and employees missing from the file';

-- 058 Why these codes?
INSERT @r SELECT '058',
    CASE WHEN OBJECT_ID('dbo.EmployeeCodeMonthDetail') IS NOT NULL AND OBJECT_ID('dbo.sp_GetCodeExplanation') IS NOT NULL
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Generate1095Codes')) LIKE '%EmployeeCodeMonthDetail%' THEN 'Yes' ELSE 'No' END,
    'The code generator keeps each month''s working values, which explain the 1095-C codes';

-- 059 Global search
INSERT @r SELECT '059',
    CASE WHEN OBJECT_ID('dbo.sp_GlobalSearch_Employees') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'Ctrl+K search finds the selected employer''s employees';

-- 060 Employee page speed
INSERT @r SELECT '060',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_EmployeeList_Filter')) LIKE '%@HasEmployerFilter%'
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Employee_QuickStats')) LIKE '%#SelectedIDs%' THEN 'Yes' ELSE 'No' END,
    'The Employee list and its counts are planned for the filters in use';

-- 061 Acknowledged warnings
INSERT @r SELECT '061',
    CASE WHEN OBJECT_ID('dbo.IssueAcknowledgements') IS NOT NULL AND COL_LENGTH('dbo.StagingIssueSkips', 'Reason') IS NOT NULL
              AND EXISTS (SELECT 1 FROM sys.parameters WHERE object_id = OBJECT_ID('dbo.sp_UpdateStagingRowErrorStatus') AND name = '@Reason')
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Validation_ReapplySkips')) LIKE '%IssueAcknowledgements%' THEN 'Yes' ELSE 'No' END,
    'Warnings are acknowledged with a reason, carried over to later files of the employer';

-- 062 System Health
INSERT @r SELECT '062',
    CASE WHEN OBJECT_ID('dbo.sp_SystemHealth') IS NOT NULL
              AND EXISTS (SELECT 1 FROM dbo.tbl_Menus WHERE Controller = 'SystemHealth' AND IsActive = 1 AND Flag = 0) THEN 'Yes' ELSE 'No' END,
    'Administration > System Health; information log lines are no longer counted as errors';

-- 063 Filing deadlines
INSERT @r SELECT '063',
    CASE WHEN OBJECT_ID('dbo.FilingDeadlines') IS NOT NULL AND OBJECT_ID('dbo.sp_FilingDeadlines_Upcoming') IS NOT NULL
              AND EXISTS (SELECT 1 FROM dbo.tbl_Menus WHERE Controller = 'FilingDeadlines' AND IsActive = 1 AND Flag = 0) THEN 'Yes' ELSE 'No' END,
    'IRS and state filing deadlines (dashboards, Calendar, Administration > Filing Deadlines)';

-- 064 Year over year
INSERT @r SELECT '064',
    CASE WHEN OBJECT_ID('dbo.sp_PenaltyRisk_YearOverYear') IS NOT NULL AND OBJECT_ID('dbo.sp_PenaltyExposureGrowth') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'Penalty Risk compared with last year; employers whose exposure grew most';

-- 065 Faster validation
INSERT @r SELECT '065',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Validation_UpdateStatus')) NOT LIKE '%UPDATE STATISTICS%' THEN 'Yes' ELSE 'No' END,
    'Validation no longer updates statistics on every run (faster saves in the Error Triage editor)';

-- 066 Duplicate SSNs
INSERT @r SELECT '066',
    CASE WHEN OBJECT_ID('dbo.sp_DuplicateSsn_Report') IS NOT NULL
              AND EXISTS (SELECT 1 FROM dbo.ValidationRules WHERE RuleCode = 'EMP-160')
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Validation_ExecuteRules')) LIKE '%SSN_UnderOtherEmployer%' THEN 'Yes' ELSE 'No' END,
    'Duplicate SSNs: warning on upload (EMP-160) and Reports > Duplicate SSNs';

-- 067 Rule what-if
INSERT @r SELECT '067',
    CASE WHEN OBJECT_ID('dbo.sp_Validation_RuleWhatIf') IS NOT NULL AND OBJECT_ID('dbo.RuleWhatIfHits') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'Validation Rules: try a rule on recent files before saving it';

-- 068 Employee change history
INSERT @r SELECT '068',
    CASE WHEN OBJECT_ID('dbo.EmployeeChangeHistory') IS NOT NULL AND OBJECT_ID('dbo.sp_EmployeeChangeHistory') IS NOT NULL
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_ImportData_FromStaging')) LIKE '%EmployeeChangeHistory%' THEN 'Yes' ELSE 'No' END,
    'Employee History tab; imports record what they change';

-- 069 Employer timeline
INSERT @r SELECT '069',
    CASE WHEN OBJECT_ID('dbo.sp_EmployerTimeline') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'Employer Timeline tab';

-- 070 Notifications
INSERT @r SELECT '070',
    CASE WHEN OBJECT_ID('dbo.sp_Notifications_Recent') IS NOT NULL AND OBJECT_ID('dbo.NotificationPreferences') IS NOT NULL
              AND COL_LENGTH('dbo.tbl_UserNotifications', 'EventKey') IS NOT NULL
              AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_UpdateFileLogStatus')) LIKE '%sp_Notify_FileEvent%' THEN 'Yes' ELSE 'No' END,
    'Notifications: file events, deadline reminders, System Health digest, per-user choices';

-- 071 Safe harbor advice
INSERT @r SELECT '071',
    CASE WHEN OBJECT_ID('dbo.sp_SafeHarborAdvice') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'Best safe harbor per employee (advice only)';

-- 072 State filings
INSERT @r SELECT '072',
    CASE WHEN OBJECT_ID('dbo.sp_StateFilings_Summary') IS NOT NULL AND OBJECT_ID('dbo.sp_StateFilings_Residents') IS NOT NULL
              AND EXISTS (SELECT 1 FROM dbo.tbl_Menus WHERE Controller = 'StateFilings' AND IsActive = 1 AND Flag = 0) THEN 'Yes' ELSE 'No' END,
    'Reports > State Filings (CA, NJ, RI, DC, MA) and the MA HIRD deadline';

-- 073 Employer column mappings
INSERT @r SELECT '073',
    CASE WHEN OBJECT_ID('dbo.EmployerColumnMaps') IS NOT NULL AND OBJECT_ID('dbo.sp_EmployerColumnMaps_Get') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'Column mappings kept for each employer (Files > Map columns)';

-- 074 Bulk actions
INSERT @r SELECT '074',
    CASE WHEN OBJECT_ID('dbo.sp_EmployeeCodes_SetLocked') IS NOT NULL AND OBJECT_ID('dbo.sp_AssignableStaff') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'Bulk lock / unlock codes and bulk employer assignment';

-- 075 My sign-in sessions
INSERT @r SELECT '075',
    CASE WHEN OBJECT_ID('dbo.sp_AuthSession_ListForSubject') IS NOT NULL AND OBJECT_ID('dbo.sp_AuthSession_RevokeOthers') IS NOT NULL THEN 'Yes' ELSE 'No' END,
    'Account Settings: where you are signed in, sign out everywhere else';

SELECT DB_NAME() AS [Database], Script, Applied, Detail FROM @r ORDER BY Script;

SELECT CASE WHEN EXISTS (SELECT 1 FROM @r WHERE Applied = 'No')
            THEN 'Missing: ' + STUFF((SELECT ', ' + Script FROM @r WHERE Applied = 'No' ORDER BY Script FOR XML PATH('')), 1, 2, '')
            ELSE 'All of 014, 023-034, 036-044 and 047-075 are in place.' END AS Summary;
