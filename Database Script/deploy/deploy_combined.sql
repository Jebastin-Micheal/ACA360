/* =============================================================================
   deploy_combined.sql   (generated from Database Script/fixes)

   Every fix script from 023 to 075 except 035, 045 and 046, in order, as one file. 035 (drops the archive
   schema) is deliberately NOT included: run it separately, later, after a full
   backup, once 034 has been in use for a while. 045 and 046 clean up data on the
   developer database only and are never deployed.

   How to run (SSMS):
   - Take a backup first.
   - Run deploy_check.sql to see which scripts the database already has.
     Every script is safe to re-run, so running the whole file is fine either way.
   - Run this file. Each script prints a "=== 0xx" line; read the Messages tab
     for errors before deploying the web app. SSMS continues after a failed batch,
     so check the messages; or turn on Query > SQLCMD Mode and uncomment the
     ":on error exit" line below to stop at the first error.
   - Run it BEFORE deploying the web app:
       039 must be in place before the version that passes @EmployerIDs to
           sp_Tracker_GetEmployerList;
       041 must be in place before the version whose file actions (download,
           details, archive, delete, import analysis) call dbo.fn_FileVisibleToUser,
           or every one of them fails.
       (042 is not required first: without it User Accounts just keeps showing
           switched-off logins as Active.)
       (044 is not required first either: the web app already refuses to create
           Employer logins; 044 makes the database refuse them too and lists
           Tracker-made Employer logins in User Accounts.)
       048 adds the transmitter address the IRS manifest needs; until it is
           in place and the address is set (see 048), generating IRS files
           stops and says so. The import fix in 048 can go in at any time.
       049 must be in place before the web app version that numbers IRS records
           1, 2, 3... (it saves them in dbo.IrsSubmissionRecord), or generating
           IRS files fails. 050 likewise (the record list carries the filed name
           and TIN, and regenerating passes the replacement receipt).
       056 must be in place before the web app version whose Error Triage editor
           passes who made a change (sp_UpdateStagingField_NoValidation @ChangedBy)
           and opens the Corrections tab; 057, 058 and 059 before the version with
           "What changed", "Why these codes?" and the Ctrl+K search. 060 (Employee
           page speed) can go in at any time.
       061 before the version whose Error Triage asks why a warning is
           acknowledged (sp_UpdateStagingRowErrorStatus @Reason); 062, 063 and 064
           before the version with System Health, Filing Deadlines (and their menu
           items) and the year-over-year comparison. 063 seeds IRS and state dates
           for tax years 2025 and 2026; confirm the state ones on Filing Deadlines.
       066-074 before the version with Duplicate SSNs, the rule what-if, employee
           history and employer timeline, notifications, safe harbor advice, State
           Filings, column mapping and bulk actions. 070 and 073 above all: the
           notification bell on every page calls sp_Notifications_Recent, and every
           uploaded file's check reads sp_EmployerColumnMaps_Get.
       075 before the version whose Account Settings lists where the user is signed in.
   - Script 037 switches off duplicate staff logins; its broker section stays off
     unless @DisableOrphanBrokerLogins is set to 1 inside it.
   - Script 040 switches off Broker / Employer logins whose account record does not
     exist (Flag = 0, sessions revoked; nothing deleted). It prints the logins it
     changed; read that list.
   Included: 023, 024, 025, 026, 027, 028, 029, 030, 031, 032, 033, 034, 036, 037, 038, 039, 040, 041, 042, 043, 044, 047, 048, 049, 050, 051, 052, 053, 054, 055, 056, 057, 058, 059, 060, 061, 062, 063, 064, 065, 066, 067, 068, 069, 070, 071, 072, 073, 074, 075
   ============================================================================= */
-- :on error exit
SET NOCOUNT ON;
GO

/* ############################################################################
   023_server_side_auth_sessions.sql
   ############################################################################ */
PRINT '=== 023_server_side_auth_sessions.sql';
GO
/* =============================================================================
   023_server_side_auth_sessions.sql

   Server-side login sessions, so that logging out actually ends a session and
   an idle session expires even if the browser still holds its cookie.

   Before this, the login cookie was the only record of a session. Logout just
   asked the browser to delete it; a copy of the cookie kept working for up to
   30 minutes after logout, and "Remember me" kept it valid for 30 days.

   The web app now:
     - inserts a row here on every sign-in and puts its SessionId in the cookie
     - checks the row on every request (missing, revoked, or idle > 15 min
       => the user is sent to the login page)
     - revokes the row on every sign-out

   RUN THIS BEFORE DEPLOYING THE MATCHING C# CHANGE. The C# rejects any cookie
   without a valid session row, so every user currently logged in will be sent
   to the login page once, when the new build goes live. That is expected.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.tbl_AuthSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_AuthSessions
    (
        SessionId       UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_tbl_AuthSessions PRIMARY KEY,
        -- "user:<User_ID>" for staff, "portal:<EmployeeId>" for the employee portal.
        Subject         NVARCHAR(100)    NOT NULL,
        CreatedAt       DATETIME2(3)     NOT NULL
            CONSTRAINT DF_tbl_AuthSessions_CreatedAt DEFAULT SYSUTCDATETIME(),
        LastActivityAt  DATETIME2(3)     NOT NULL
            CONSTRAINT DF_tbl_AuthSessions_LastActivityAt DEFAULT SYSUTCDATETIME(),
        RevokedAt       DATETIME2(3)     NULL,
        IpAddress       NVARCHAR(64)     NULL,
        UserAgent       NVARCHAR(512)    NULL
    );

    CREATE INDEX IX_tbl_AuthSessions_Subject   ON dbo.tbl_AuthSessions (Subject);
    CREATE INDEX IX_tbl_AuthSessions_CreatedAt ON dbo.tbl_AuthSessions (CreatedAt);
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_Create
    @SessionId  UNIQUEIDENTIFIER,
    @Subject    NVARCHAR(100),
    @IpAddress  NVARCHAR(64)  = NULL,
    @UserAgent  NVARCHAR(512) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.tbl_AuthSessions (SessionId, Subject, IpAddress, UserAgent)
    VALUES (@SessionId, @Subject, @IpAddress, LEFT(@UserAgent, 512));
END
GO

/* Returns one row, IsValid = 1 or 0.
   @Touch = 0 is for background polling (notifications, file status): it checks
   the session without counting as user activity, so an open tab that is only
   polling still times out. LastActivityAt is written at most once per
   @TouchIntervalSeconds, so a busy page is not a write per request. */
CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_Validate
    @SessionId             UNIQUEIDENTIFIER,
    @IdleMinutes           INT,
    @Touch                 BIT,
    @TouchIntervalSeconds  INT = 60
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now  DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Last DATETIME2(3);

    SELECT @Last = LastActivityAt
    FROM   dbo.tbl_AuthSessions
    WHERE  SessionId = @SessionId
      AND  RevokedAt IS NULL;

    IF @Last IS NULL
    BEGIN
        SELECT CAST(0 AS BIT) AS IsValid;
        RETURN;
    END

    IF @Last < DATEADD(MINUTE, -@IdleMinutes, @Now)
    BEGIN
        -- Close it for good, so a later request cannot revive an expired session.
        UPDATE dbo.tbl_AuthSessions
        SET    RevokedAt = @Now
        WHERE  SessionId = @SessionId
          AND  RevokedAt IS NULL;

        SELECT CAST(0 AS BIT) AS IsValid;
        RETURN;
    END

    IF @Touch = 1 AND @Last < DATEADD(SECOND, -@TouchIntervalSeconds, @Now)
    BEGIN
        UPDATE dbo.tbl_AuthSessions
        SET    LastActivityAt = @Now
        WHERE  SessionId = @SessionId
          AND  RevokedAt IS NULL;
    END

    SELECT CAST(1 AS BIT) AS IsValid;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_Revoke
    @SessionId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tbl_AuthSessions
    SET    RevokedAt = SYSUTCDATETIME()
    WHERE  SessionId = @SessionId
      AND  RevokedAt IS NULL;
END
GO

-- Called daily by a Hangfire recurring job. Keeps 30 days for audit.
CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_Purge
    @OlderThanDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.tbl_AuthSessions
    WHERE  CreatedAt < DATEADD(DAY, -@OlderThanDays, SYSUTCDATETIME());
END
GO

/* ############################################################################
   024_file_status_buckets.sql
   ############################################################################ */
PRINT '=== 024_file_status_buckets.sql';
GO
/* =============================================================================
   024_file_status_buckets.sql

   One definition of "what state is this uploaded file in", shared by the file
   grid, the summary cards and the status filter chips on the File Upload page.

   Before this, four places each had their own rules:
     - FileStatusResolver.cs + two sets of overrides (grid, live poller)
     - sp_GetFileDashboardStats (the cards)
     - the FilterType block in sp_SearchFileLogsPaginated (the chips)
   and they disagreed. Example: a file that had finished validating and was
   waiting to be imported showed "DA Review" in the grid but was counted as
   "Processing" by the cards and matched by the "Processing" chip.

   PART 1  dbo.fn_FileStatusBucket
           Returns the bucket for a (ValidationStatusId, WorkflowStatusId,
           is-assigned) triple. Mirrors FileStatusResolver.Resolve in
           ACA360.Core/Helpers/FileStatusResolver.cs rule for rule and in the
           same order. CHANGE BOTH TOGETHER.

   PART 2  sp_SearchFileLogsPaginated
           - status chips filter by bucket:
               Attention  = NeedsAssignment, NeedsFixes, ReadyToImport, ImportFailed
               Processing = Working
               Imported   = Imported
             Rejected/invalid files are no longer in "Attention": nothing can
             be done with them except a new upload. They still show under All.
           - new 3rd result set: the card counts, computed from the same rows
             the grid is drawn from (same view, date range, "My uploads",
             search and role visibility), ignoring only the status chip.
           - the Employer security join is now an EXISTS. The old LEFT JOIN
             returned a file once per matching Employer row, so an EIN with
             duplicate employer rows for a year would list its files twice.
           Result sets 1 and 2 are unchanged in shape, so existing callers
           keep working.

   sp_GetFileDashboardStats is left in place but is no longer called by the
   File Upload page.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER FUNCTION dbo.fn_FileStatusBucket
(
    @Vs         INT,
    @Ws         INT,
    @IsAssigned BIT
)
RETURNS VARCHAR(20)
WITH SCHEMABINDING
AS
BEGIN
    RETURN CASE
        WHEN @Ws IN (298, 231)                        THEN 'OnHold'          -- On hold / awaiting client
        WHEN @Ws = 299                                THEN 'Closed'          -- Superseded
        WHEN @Vs = 40                                 THEN 'Closed'          -- Invalid file
        WHEN @Ws IN (209, 241, 290)                   THEN 'Closed'          -- Rejected
        WHEN @Ws IN (250, 251)                        THEN 'Working'         -- Importing
        WHEN @Ws = 260 OR @Vs = 90                    THEN 'Imported'
        WHEN @Ws = 261                                THEN 'ImportFailed'
        WHEN @Ws = 200 OR @Vs = 20                    THEN 'Working'         -- Structure check
        WHEN ISNULL(@IsAssigned, 0) = 0 OR @Ws = 210  THEN 'NeedsAssignment'
        WHEN @Vs IN (10, 30)                          THEN 'Working'         -- Queued / validating
        WHEN @Vs = 41                                 THEN 'NeedsFixes'      -- Mapping required
        WHEN @Vs IN (50, 60, 99) OR @Ws = 230         THEN 'NeedsFixes'      -- Data errors
        WHEN @Vs IN (70, 80)                          THEN 'ReadyToImport'
        ELSE 'Other'
    END;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_SearchFileLogsPaginated]
    @SearchTerm VARCHAR(255) = NULL,
    @PageSize INT,
    @PageNumber INT,
    @UserId INT,
    @SortColumn VARCHAR(50) = 'UploadedAt',
    @SortOrder VARCHAR(4) = 'DESC',
    @FilterType VARCHAR(20) = 'All',
    -- Dashboard Filters
    @DateFrom DATETIME = NULL,
    @DateTo DATETIME = NULL,
    @ShowMyFiles BIT = 0,
    -- View Mode: 'Active' (Default), 'Archived', 'RecycleBin'
    @ViewMode VARCHAR(20) = 'Active'
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;

    -- 1. Get User Role & Scope
    DECLARE @RoleID INT;
    DECLARE @RefID INT;
    DECLARE @CanSeeAll BIT = 0;

    SELECT @RoleID = Role_ID, @RefID = Ref_ID
    FROM tbl_User
    WHERE User_ID = @UserId;

    -- Level 1: Super Users (SuperAdmin, Admin, Director, Supervisors, Audit)
    IF @RoleID IN (2, 3, 4, 5, 6, 15)
        SET @CanSeeAll = 1;

    -- 2. Every file this user can see in this view. The status chip is applied
    --    afterwards, so the card counts below cover the whole view.
    SELECT
        log.FileLogId,
        log.OriginalFileName,
        log.EmployerName,
        log.TaxID,
        log.UploadedAt,
        log.PlanYear,
        vs.StatusName AS ValidationStatus,
        ws.StatusName AS WorkflowStatus,
        log.ValidationStatusId,
        log.WorkflowStatusId,
        log.TotalRowsValidated,
        log.FatalErrorCount,
        log.WarningCount,
        log.ErrorPercentage,
        -- Processing Time
        ISNULL(CONVERT(VARCHAR(8), DATEADD(SECOND, DATEDIFF(SECOND, log.ProcessingStartTime, ISNULL(log.ProcessingEndTime, GETDATE())), 0), 108), '--') AS ProcessingTime,
        -- Priority Rank
        CASE
            WHEN log.ValidationStatusId IN (10, 20, 30) AND DATEDIFF(HOUR, log.UploadedAt, GETDATE()) > 24 THEN 0 -- Critical
            WHEN log.ValidationStatusId IN (10, 20, 30) AND DATEDIFF(HOUR, log.UploadedAt, GETDATE()) > 4 THEN 1  -- Warning
            ELSE 2 -- Normal
        END AS PriorityRank,
        -- Priority Level Label
        CASE
            WHEN log.ValidationStatusId IN (10, 20, 30) AND DATEDIFF(HOUR, log.UploadedAt, GETDATE()) > 24 THEN 'Critical'
            WHEN log.ValidationStatusId IN (10, 20, 30) AND DATEDIFF(HOUR, log.UploadedAt, GETDATE()) > 4 THEN 'Warning'
            ELSE 'Normal'
        END AS PriorityLevel,
        u_up.User_Name AS UploadedByName,
        u_as.User_Name AS AssignedToName,
        t.TemplateName,
        log.IsDeleted,
        log.IsArchived,
        log.UploadedByUserId,
        log.AssignedToUserId,
        dbo.fn_FileStatusBucket(
            log.ValidationStatusId,
            log.WorkflowStatusId,
            CASE WHEN NULLIF(LTRIM(RTRIM(log.AssignedToUserId)), '') IS NULL THEN 0 ELSE 1 END
        ) AS StatusBucket
    INTO #Scoped
    FROM UploadedFileLog log
    LEFT JOIN ValidationStatuses vs ON log.ValidationStatusId = vs.ValidationStatusId
    LEFT JOIN WorkflowStatuses ws ON log.WorkflowStatusId = ws.WorkflowStatusId
    LEFT JOIN ImportTemplates t ON log.ImportTemplateId = t.TemplateId
    LEFT JOIN tbl_User u_up ON log.UploadedByUserId = u_up.User_ID
    LEFT JOIN tbl_User u_as ON log.AssignedToUserId = u_as.User_ID
    WHERE
        -- View Mode Logic (Mutually Exclusive)
        (
            (@ViewMode = 'Active'      AND log.IsDeleted = 0 AND log.IsArchived = 0) OR
            (@ViewMode = 'Archived'    AND log.IsDeleted = 0 AND log.IsArchived = 1) OR
            (@ViewMode = 'RecycleBin'  AND log.IsDeleted = 1)
        )
        -- Standard Filters
        AND (@SearchTerm IS NULL OR log.OriginalFileName LIKE '%' + @SearchTerm + '%' OR log.EmployerName LIKE '%' + @SearchTerm + '%')
        -- Date Range Filter
        AND (@DateFrom IS NULL OR log.UploadedAt >= @DateFrom)
        AND (@DateTo IS NULL OR log.UploadedAt <= DATEADD(DAY, 1, @DateTo))
        -- Strict "My Files"
        AND (
            (@ShowMyFiles = 1 AND log.UploadedByUserId = @UserId)
            OR
            (@ShowMyFiles = 0)
        )
        -- Security Scoping (EXISTS, so a file is never repeated per matching employer row)
        AND (
            @CanSeeAll = 1
            OR log.UploadedByUserId = @UserId
            OR log.AssignedToUserId = @UserId
            OR EXISTS (
                SELECT 1
                FROM Employer emp
                WHERE emp.taxid = log.TaxID
                  AND emp.filingYear = log.PlanYear
                  AND (
                        (@RoleID = 13 AND emp.id = @RefID)
                     OR (@RoleID = 16 AND EXISTS (SELECT 1 FROM tbl_Portfolio_Assignment pa WHERE pa.AM_UserID = @UserId AND pa.EmployerID = emp.id AND pa.IsActive = 1))
                     OR (@RoleID = 11 AND EXISTS (SELECT 1 FROM tbl_Operational_Assignment oa WHERE oa.DA_UserID = @UserId AND oa.EmployerID = emp.id AND oa.IsActive = 1))
                     OR (@RoleID = 10 AND EXISTS (SELECT 1 FROM tbl_Employer_Broker_Assignment ba WHERE ba.Broker_UserID = @UserId AND ba.EmployerID = emp.id AND ba.IsActive = 1))
                  )
            )
        );

    -- 3. Apply the status chip
    SELECT *
    INTO #SearchResults
    FROM #Scoped
    WHERE
        @ViewMode = 'RecycleBin' -- Show everything in bin regardless of status
        OR @FilterType = 'All'
        OR (@FilterType = 'Attention'  AND StatusBucket IN ('NeedsAssignment', 'NeedsFixes', 'ReadyToImport', 'ImportFailed'))
        OR (@FilterType = 'Processing' AND StatusBucket = 'Working')
        OR (@FilterType = 'Imported'   AND StatusBucket = 'Imported');

    -- Result Set 1: Paged Data
    SELECT * FROM #SearchResults
    ORDER BY
        CASE WHEN @SortColumn = 'UploadedAt' AND @SortOrder = 'DESC' THEN UploadedAt END DESC,
        CASE WHEN @SortColumn = 'UploadedAt' AND @SortOrder = 'ASC' THEN UploadedAt END ASC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

    -- Result Set 2: Total Count (for paging)
    SELECT COUNT(*) AS TotalCount FROM #SearchResults;

    -- Result Set 3: Summary card counts for the whole view
    SELECT
        COUNT(*) AS Total,
        ISNULL(SUM(CASE WHEN StatusBucket IN ('NeedsAssignment', 'NeedsFixes', 'ReadyToImport', 'ImportFailed') THEN 1 ELSE 0 END), 0) AS ActionRequired,
        ISNULL(SUM(CASE WHEN StatusBucket = 'Working'  THEN 1 ELSE 0 END), 0) AS Processing,
        ISNULL(SUM(CASE WHEN StatusBucket = 'Imported' THEN 1 ELSE 0 END), 0) AS Completed
    FROM #Scoped;

    DROP TABLE #SearchResults;
    DROP TABLE #Scoped;
END
GO

/* ############################################################################
   025_file_progress_updates.sql
   ############################################################################ */
PRINT '=== 025_file_progress_updates.sql';
GO
/* =============================================================================
   025_file_progress_updates.sql

   Lets the structure check and validation report progress, not just the import.

   UploadedFileLog already has ValidationCurrentStep / ValidationProgressPercent /
   ValidationRowsProcessed / ValidationTotalRows, but only sp_ProcessEmployerData
   (the import) wrote to them, so a file being validated showed an endless
   spinner. The web app now calls this procedure while a file is checked and
   validated, and the File Upload page shows the step and a real percentage.

   Passing NULLs clears the columns. The app does that when a phase ends, so a
   later phase never shows a leftover step from an earlier one.

   Row counts are left to the import: during validation the detail goes into
   the step text ("Reading Employee: 1,200 of 4,800 rows").

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateFileProgress
    @FileLogId INT,
    @Step      VARCHAR(100)  = NULL,
    @Percent   DECIMAL(5, 2) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.UploadedFileLog
    SET    ValidationCurrentStep     = LEFT(@Step, 100),
           ValidationProgressPercent = @Percent,
           ValidationRowsProcessed   = NULL,
           ValidationTotalRows       = NULL
    WHERE  FileLogId = @FileLogId;
END
GO

/* ############################################################################
   026_file_status_filter_by_bucket.sql
   ############################################################################ */
PRINT '=== 026_file_status_filter_by_bucket.sql';
GO
/* =============================================================================
   026_file_status_filter_by_bucket.sql

   Adds status filter chips on the File Upload page for single buckets:
   "Waiting for analyst", "Needs fixes", "Ready to import", "On hold".

   Rather than another hard-coded FilterType per chip, @FilterType may now be
   any bucket name returned by dbo.fn_FileStatusBucket (script 024) --
   NeedsAssignment, NeedsFixes, ReadyToImport, ImportFailed, OnHold, Closed,
   Working, Imported -- and filters to exactly that bucket. The existing
   values keep their meaning:
       All         everything
       Attention   NeedsAssignment + NeedsFixes + ReadyToImport + ImportFailed
       Processing  Working
       Imported    Imported

   The procedure is otherwise identical to script 024. Requires script 024.
   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_SearchFileLogsPaginated]
    @SearchTerm VARCHAR(255) = NULL,
    @PageSize INT,
    @PageNumber INT,
    @UserId INT,
    @SortColumn VARCHAR(50) = 'UploadedAt',
    @SortOrder VARCHAR(4) = 'DESC',
    @FilterType VARCHAR(20) = 'All',
    -- Dashboard Filters
    @DateFrom DATETIME = NULL,
    @DateTo DATETIME = NULL,
    @ShowMyFiles BIT = 0,
    -- View Mode: 'Active' (Default), 'Archived', 'RecycleBin'
    @ViewMode VARCHAR(20) = 'Active'
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;

    -- 1. Get User Role & Scope
    DECLARE @RoleID INT;
    DECLARE @RefID INT;
    DECLARE @CanSeeAll BIT = 0;

    SELECT @RoleID = Role_ID, @RefID = Ref_ID
    FROM tbl_User
    WHERE User_ID = @UserId;

    -- Level 1: Super Users (SuperAdmin, Admin, Director, Supervisors, Audit)
    IF @RoleID IN (2, 3, 4, 5, 6, 15)
        SET @CanSeeAll = 1;

    -- 2. Every file this user can see in this view. The status chip is applied
    --    afterwards, so the card counts below cover the whole view.
    SELECT
        log.FileLogId,
        log.OriginalFileName,
        log.EmployerName,
        log.TaxID,
        log.UploadedAt,
        log.PlanYear,
        vs.StatusName AS ValidationStatus,
        ws.StatusName AS WorkflowStatus,
        log.ValidationStatusId,
        log.WorkflowStatusId,
        log.TotalRowsValidated,
        log.FatalErrorCount,
        log.WarningCount,
        log.ErrorPercentage,
        -- Processing Time
        ISNULL(CONVERT(VARCHAR(8), DATEADD(SECOND, DATEDIFF(SECOND, log.ProcessingStartTime, ISNULL(log.ProcessingEndTime, GETDATE())), 0), 108), '--') AS ProcessingTime,
        -- Priority Rank
        CASE
            WHEN log.ValidationStatusId IN (10, 20, 30) AND DATEDIFF(HOUR, log.UploadedAt, GETDATE()) > 24 THEN 0 -- Critical
            WHEN log.ValidationStatusId IN (10, 20, 30) AND DATEDIFF(HOUR, log.UploadedAt, GETDATE()) > 4 THEN 1  -- Warning
            ELSE 2 -- Normal
        END AS PriorityRank,
        -- Priority Level Label
        CASE
            WHEN log.ValidationStatusId IN (10, 20, 30) AND DATEDIFF(HOUR, log.UploadedAt, GETDATE()) > 24 THEN 'Critical'
            WHEN log.ValidationStatusId IN (10, 20, 30) AND DATEDIFF(HOUR, log.UploadedAt, GETDATE()) > 4 THEN 'Warning'
            ELSE 'Normal'
        END AS PriorityLevel,
        u_up.User_Name AS UploadedByName,
        u_as.User_Name AS AssignedToName,
        t.TemplateName,
        log.IsDeleted,
        log.IsArchived,
        log.UploadedByUserId,
        log.AssignedToUserId,
        dbo.fn_FileStatusBucket(
            log.ValidationStatusId,
            log.WorkflowStatusId,
            CASE WHEN NULLIF(LTRIM(RTRIM(log.AssignedToUserId)), '') IS NULL THEN 0 ELSE 1 END
        ) AS StatusBucket
    INTO #Scoped
    FROM UploadedFileLog log
    LEFT JOIN ValidationStatuses vs ON log.ValidationStatusId = vs.ValidationStatusId
    LEFT JOIN WorkflowStatuses ws ON log.WorkflowStatusId = ws.WorkflowStatusId
    LEFT JOIN ImportTemplates t ON log.ImportTemplateId = t.TemplateId
    LEFT JOIN tbl_User u_up ON log.UploadedByUserId = u_up.User_ID
    LEFT JOIN tbl_User u_as ON log.AssignedToUserId = u_as.User_ID
    WHERE
        -- View Mode Logic (Mutually Exclusive)
        (
            (@ViewMode = 'Active'      AND log.IsDeleted = 0 AND log.IsArchived = 0) OR
            (@ViewMode = 'Archived'    AND log.IsDeleted = 0 AND log.IsArchived = 1) OR
            (@ViewMode = 'RecycleBin'  AND log.IsDeleted = 1)
        )
        -- Standard Filters
        AND (@SearchTerm IS NULL OR log.OriginalFileName LIKE '%' + @SearchTerm + '%' OR log.EmployerName LIKE '%' + @SearchTerm + '%')
        -- Date Range Filter
        AND (@DateFrom IS NULL OR log.UploadedAt >= @DateFrom)
        AND (@DateTo IS NULL OR log.UploadedAt <= DATEADD(DAY, 1, @DateTo))
        -- Strict "My Files"
        AND (
            (@ShowMyFiles = 1 AND log.UploadedByUserId = @UserId)
            OR
            (@ShowMyFiles = 0)
        )
        -- Security Scoping (EXISTS, so a file is never repeated per matching employer row)
        AND (
            @CanSeeAll = 1
            OR log.UploadedByUserId = @UserId
            OR log.AssignedToUserId = @UserId
            OR EXISTS (
                SELECT 1
                FROM Employer emp
                WHERE emp.taxid = log.TaxID
                  AND emp.filingYear = log.PlanYear
                  AND (
                        (@RoleID = 13 AND emp.id = @RefID)
                     OR (@RoleID = 16 AND EXISTS (SELECT 1 FROM tbl_Portfolio_Assignment pa WHERE pa.AM_UserID = @UserId AND pa.EmployerID = emp.id AND pa.IsActive = 1))
                     OR (@RoleID = 11 AND EXISTS (SELECT 1 FROM tbl_Operational_Assignment oa WHERE oa.DA_UserID = @UserId AND oa.EmployerID = emp.id AND oa.IsActive = 1))
                     OR (@RoleID = 10 AND EXISTS (SELECT 1 FROM tbl_Employer_Broker_Assignment ba WHERE ba.Broker_UserID = @UserId AND ba.EmployerID = emp.id AND ba.IsActive = 1))
                  )
            )
        );

    -- 3. Apply the status chip
    SELECT *
    INTO #SearchResults
    FROM #Scoped
    WHERE
        @ViewMode = 'RecycleBin' -- Show everything in bin regardless of status
        OR @FilterType = 'All'
        OR (@FilterType = 'Attention'  AND StatusBucket IN ('NeedsAssignment', 'NeedsFixes', 'ReadyToImport', 'ImportFailed'))
        OR (@FilterType = 'Processing' AND StatusBucket = 'Working')
        OR (@FilterType = 'Imported'   AND StatusBucket = 'Imported')
        OR StatusBucket = @FilterType; -- any single bucket, e.g. 'NeedsFixes'

    -- Result Set 1: Paged Data
    SELECT * FROM #SearchResults
    ORDER BY
        CASE WHEN @SortColumn = 'UploadedAt' AND @SortOrder = 'DESC' THEN UploadedAt END DESC,
        CASE WHEN @SortColumn = 'UploadedAt' AND @SortOrder = 'ASC' THEN UploadedAt END ASC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

    -- Result Set 2: Total Count (for paging)
    SELECT COUNT(*) AS TotalCount FROM #SearchResults;

    -- Result Set 3: Summary card counts for the whole view
    SELECT
        COUNT(*) AS Total,
        ISNULL(SUM(CASE WHEN StatusBucket IN ('NeedsAssignment', 'NeedsFixes', 'ReadyToImport', 'ImportFailed') THEN 1 ELSE 0 END), 0) AS ActionRequired,
        ISNULL(SUM(CASE WHEN StatusBucket = 'Working'  THEN 1 ELSE 0 END), 0) AS Processing,
        ISNULL(SUM(CASE WHEN StatusBucket = 'Imported' THEN 1 ELSE 0 END), 0) AS Completed
    FROM #Scoped;

    DROP TABLE #SearchResults;
    DROP TABLE #Scoped;
END
GO

/* ############################################################################
   027_admin_password_reset.sql
   ############################################################################ */
PRINT '=== 027_admin_password_reset.sql';
GO
/* =============================================================================
   027_admin_password_reset.sql

   Makes an admin password reset (User Accounts > Edit > Change Password) a real
   reset, and stops storing staff temporary passwords as plain text.

   sp_UpdateUserAccounts
     1. A new password now replaces the old one. Before, the SP only wrote
        Temp_Password and left User_Password / PasswordHash in place, so the
        user's old password kept working after the reset.
        Now it clears both, unlocks the account, and ends the user's open
        sessions (tbl_AuthSessions, from 023), so a reset also logs out anyone
        using the old password.
     2. @UserAccounts_Password NULL (or '') now means "leave the password
        alone". Before, the app re-sent the stored temp password on every
        edit to keep it.
     3. The password parameter is NVARCHAR(100). It was NVARCHAR(25), which
        would silently cut a BCrypt hash (60 chars) short.
     4. The login row is found by Ref_ID *and* account type. Before it was
        Ref_ID only, but broker, employer and staff IDs come from separate
        tables and overlap, so editing staff #5 also renamed / re-roled /
        reset the login of broker #5 and employer #5. Employer logins created
        from the Tracker (Role 13, Ref_ID = tracker employer) share the
        employer role, so when more than one row matches, the one whose
        User_Name is this account's current email wins; if that is still
        ambiguous the SP raises an error instead of guessing.

   sp_CreateUserAccounts
     - Password parameter widened to NVARCHAR(100) for the same reason.
     - On error it now rolls back. Before, it could COMMIT the half-done
       work (an account row with no login).

   The web app now BCrypt-hashes the password before calling either SP, and
   login accepts a hashed temp password. Temp passwords already stored in
   plain text keep working (login compares them as before) until they are
   used or replaced.

   Run after 023. Deploy together with the matching web app build: an older
   build sends plain-text passwords, which this script would store as-is.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_CreateUserAccounts
    @UserAccounts_FName    NVARCHAR(100),
    @UserAccounts_LName    NVARCHAR(100),
    @UserAccounts_Addr1    NVARCHAR(100) = NULL,
    @UserAccounts_Addr2    NVARCHAR(50)  = NULL,
    @UserAccounts_City     NVARCHAR(50)  = NULL,
    @UserAccounts_State    BIGINT        = NULL,
    @UserAccounts_Zip      NVARCHAR(10)  = NULL,
    @UserAccounts_Phone    NVARCHAR(25)  = NULL,
    @UserAccounts_Email    NVARCHAR(100),
    @UserAccounts_Status   BIGINT        = NULL,
    @UserAccounts_Role     BIGINT        = NULL,
    @UserAccounts_Password NVARCHAR(100) = NULL,   -- BCrypt hash from the app
    @ProfilePicture        NVARCHAR(MAX) = NULL,
    @IsMFA                 INT           = NULL,
    @createdby             BIGINT        = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserAccounts_ID BIGINT;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@UserAccounts_Role = 10)
        BEGIN
            INSERT INTO dbo.BrokerAccounts (
                Broker_FName, Broker_LName, Broker_Addr1, Broker_Addr2,
                Broker_City, Broker_State, Broker_Zip, Broker_Phone,
                Broker_Email, Broker_Status, createdon, createdby)
            VALUES (
                @UserAccounts_FName, @UserAccounts_LName, @UserAccounts_Addr1, @UserAccounts_Addr2,
                @UserAccounts_City, @UserAccounts_State, @UserAccounts_Zip, @UserAccounts_Phone,
                @UserAccounts_Email, @UserAccounts_Status, GETDATE(), @createdby);
        END
        ELSE IF (@UserAccounts_Role = 13)
        BEGIN
            INSERT INTO dbo.EmployerAccounts (
                Employer_FName, Employer_LName, Employer_Addr1, Employer_Addr2,
                Employer_City, Employer_State, Employer_Zip, Employer_Phone,
                Employer_Email, Employer_Status, createdon, createdby)
            VALUES (
                @UserAccounts_FName, @UserAccounts_LName, @UserAccounts_Addr1, @UserAccounts_Addr2,
                @UserAccounts_City, @UserAccounts_State, @UserAccounts_Zip, @UserAccounts_Phone,
                @UserAccounts_Email, @UserAccounts_Status, GETDATE(), @createdby);
        END
        ELSE
        BEGIN
            INSERT INTO dbo.StaffAccounts (
                Staff_FName, Staff_LName, Staff_Addr1, Staff_Addr2,
                Staff_City, Staff_State, Staff_Zip, Staff_Phone,
                Staff_Email, Staff_Status, createdon, createdby)
            VALUES (
                @UserAccounts_FName, @UserAccounts_LName, @UserAccounts_Addr1, @UserAccounts_Addr2,
                @UserAccounts_City, @UserAccounts_State, @UserAccounts_Zip, @UserAccounts_Phone,
                @UserAccounts_Email, @UserAccounts_Status, GETDATE(), @createdby);
        END

        SET @UserAccounts_ID = SCOPE_IDENTITY();

        INSERT INTO dbo.tbl_User ([User_Name], Temp_Password, Ref_ID, Role_ID, Profile_Picture, isMFA)
        VALUES (@UserAccounts_Email, @UserAccounts_Password, @UserAccounts_ID, @UserAccounts_Role, @ProfilePicture, @IsMFA);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        INSERT INTO dbo.DB_Errors
        VALUES (SUSER_SNAME(), ERROR_NUMBER(), ERROR_STATE(), ERROR_SEVERITY(),
                ERROR_LINE(), ERROR_PROCEDURE(), ERROR_MESSAGE(), GETDATE());

        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateUserAccounts
    @UserAccounts_ID       BIGINT,
    @UserAccounts_FName    NVARCHAR(100),
    @UserAccounts_LName    NVARCHAR(100),
    @UserAccounts_Addr1    NVARCHAR(100) = NULL,
    @UserAccounts_Addr2    NVARCHAR(50)  = NULL,
    @UserAccounts_City     NVARCHAR(50)  = NULL,
    @UserAccounts_State    BIGINT        = NULL,
    @UserAccounts_Zip      VARCHAR(10)   = NULL,
    @UserAccounts_Phone    VARCHAR(25)   = NULL,
    @UserAccounts_Email    VARCHAR(100),
    @UserAccounts_Status   BIGINT        = NULL,
    @UserAccounts_Role     BIGINT        = NULL,
    @UserAccounts_Password NVARCHAR(100) = NULL,   -- BCrypt hash from the app; NULL = keep the current password
    @ProfilePicture        NVARCHAR(MAX) = NULL,
    @IsMFA                 INT           = NULL,
    @modifiedby            BIGINT        = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @OldEmail NVARCHAR(100),
            @UserId   BIGINT,
            @Matches  INT,
            @IsBroker   BIT = CASE WHEN @UserAccounts_Role = 10 THEN 1 ELSE 0 END,
            @IsEmployer BIT = CASE WHEN @UserAccounts_Role = 13 THEN 1 ELSE 0 END;

    BEGIN TRY
        BEGIN TRANSACTION;

        -----------------------------------------------------------------
        -- 1. Update the account row (email captured first to find the login)
        -----------------------------------------------------------------
        IF (@IsBroker = 1)
        BEGIN
            SELECT @OldEmail = Broker_Email FROM dbo.BrokerAccounts WHERE Broker_ID = @UserAccounts_ID;

            UPDATE dbo.BrokerAccounts
            SET    Broker_FName  = @UserAccounts_FName,
                   Broker_LName  = @UserAccounts_LName,
                   Broker_Addr1  = @UserAccounts_Addr1,
                   Broker_Addr2  = @UserAccounts_Addr2,
                   Broker_City   = @UserAccounts_City,
                   Broker_State  = @UserAccounts_State,
                   Broker_Zip    = @UserAccounts_Zip,
                   Broker_Phone  = @UserAccounts_Phone,
                   Broker_Email  = @UserAccounts_Email,
                   Broker_Status = @UserAccounts_Status,
                   modifiedon    = GETDATE(),
                   modifiedby    = @modifiedby
            WHERE  Broker_ID = @UserAccounts_ID;
        END
        ELSE IF (@IsEmployer = 1)
        BEGIN
            SELECT @OldEmail = Employer_Email FROM dbo.EmployerAccounts WHERE Employer_ID = @UserAccounts_ID;

            UPDATE dbo.EmployerAccounts
            SET    Employer_FName  = @UserAccounts_FName,
                   Employer_LName  = @UserAccounts_LName,
                   Employer_Addr1  = @UserAccounts_Addr1,
                   Employer_Addr2  = @UserAccounts_Addr2,
                   Employer_City   = @UserAccounts_City,
                   Employer_State  = @UserAccounts_State,
                   Employer_Zip    = @UserAccounts_Zip,
                   Employer_Phone  = @UserAccounts_Phone,
                   Employer_Email  = @UserAccounts_Email,
                   Employer_Status = @UserAccounts_Status,
                   modifiedon      = GETDATE(),
                   modifiedby      = @modifiedby
            WHERE  Employer_ID = @UserAccounts_ID;
        END
        ELSE
        BEGIN
            SELECT @OldEmail = Staff_Email FROM dbo.StaffAccounts WHERE Staff_ID = @UserAccounts_ID;

            UPDATE dbo.StaffAccounts
            SET    Staff_FName  = @UserAccounts_FName,
                   Staff_LName  = @UserAccounts_LName,
                   Staff_Addr1  = @UserAccounts_Addr1,
                   Staff_Addr2  = @UserAccounts_Addr2,
                   Staff_City   = @UserAccounts_City,
                   Staff_State  = @UserAccounts_State,
                   Staff_Zip    = @UserAccounts_Zip,
                   Staff_Phone  = @UserAccounts_Phone,
                   Staff_Email  = @UserAccounts_Email,
                   Staff_Status = @UserAccounts_Status,
                   modifiedon   = GETDATE(),
                   modifiedby   = @modifiedby
            WHERE  Staff_ID = @UserAccounts_ID;
        END

        -----------------------------------------------------------------
        -- 2. Find this account's login row (same Ref_ID + same account type)
        -----------------------------------------------------------------
        SELECT @Matches = COUNT(*), @UserId = MIN(u.User_ID)
        FROM   dbo.tbl_User u
        WHERE  u.Ref_ID = @UserAccounts_ID
          AND  (   (@IsBroker = 1   AND u.Role_ID = 10)
                OR (@IsEmployer = 1 AND u.Role_ID = 13)
                OR (@IsBroker = 0 AND @IsEmployer = 0 AND ISNULL(u.Role_ID, 0) NOT IN (10, 13)));

        IF (@Matches > 1)
        BEGIN
            SELECT @Matches = COUNT(*), @UserId = MIN(u.User_ID)
            FROM   dbo.tbl_User u
            WHERE  u.Ref_ID = @UserAccounts_ID
              AND  u.[User_Name] = @OldEmail
              AND  (   (@IsBroker = 1   AND u.Role_ID = 10)
                    OR (@IsEmployer = 1 AND u.Role_ID = 13)
                    OR (@IsBroker = 0 AND @IsEmployer = 0 AND ISNULL(u.Role_ID, 0) NOT IN (10, 13)));

            IF (@Matches <> 1)
                THROW 50027, 'sp_UpdateUserAccounts: more than one login matches this account and none is uniquely tied to its email. Login not updated.', 1;
        END

        -----------------------------------------------------------------
        -- 3. Update the login (no login row = nothing to update, as before)
        -----------------------------------------------------------------
        IF (@UserId IS NOT NULL)
        BEGIN
            UPDATE dbo.tbl_User
            SET    [User_Name]     = @UserAccounts_Email,
                   Role_ID         = @UserAccounts_Role,
                   isMFA           = @IsMFA,
                   Profile_Picture = COALESCE(@ProfilePicture, Profile_Picture),
                   ModifiedOn      = GETDATE()
            WHERE  User_ID = @UserId;

            -- Admin reset: the new temp password replaces the old password,
            -- the account is unlocked, and open sessions are ended.
            IF (NULLIF(@UserAccounts_Password, N'') IS NOT NULL)
            BEGIN
                UPDATE dbo.tbl_User
                SET    Temp_Password     = @UserAccounts_Password,
                       User_Password     = NULL,
                       PasswordHash      = NULL,
                       AccessFailedCount = 0,
                       LockoutEnd        = NULL
                WHERE  User_ID = @UserId;

                IF OBJECT_ID(N'dbo.tbl_AuthSessions', N'U') IS NOT NULL
                    UPDATE dbo.tbl_AuthSessions
                    SET    RevokedAt = SYSUTCDATETIME()
                    WHERE  Subject = CONCAT(N'user:', @UserId)
                      AND  RevokedAt IS NULL;
            END
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        INSERT INTO dbo.DB_Errors
        VALUES (SUSER_SNAME(), ERROR_NUMBER(), ERROR_STATE(), ERROR_SEVERITY(),
                ERROR_LINE(), ERROR_PROCEDURE(), ERROR_MESSAGE(), GETDATE());

        THROW;
    END CATCH
END
GO

/* ############################################################################
   028_block_deleted_user_login.sql
   ############################################################################ */
PRINT '=== 028_block_deleted_user_login.sql';
GO
/* =============================================================================
   028_block_deleted_user_login.sql

   Deleted, inactive and terminated users can no longer log in, and any session
   they already have ends on their next request.

   Before this, login only refused tbl_User.Flag = 0, which nothing in User
   Accounts ever sets:
     - Delete (sp_DeleteUserAccounts) sets tbl_User.Status = 4 and the account's
       *_Status = 4, so a deleted user could still log in.
     - Setting a user to Inactive (2) or Terminated (3) only changes the
       account row (Staff/Employer/Broker_Status), so they could too.

   dbo.fn_UserLoginAllowed holds the rule in one place:
     - Flag = 0                                   -> refused (as before)
     - account row status Inactive / Terminated / Deleted (2, 3, 4) -> refused
     - no account row and tbl_User.Status = '4'   -> refused
   The account row is the Staff/Employer/Broker row with the login's Ref_ID,
   the same account type, and the login's email. When there is one, its status
   decides, so an admin setting a deleted user back to Active lets them in again.
   Logins with no such row (Tracker employer logins, older rows) keep working
   unless Flag = 0 or Status = '4'.

   Used by:
     - sp_Login_access: refused users get the same "Invalid login attempt." as
       an unknown user.
     - sp_AuthSession_Validate (from 023): runs on every request, so an open
       session of a user who is deleted or deactivated is ended at once.

   sp_Login_access also gets its name lookup fixed: it joined the account tables
   by Ref_ID only, so an employer login sharing an ID with a staff member showed
   the staff member's name in the header.

   Run after 023. No web app change needed. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER FUNCTION dbo.fn_UserLoginAllowed (@UserId BIGINT)
RETURNS TABLE
AS
RETURN
    SELECT CAST(CASE
               WHEN ISNULL(u.Flag, 1) = 0                            THEN 0
               WHEN a.HasAccount = 1 AND a.AccountStatus IN (2, 3, 4) THEN 0
               WHEN a.HasAccount = 0 AND LTRIM(RTRIM(ISNULL(u.[Status], ''))) = '4' THEN 0
               ELSE 1
           END AS BIT) AS IsAllowed
    FROM dbo.tbl_User u
    OUTER APPLY (
        SELECT COUNT(*) AS HasAccount, MAX(x.AccountStatus) AS AccountStatus
        FROM (
            SELECT b.Broker_Status AS AccountStatus
            FROM   dbo.BrokerAccounts b
            WHERE  u.Role_ID = 10 AND b.Broker_ID = u.Ref_ID AND b.Broker_Email = u.[User_Name]
            UNION ALL
            SELECT e.Employer_Status
            FROM   dbo.EmployerAccounts e
            WHERE  u.Role_ID = 13 AND e.Employer_ID = u.Ref_ID AND e.Employer_Email = u.[User_Name]
            UNION ALL
            SELECT s.Staff_Status
            FROM   dbo.StaffAccounts s
            WHERE  ISNULL(u.Role_ID, 0) NOT IN (10, 13) AND s.Staff_ID = u.Ref_ID AND s.Staff_Email = u.[User_Name]
        ) x
    ) a
    WHERE u.User_ID = @UserId;
GO

CREATE OR ALTER PROCEDURE dbo.sp_Login_access
(
    @uname NVARCHAR(100)
)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserId BIGINT;

    -- Only a login that is allowed to sign in (fn_UserLoginAllowed) is returned;
    -- a refused one looks exactly like an unknown user to the caller.
    SELECT @UserId = u.User_ID
    FROM   dbo.tbl_User u
    CROSS APPLY dbo.fn_UserLoginAllowed(u.User_ID) ok
    WHERE  u.[User_Name] = @uname
      AND  ok.IsAllowed = 1;

    -- =========================================================
    -- 1st ResultSet: User Credentials & Profile Data
    -- =========================================================
    SELECT
        u.User_ID,
        u.[User_Name] AS [User_Name],
        u.User_Password,
        u.Temp_Password,
        u.Role_ID,
        u.Flag,
        u.Ref_ID,
        u.Profile_Picture,
        u.isMFA,
        u.User_LandingPage,
        u.CreatedOn,
        u.ModifiedOn,
        u.PasswordHash,
        u.LockoutEnd,
        u.AccessFailedCount,
        u.LockoutEnabled,
        r.RoleName AS Role_Name,
        LTRIM(RTRIM(
            COALESCE(s.Staff_FName, e.Employer_FName, b.Broker_FName, '') + ' ' +
            COALESCE(s.Staff_LName, e.Employer_LName, b.Broker_LName, '')
        )) AS [Profile_Name]
    FROM dbo.tbl_User u
    LEFT JOIN dbo.tbl_Roles r ON r.ID = u.Role_ID
    -- Each login is joined only to the account table for its own type.
    LEFT JOIN dbo.BrokerAccounts   b ON u.Role_ID = 10 AND b.Broker_ID = u.Ref_ID
    LEFT JOIN dbo.EmployerAccounts e ON u.Role_ID = 13 AND e.Employer_ID = u.Ref_ID
    LEFT JOIN dbo.StaffAccounts    s ON ISNULL(u.Role_ID, 0) NOT IN (10, 13) AND s.Staff_ID = u.Ref_ID
    WHERE u.User_ID = @UserId;

    -- =========================================================
    -- 2nd ResultSet: Menus and Action Permissions
    -- =========================================================
    SELECT
        m.Id AS MenuId,
        m.MenuName,
        m.Controller,
        at.ActionName AS Action,
        'Enabled' AS PermissionType
    FROM dbo.tbl_User u
    JOIN dbo.tbl_AccessPermissions ap ON ap.RoleId = u.Role_ID AND ap.IsEnabled = 1
    JOIN dbo.tbl_Menus m ON m.Id = ap.MenuId AND m.IsActive = 1 AND m.Flag = 0
    JOIN dbo.tbl_ActionTypes at ON at.Id = ap.ActionID
    WHERE u.User_ID = @UserId;
END
GO

/* Same as 023, plus: a staff session ("user:<User_ID>") whose user is no longer
   allowed to sign in is revoked and reported invalid. */
CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_Validate
    @SessionId             UNIQUEIDENTIFIER,
    @IdleMinutes           INT,
    @Touch                 BIT,
    @TouchIntervalSeconds  INT = 60
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now     DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Last    DATETIME2(3);
    DECLARE @Subject NVARCHAR(100);

    SELECT @Last = LastActivityAt, @Subject = Subject
    FROM   dbo.tbl_AuthSessions
    WHERE  SessionId = @SessionId
      AND  RevokedAt IS NULL;

    IF @Last IS NULL
    BEGIN
        SELECT CAST(0 AS BIT) AS IsValid;
        RETURN;
    END

    IF @Last < DATEADD(MINUTE, -@IdleMinutes, @Now)
       OR (@Subject LIKE N'user:%'
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.fn_UserLoginAllowed(TRY_CAST(SUBSTRING(@Subject, 6, 30) AS BIGINT))
                           WHERE  IsAllowed = 1))
    BEGIN
        -- Close it for good, so a later request cannot revive it.
        UPDATE dbo.tbl_AuthSessions
        SET    RevokedAt = @Now
        WHERE  SessionId = @SessionId
          AND  RevokedAt IS NULL;

        SELECT CAST(0 AS BIT) AS IsValid;
        RETURN;
    END

    IF @Touch = 1 AND @Last < DATEADD(SECOND, -@TouchIntervalSeconds, @Now)
    BEGIN
        UPDATE dbo.tbl_AuthSessions
        SET    LastActivityAt = @Now
        WHERE  SessionId = @SessionId
          AND  RevokedAt IS NULL;
    END

    SELECT CAST(1 AS BIT) AS IsValid;
END
GO

/* ############################################################################
   029_dashboard_scope_and_account_managers.sql
   ############################################################################ */
PRINT '=== 029_dashboard_scope_and_account_managers.sql';
GO
/* =============================================================================
   029_dashboard_scope_and_account_managers.sql

   One rule for "which employers does this user's dashboard cover", and the
   account-manager list the AM Supervisor dashboard needs.

   dbo.fn_DashboardEmployerScope(@UserId, @Year)
     Employers (id, main or affiliate) the dashboard counts for a user and plan year.
       - Sees every employer: SuperAdmin, Admin, ACA Director, DA Supervisor,
         Account Manager Supervisor, StaffAC, StaffAU.
         (Supervisors and the two staff roles were decided 2026-09-25: they see all
         employers; the web app's permitted-employer check was widened to match.)
       - Otherwise the user's own assignments:
           Account Manager  tbl_Portfolio_Assignment.AM_UserID (synced from the
                            Tracker) or EmployerAccountManagers (by EIN)
           Data Analyst     tbl_Operational_Assignment.DA_UserID
           Broker           tbl_Employer_Broker_Assignment.Broker_UserID
       - Only the selected plan year's employer rows. Employer rows are stored per
         filing year, so without this the totals counted every year's copy of each
         employer (local data: 22 "all EINs" for a year that has 9).

   sp_GetPortfolioDashboardCounts / sp_GetPortfolioProcessSteps
     Now use the function. Before, only SuperAdmin/Admin/ACA Director saw all
     employers, and account managers were matched only through
     EmployerAccountManagers, which the Tracker sync does not fill, so an account
     manager's dashboard showed no clients. The counts are otherwise unchanged.

   sp_GetDashboardAccountManagers
     RS1: employer -> account manager, for employers in the caller's scope.
     RS2: clients per account manager (active AM logins only), for team workload.

   Run after 028 (uses fn_UserLoginAllowed). Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER FUNCTION dbo.fn_DashboardEmployerScope (@UserId INT, @Year INT)
RETURNS TABLE
AS
RETURN
    SELECT e.id AS EmployerId,
           CAST(CASE WHEN ISNULL(e.companyId, 0) = 0 THEN 1 ELSE 0 END AS BIT) AS IsMain
    FROM   dbo.Employer e
    OUTER APPLY (
        SELECT TOP 1 r.RoleName
        FROM   dbo.tbl_User u
        JOIN   dbo.tbl_Roles r ON r.ID = u.Role_ID
        WHERE  u.User_ID = @UserId
    ) who
    WHERE  e.IsDeleted = 0
      AND (e.filingYear IS NULL OR e.filingYear = @Year)
      AND (
            who.RoleName IN ('SuperAdmin', 'Admin', 'ACA Director',
                             'DA Supervisor', 'Account Manager Supervisor',
                             'StaffAC', 'StaffAU')
         OR EXISTS (SELECT 1 FROM dbo.tbl_Portfolio_Assignment pa
                    WHERE pa.EmployerID = e.id
                      AND pa.AM_UserID = @UserId
                      AND pa.IsActive = 1)
         OR EXISTS (SELECT 1 FROM dbo.EmployerAccountManagers am
                    WHERE REPLACE(am.EmployerEIN, '-', '') = REPLACE(ISNULL(e.taxid, ''), '-', '')
                      AND am.AccountManagerUserId = @UserId
                      AND am.IsActive = 1)
         OR EXISTS (SELECT 1 FROM dbo.tbl_Operational_Assignment oa
                    WHERE oa.EmployerID = e.id
                      AND oa.DA_UserID = @UserId
                      AND oa.IsActive = 1)
         OR EXISTS (SELECT 1 FROM dbo.tbl_Employer_Broker_Assignment ba
                    WHERE ba.EmployerID = e.id
                      AND ba.Broker_UserID = @UserId
                      AND ba.IsActive = 1
                      AND ba.UnassignedDate IS NULL)
          );
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetPortfolioDashboardCounts
    @UserId INT,
    @Year   INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Scope TABLE (EmployerId INT PRIMARY KEY, IsMain BIT);
    INSERT INTO @Scope (EmployerId, IsMain)
    SELECT EmployerId, IsMain FROM dbo.fn_DashboardEmployerScope(@UserId, @Year);

    SELECT
        TotalClientsAllEins  = (SELECT COUNT(*) FROM @Scope),
        TotalClientsMainEins = (SELECT COUNT(*) FROM @Scope WHERE IsMain = 1),

        ClientsFormsMailed   = (
            SELECT COUNT(DISTINCT fs.EmployerId)
            FROM   dbo.EmployerFilingStatus fs
            JOIN   @Scope s ON s.EmployerId = fs.EmployerId
            WHERE  fs.TaxYear = @Year
              AND  fs.FilingStatusId >= 3   -- >>> STILL UNCONFIRMED (carried over) <<<
        ),

        ClientsEfiled        = (
            SELECT COUNT(DISTINCT l.EmployerId)
            FROM   dbo.IrsSubmissionLog l
            JOIN   @Scope s ON s.EmployerId = l.EmployerId
            WHERE  l.TaxYear = @Year
              AND  l.TransmittedDate IS NOT NULL
        ),

        ClientsWithPenalties = (
            SELECT COUNT(DISTINCT p.EmployerId)
            FROM   dbo.EmployerPenaltyCalculation p
            JOIN   @Scope s ON s.EmployerId = p.EmployerId
            WHERE  p.TaxYear = @Year
              AND (ISNULL(p.TypeA_TotalPenalty, 0) + ISNULL(p.TypeB_TotalPenalty, 0)) > 0
        ),

        TotalPenaltyExposure = (
            SELECT ISNULL(SUM(ISNULL(p.TypeA_TotalPenalty, 0) + ISNULL(p.TypeB_TotalPenalty, 0)), 0)
            FROM   dbo.EmployerPenaltyCalculation p
            JOIN   @Scope s ON s.EmployerId = p.EmployerId
            WHERE  p.TaxYear = @Year
        ),

        PastDueClients       = (
            SELECT COUNT(DISTINCT es.EmployerId)
            FROM   dbo.Tracker_EmployerService es
            JOIN   @Scope s ON s.EmployerId = es.EmployerId
            WHERE  es.PlanYear = @Year
              AND  ISNULL(es.IsDeleted, 0) = 0
              AND  TRY_CAST(es.NextFollowUp1095 AS DATE) < CAST(GETDATE() AS DATE)
        ),

        OldestPastDueDays    = (
            SELECT ISNULL(MAX(DATEDIFF(DAY, TRY_CAST(es.NextFollowUp1095 AS DATE), GETDATE())), 0)
            FROM   dbo.Tracker_EmployerService es
            JOIN   @Scope s ON s.EmployerId = es.EmployerId
            WHERE  es.PlanYear = @Year
              AND  ISNULL(es.IsDeleted, 0) = 0
              AND  TRY_CAST(es.NextFollowUp1095 AS DATE) < CAST(GETDATE() AS DATE)
        );
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetPortfolioProcessSteps
    @UserId INT,
    @Year   INT,
    @Top    INT = 25
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
        EmployerId   = e.id,
        EmployerName = e.name,
        Ein          = e.taxid,
        -- Client-facing label first; the internal step name is the fallback.
        ProcessStep  = COALESCE(NULLIF(LTRIM(RTRIM(tp.WebName)), ''), es.ProcessStep1095),
        DueDate      = TRY_CAST(es.NextFollowUp1095 AS DATE),
        DaysOverdue  = DATEDIFF(DAY, TRY_CAST(es.NextFollowUp1095 AS DATE), CAST(GETDATE() AS DATE))
    FROM   dbo.Tracker_EmployerService es
    JOIN   dbo.fn_DashboardEmployerScope(@UserId, @Year) sc ON sc.EmployerId = es.EmployerId
    JOIN   dbo.Employer e ON e.id = es.EmployerId
    LEFT   JOIN dbo.Tracker_Process tp ON tp.ProcessName = es.ProcessStep1095
    WHERE  es.PlanYear = @Year
      AND  ISNULL(es.IsDeleted, 0) = 0
    ORDER BY
        CASE WHEN TRY_CAST(es.NextFollowUp1095 AS DATE) IS NULL THEN 1 ELSE 0 END,
        DaysOverdue DESC,
        e.name ASC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetDashboardAccountManagers
    @UserId INT,
    @Year   INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Account manager display name: the staff record's name, else the login.
    DECLARE @Managers TABLE (UserId INT PRIMARY KEY, Name NVARCHAR(250));
    INSERT INTO @Managers (UserId, Name)
    SELECT u.User_ID,
           COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(s.Staff_FName, '') + ' ' + ISNULL(s.Staff_LName, ''))), ''),
                    u.[User_Name])
    FROM   dbo.tbl_User u
    JOIN   dbo.tbl_Roles r ON r.ID = u.Role_ID
    CROSS  APPLY dbo.fn_UserLoginAllowed(u.User_ID) ok
    LEFT   JOIN dbo.StaffAccounts s ON s.Staff_ID = u.Ref_ID AND s.Staff_Email = u.[User_Name]
    WHERE  r.RoleName = 'Account Manager'
      AND  ok.IsAllowed = 1;

    DECLARE @Links TABLE (EmployerId INT, AccountManagerUserId INT, PRIMARY KEY (EmployerId, AccountManagerUserId));
    INSERT INTO @Links (EmployerId, AccountManagerUserId)
    SELECT DISTINCT sc.EmployerId, x.AmUserId
    FROM   dbo.fn_DashboardEmployerScope(@UserId, @Year) sc
    JOIN   dbo.Employer e ON e.id = sc.EmployerId
    CROSS  APPLY (
        SELECT pa.AM_UserID AS AmUserId
        FROM   dbo.tbl_Portfolio_Assignment pa
        WHERE  pa.EmployerID = e.id AND pa.IsActive = 1
        UNION
        SELECT am.AccountManagerUserId
        FROM   dbo.EmployerAccountManagers am
        WHERE  REPLACE(am.EmployerEIN, '-', '') = REPLACE(ISNULL(e.taxid, ''), '-', '')
          AND  am.IsActive = 1
    ) x;

    -- RS1: employer -> account manager
    SELECT l.EmployerId,
           l.AccountManagerUserId,
           AccountManagerName = ISNULL(m.Name, u.[User_Name])
    FROM   @Links l
    LEFT   JOIN @Managers m ON m.UserId = l.AccountManagerUserId
    LEFT   JOIN dbo.tbl_User u ON u.User_ID = l.AccountManagerUserId;

    -- RS2: clients per active account manager, including those with none
    SELECT Name     = m.Name,
           FileCount = (SELECT COUNT(*) FROM @Links l WHERE l.AccountManagerUserId = m.UserId)
    FROM   @Managers m
    ORDER  BY FileCount DESC, m.Name;
END
GO

/* ############################################################################
   030_align_role_permissions.sql
   ############################################################################ */
PRINT '=== 030_align_role_permissions.sql';
GO
/* =============================================================================
   030_align_role_permissions.sql

   Grants the menus and actions the client's role sheets list (ACA 360 Coding-Testing
   Tracking Document.xlsx: Director / DA Supervisor / AM Supervisor / DA / AM Role
   Testing) to those five roles. Goes with the web change that makes every
   User Accounts action check its permission (before, any logged-in role could open
   User Accounts and add or edit users).

   ADDITIVE ONLY. Nothing is revoked. Grants a role has beyond its sheet are
   listed by the last query for review. Disabled rows for a granted menu/action
   are re-enabled rather than duplicated. A backup of tbl_AccessPermissions is
   taken on the first run (tbl_AccessPermissions_Backup_030).

   How the sheet is read
     - "View" on a page also grants Index: every page's landing action checks
       Index, so View alone would show the menu and then deny the page.
     - "Broker Dashboard" / "Employer Dashboard" = Employer/Dashboard, which checks
       Employer:Index, so those are folded into the Employers menu.
     - "Generate Reports" = the Reports menu and its four report pages.
     - "Files: Upload" / "Download Hub: Download" also get Index + View to open them.
     - "?" and "N/A" grant nothing. Master Settings and ACA Calculation are "?".
     - User Accounts: the menu sheets give AM Supervisor and Account Manager full
       rights, but their dashboard spec says "User Management (Reset Passwords
       Only)". They get Index + View + Edit; the web app treats Edit without Add as
       password-reset-only. ACA Director gets full rights, as the sheet says.
       (Only Admin/SuperAdmin can touch Admin/SuperAdmin accounts — enforced in code.)
     - Every role gets Dashboard:Index so the Dashboard link shows (DA Supervisor,
       AM Supervisor, StaffAC and StaffAU had no row, so no Dashboard menu item).

   Parent menus
     A child menu only renders when its parent (and grandparent) has a row for the
     role, so each ancestor of a granted menu gets View. The Administration
     container's Controller was 'UserAccounts' (script 015), which turned that
     container row into a UserAccounts:View permission for every role that can see
     System Settings or Master Settings. It becomes 'Administration' (a label, not a
     page) so a container row grants nothing.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.tbl_AccessPermissions_Backup_030', N'U') IS NULL
    SELECT * INTO dbo.tbl_AccessPermissions_Backup_030 FROM dbo.tbl_AccessPermissions;
GO

BEGIN TRANSACTION;

-- The menu ids below are from the ACA360 menu seed. Stop if any of them points to
-- a different page in this database, rather than granting the wrong screen.
DECLARE @Expect TABLE (Id INT PRIMARY KEY, Controller NVARCHAR(100));
INSERT INTO @Expect VALUES
    (2,'Employee'),(3,'Plan'),(5,'Dashboard'),(8,'SelectEmployer'),(9,'Employer'),(15,'Home'),(16,'Home'),
    (17,'FileUpload'),(18,'Role'),(19,'UserAccounts'),(21,'Rules'),(1021,'System'),(1023,'Filing'),
    (1025,'Downloads'),(1026,'Distribution'),(1028,'Tracker'),(1029,'Service'),(1030,'Process'),
    (1031,'Partner'),(1032,'TrackerEmployer'),(1039,'Report'),(1040,'eFileReport'),
    (1041,'MailFulfillmentReport'),(1047,'AMReport'),(1048,'DataAnalystReport');

IF EXISTS (SELECT 1 FROM @Expect e LEFT JOIN dbo.tbl_Menus m ON m.Id = e.Id
           WHERE m.Id IS NULL OR LTRIM(RTRIM(m.Controller)) <> e.Controller)
BEGIN
    SELECT e.Id, e.Controller AS Expected, m.Controller AS Found, m.MenuName
    FROM   @Expect e LEFT JOIN dbo.tbl_Menus m ON m.Id = e.Id
    WHERE  m.Id IS NULL OR LTRIM(RTRIM(m.Controller)) <> e.Controller;
    THROW 50030, 'Menu ids do not match this database. Nothing changed.', 1;
END

-- Administration container: a label, not a page.
UPDATE dbo.tbl_Menus
SET    Controller = 'Administration', DateModified = GETDATE()
WHERE  MenuName = 'Administration' AND ParentMenuId IS NULL AND Controller = 'UserAccounts';

DECLARE @All VARCHAR(100) = 'Index,View,Add,Edit,Delete,Download,Upload';

-- Role, menu, actions (from the role sheets; see header for how they are read)
DECLARE @Spec TABLE (RoleName NVARCHAR(60), MenuId INT, Actions VARCHAR(100));
INSERT INTO @Spec (RoleName, MenuId, Actions) VALUES
    -- ACA Director
    ('ACA Director',    5, 'Index'),
    ('ACA Director',    9, 'Index,View,Edit,Delete,Download,Upload'),
    ('ACA Director',   21, @All),
    ('ACA Director', 1029, 'Index,View'),
    ('ACA Director', 1030, @All),
    ('ACA Director',    8, 'Index,View'),
    ('ACA Director', 1031, @All),
    ('ACA Director', 1032, @All),
    ('ACA Director',    3, @All),
    ('ACA Director',    2, @All),
    ('ACA Director',   17, 'Index,View,Upload'),
    ('ACA Director', 1028, @All),
    ('ACA Director', 1039, @All), ('ACA Director', 1040, @All), ('ACA Director', 1041, @All),
    ('ACA Director', 1047, @All), ('ACA Director', 1048, @All),
    ('ACA Director', 1025, 'Index,View,Download'),
    ('ACA Director', 1023, @All),
    ('ACA Director', 1026, @All),
    ('ACA Director',   15, @All),
    ('ACA Director',   16, @All),
    ('ACA Director',   19, @All),
    ('ACA Director', 1021, @All),
    ('ACA Director',   18, @All),

    -- DA Supervisor
    ('DA Supervisor',    5, 'Index'),
    ('DA Supervisor',    9, 'Index,View'),
    ('DA Supervisor',   21, 'Index,View'),
    ('DA Supervisor', 1030, @All),
    ('DA Supervisor',    8, 'Index,View'),
    ('DA Supervisor', 1031, 'Index,View'),
    ('DA Supervisor', 1032, 'Index,View'),
    ('DA Supervisor',    3, 'Index,View'),
    ('DA Supervisor',    2, 'Index,View,Edit'),
    ('DA Supervisor',   17, 'Index,View,Upload'),
    ('DA Supervisor', 1028, @All),
    ('DA Supervisor', 1039, @All), ('DA Supervisor', 1040, @All), ('DA Supervisor', 1041, @All),
    ('DA Supervisor', 1047, @All), ('DA Supervisor', 1048, @All),
    ('DA Supervisor', 1025, 'Index,View,Download'),
    ('DA Supervisor',   15, 'Index,View'),
    ('DA Supervisor',   16, 'Index,View'),

    -- Account Manager Supervisor
    ('Account Manager Supervisor',    5, 'Index'),
    ('Account Manager Supervisor',    9, 'Index,View,Edit,Download,Upload'),
    ('Account Manager Supervisor', 1030, @All),
    ('Account Manager Supervisor',    8, 'Index,View'),
    ('Account Manager Supervisor', 1031, @All),
    ('Account Manager Supervisor', 1032, @All),
    ('Account Manager Supervisor',    3, @All),
    ('Account Manager Supervisor',    2, @All),
    ('Account Manager Supervisor',   17, 'Index,View,Upload'),
    ('Account Manager Supervisor', 1028, @All),
    ('Account Manager Supervisor', 1039, @All), ('Account Manager Supervisor', 1040, @All),
    ('Account Manager Supervisor', 1041, @All), ('Account Manager Supervisor', 1047, @All),
    ('Account Manager Supervisor', 1048, @All),
    ('Account Manager Supervisor', 1025, 'Index,View,Download'),
    ('Account Manager Supervisor', 1023, @All),
    ('Account Manager Supervisor',   15, 'Index,View'),
    ('Account Manager Supervisor',   16, 'Index,View'),
    ('Account Manager Supervisor',   19, 'Index,View,Edit'),

    -- Data Analyst
    ('Data Analyst',    5, 'Index'),
    ('Data Analyst',    9, 'Index,View'),
    ('Data Analyst',   21, 'Index,View'),
    ('Data Analyst', 1030, @All),
    ('Data Analyst',    8, 'Index,View'),
    ('Data Analyst', 1031, 'Index,View'),
    ('Data Analyst', 1032, 'Index,View'),
    ('Data Analyst',    3, 'Index,View'),
    ('Data Analyst',    2, 'Index,View,Edit'),
    ('Data Analyst',   17, 'Index,View,Upload'),
    ('Data Analyst', 1028, @All),
    ('Data Analyst', 1039, @All), ('Data Analyst', 1040, @All), ('Data Analyst', 1041, @All),
    ('Data Analyst', 1047, @All), ('Data Analyst', 1048, @All),
    ('Data Analyst', 1025, 'Index,View,Download'),
    ('Data Analyst',   15, 'Index,View'),
    ('Data Analyst',   16, 'Index,View'),

    -- Account Manager
    ('Account Manager',    5, 'Index'),
    ('Account Manager',    9, 'Index,View,Edit,Download,Upload'),
    ('Account Manager', 1030, @All),
    ('Account Manager',    8, 'Index,View'),
    ('Account Manager', 1031, @All),
    ('Account Manager', 1032, @All),
    ('Account Manager',    3, @All),
    ('Account Manager',    2, @All),
    ('Account Manager',   17, 'Index,View,Upload'),
    ('Account Manager', 1028, @All),
    ('Account Manager', 1039, @All), ('Account Manager', 1040, @All), ('Account Manager', 1041, @All),
    ('Account Manager', 1047, @All), ('Account Manager', 1048, @All),
    ('Account Manager', 1025, 'Index,View,Download'),
    ('Account Manager', 1023, @All),
    ('Account Manager',   15, 'Index,View'),
    ('Account Manager',   16, 'Index,View'),
    ('Account Manager',   19, 'Index,View,Edit'),

    -- Home page for the roles that had no Dashboard menu row
    ('StaffAC', 5, 'Index'),
    ('StaffAU', 5, 'Index');

-- Expand to (RoleId, MenuId, ActionID)
DECLARE @Want TABLE (RoleId INT, MenuId INT, ActionID INT, PRIMARY KEY (RoleId, MenuId, ActionID));
INSERT INTO @Want (RoleId, MenuId, ActionID)
SELECT DISTINCT r.ID, s.MenuId, at.Id
FROM   @Spec s
JOIN   dbo.tbl_Roles r  ON r.RoleName = s.RoleName
JOIN   dbo.tbl_Menus m  ON m.Id = s.MenuId
CROSS  APPLY STRING_SPLIT(s.Actions, ',') a
JOIN   dbo.tbl_ActionTypes at ON at.ActionName = LTRIM(RTRIM(a.value));

-- Ancestors of every granted menu get View, or the child never renders.
;WITH Up AS (
    SELECT w.RoleId, m.ParentMenuId AS MenuId
    FROM   (SELECT DISTINCT RoleId, MenuId FROM @Want) w
    JOIN   dbo.tbl_Menus m ON m.Id = w.MenuId
    WHERE  m.ParentMenuId IS NOT NULL
    UNION ALL
    SELECT u.RoleId, m.ParentMenuId
    FROM   Up u
    JOIN   dbo.tbl_Menus m ON m.Id = u.MenuId
    WHERE  m.ParentMenuId IS NOT NULL
)
INSERT INTO @Want (RoleId, MenuId, ActionID)
SELECT DISTINCT u.RoleId, u.MenuId, 1
FROM   Up u
WHERE  NOT EXISTS (SELECT 1 FROM @Want w WHERE w.RoleId = u.RoleId AND w.MenuId = u.MenuId AND w.ActionID = 1);

-- Re-enable disabled rows that the sheet grants
UPDATE ap
SET    IsEnabled = 1, Date_Modified = GETDATE(), Modified_by = 'script 030'
FROM   dbo.tbl_AccessPermissions ap
JOIN   @Want w ON w.RoleId = ap.RoleId AND w.MenuId = ap.MenuId AND w.ActionID = ap.ActionID
WHERE  ISNULL(ap.IsEnabled, 0) = 0;
PRINT CONCAT('Re-enabled: ', @@ROWCOUNT);

-- Add the missing ones
INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT w.RoleId, w.MenuId, w.ActionID, 1, GETDATE(), 'script 030'
FROM   @Want w
WHERE  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions ap
                   WHERE ap.RoleId = w.RoleId AND ap.MenuId = w.MenuId AND ap.ActionID = w.ActionID);
PRINT CONCAT('Added: ', @@ROWCOUNT);

COMMIT TRANSACTION;

-- For review: enabled grants these five roles hold that their sheet does not list
-- (nothing was revoked). Parent-menu View rows are expected and not listed.
SELECT r.RoleName, m.MenuName, m.Controller, STRING_AGG(at.ActionName, ',') AS ExtraActions
FROM   dbo.tbl_AccessPermissions ap
JOIN   dbo.tbl_Roles r ON r.ID = ap.RoleId
JOIN   dbo.tbl_Menus m ON m.Id = ap.MenuId
JOIN   dbo.tbl_ActionTypes at ON at.Id = ap.ActionID
WHERE  ap.IsEnabled = 1
  AND  r.RoleName IN ('ACA Director', 'DA Supervisor', 'Account Manager Supervisor', 'Data Analyst', 'Account Manager')
  AND  NOT EXISTS (
        SELECT 1 FROM @Spec s
        CROSS APPLY STRING_SPLIT(s.Actions, ',') a
        WHERE s.RoleName = r.RoleName AND s.MenuId = ap.MenuId AND LTRIM(RTRIM(a.value)) = at.ActionName)
  AND  NOT (ap.ActionID = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_Menus c WHERE c.ParentMenuId = m.Id))
GROUP  BY r.RoleName, m.MenuName, m.Controller
ORDER  BY r.RoleName, m.MenuName;
GO

/* ############################################################################
   031_dashboard_file_counts.sql
   ############################################################################ */
PRINT '=== 031_dashboard_file_counts.sql';
GO
/* =============================================================================
   031_dashboard_file_counts.sql

   File counts per Data Analyst for the DA Supervisor and Data Analyst dashboards,
   and the analyst's priority queue.

   The categories come from dbo.fn_FileStatusBucket (script 024), the same rule the
   File Upload page's badges, cards and filters use, so the dashboard and that page
   always agree:
       Accepted         Imported
       Rejected         rejected (system / AM / DA) or an invalid file
                        (replaced files are not counted)
       Processing       checking, queued, validating, importing, ready to import,
                        on hold
       Critical errors  needs fixes (data errors, column mapping) or import failed
       Waiting          no analyst assigned yet (supervisor view only)

   The old analyst dashboard used ValidationStatusId 2, 3 and 12, which do not
   exist (the codes are 10-99), so its queue never showed a file as needing a fix,
   and sp_GetSupervisorStats returned 0 for every file count.

   sp_GetDashboardFileCounts @Year, @OnlyAssignedTo
     RS1  one row per analyst: active Data Analysts (even with no files) plus anyone
          else files are assigned to, and one row with AnalystUserId NULL for files
          not yet assigned. With @OnlyAssignedTo, just that user's row.
     RS2  with @OnlyAssignedTo: that user's open files, most urgent first
          (import failed, needs fixes, ready to import, in progress, on hold).
   Deleted files are excluded; archived files still count for their plan year.

   Run after 028 (fn_UserLoginAllowed). Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetDashboardFileCounts
    @Year           INT,
    @OnlyAssignedTo INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Files TABLE (
        FileLogId INT PRIMARY KEY, UserId INT NULL, Bucket VARCHAR(20), Ws INT,
        Vs INT, OriginalFileName NVARCHAR(500), EmployerName NVARCHAR(500), UploadedAt DATETIME);

    INSERT INTO @Files
    SELECT f.FileLogId,
           f.AssignedToUserId,
           dbo.fn_FileStatusBucket(f.ValidationStatusId, f.WorkflowStatusId,
                                   CASE WHEN f.AssignedToUserId IS NULL THEN 0 ELSE 1 END),
           f.WorkflowStatusId, f.ValidationStatusId, f.OriginalFileName, f.EmployerName, f.UploadedAt
    FROM   dbo.UploadedFileLog f
    WHERE  ISNULL(f.IsDeleted, 0) = 0
      AND  f.PlanYear = @Year
      AND (@OnlyAssignedTo IS NULL OR f.AssignedToUserId = @OnlyAssignedTo);

    -- Who gets a row
    DECLARE @People TABLE (UserId INT NULL);
    IF @OnlyAssignedTo IS NOT NULL
        INSERT INTO @People VALUES (@OnlyAssignedTo);
    ELSE
    BEGIN
        INSERT INTO @People (UserId)
        SELECT u.User_ID
        FROM   dbo.tbl_User u
        JOIN   dbo.tbl_Roles r ON r.ID = u.Role_ID
        CROSS  APPLY dbo.fn_UserLoginAllowed(u.User_ID) ok
        WHERE  r.RoleName = 'Data Analyst' AND ok.IsAllowed = 1
        UNION
        SELECT DISTINCT UserId FROM @Files WHERE UserId IS NOT NULL;

        IF EXISTS (SELECT 1 FROM @Files WHERE UserId IS NULL)
            INSERT INTO @People VALUES (NULL);
    END

    -- RS1: counts per analyst
    SELECT AnalystUserId  = p.UserId,
           AnalystName    = CASE WHEN p.UserId IS NULL THEN 'Not assigned'
                                 ELSE COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(s.Staff_FName, '') + ' ' + ISNULL(s.Staff_LName, ''))), ''),
                                               u.[User_Name], CONCAT('User ', p.UserId)) END,
           Accepted       = SUM(CASE WHEN f.Bucket = 'Imported' THEN 1 ELSE 0 END),
           Rejected       = SUM(CASE WHEN f.Bucket = 'Closed' AND f.Ws <> 299 THEN 1 ELSE 0 END),
           Processing     = SUM(CASE WHEN f.Bucket IN ('Working', 'ReadyToImport', 'OnHold') THEN 1 ELSE 0 END),
           CriticalErrors = SUM(CASE WHEN f.Bucket IN ('NeedsFixes', 'ImportFailed') THEN 1 ELSE 0 END),
           Waiting        = SUM(CASE WHEN f.Bucket = 'NeedsAssignment' THEN 1 ELSE 0 END)
    FROM   @People p
    LEFT   JOIN @Files f ON (f.UserId = p.UserId) OR (f.UserId IS NULL AND p.UserId IS NULL)
    LEFT   JOIN dbo.tbl_User u ON u.User_ID = p.UserId
    LEFT   JOIN dbo.StaffAccounts s ON s.Staff_ID = u.Ref_ID AND s.Staff_Email = u.[User_Name]
    GROUP  BY p.UserId, s.Staff_FName, s.Staff_LName, u.[User_Name]
    ORDER  BY CASE WHEN p.UserId IS NULL THEN 1 ELSE 0 END, AnalystName;

    -- RS2: the analyst's open files, most urgent first
    SELECT TOP (50) FileLogId, OriginalFileName, EmployerName, UploadedAt,
           ValidationStatusId = Vs, WorkflowStatusId = Ws
    FROM   @Files
    WHERE  @OnlyAssignedTo IS NOT NULL
      AND  Bucket IN ('ImportFailed', 'NeedsFixes', 'ReadyToImport', 'Working', 'OnHold')
    ORDER  BY CASE Bucket WHEN 'ImportFailed' THEN 0 WHEN 'NeedsFixes' THEN 1 WHEN 'ReadyToImport' THEN 2
                          WHEN 'Working' THEN 3 ELSE 4 END,
              UploadedAt;
END
GO

/* ############################################################################
   032_dashboard_system_stats.sql
   ############################################################################ */
PRINT '=== 032_dashboard_system_stats.sql';
GO
/* =============================================================================
   032_dashboard_system_stats.sql

   System figures for the Admin / SuperAdmin dashboard.

   The old dashboard read sp_GetAdvancedSuperAdminStats, which reports 0 users
   (35 active locally), counts deleted admins, and adds up every year's copy of each
   employer. The old page also showed hard-coded values ("All Systems Operational",
   "Data Completeness 78%"). This procedure returns only figures it can stand behind:

       ActiveUsers       logins allowed to sign in (dbo.fn_UserLoginAllowed, 028)
       ActiveAdmins      of those, SuperAdmin / Admin
       LockedOutUsers    active logins locked after failed sign-ins (LockoutEnd ahead)
       PendingPassword   active logins that still only have a temporary password
       SignedInNow       distinct users with a session active in the last 15 minutes
                         (tbl_AuthSessions, 023)
       FilesThisMonth    uploads this calendar month (not deleted)
       FilesLastMonth    uploads last calendar month

   sp_GetAdvancedSuperAdminStats is left in place, unused by the dashboard.

   Run after 023 and 028. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetDashboardSystemStats
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @MonthStart DATETIME = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
    DECLARE @LastMonthStart DATETIME = DATEADD(MONTH, -1, @MonthStart);

    DECLARE @Active TABLE (UserId BIGINT PRIMARY KEY, RoleName NVARCHAR(100), LockoutEnd DATETIMEOFFSET, HasOwnPassword BIT, HasTemp BIT);
    INSERT INTO @Active
    SELECT u.User_ID, r.RoleName, u.LockoutEnd,
           CASE WHEN u.User_Password LIKE '$2%' THEN 1 ELSE 0 END,
           CASE WHEN u.Temp_Password IS NOT NULL THEN 1 ELSE 0 END
    FROM   dbo.tbl_User u
    LEFT   JOIN dbo.tbl_Roles r ON r.ID = u.Role_ID
    CROSS  APPLY dbo.fn_UserLoginAllowed(u.User_ID) ok
    WHERE  ok.IsAllowed = 1;

    SELECT
        ActiveUsers     = (SELECT COUNT(*) FROM @Active),
        ActiveAdmins    = (SELECT COUNT(*) FROM @Active WHERE RoleName IN ('SuperAdmin', 'Admin')),
        LockedOutUsers  = (SELECT COUNT(*) FROM @Active WHERE LockoutEnd > SYSDATETIMEOFFSET()),
        PendingPassword = (SELECT COUNT(*) FROM @Active WHERE HasOwnPassword = 0 AND HasTemp = 1),
        SignedInNow     = (SELECT COUNT(DISTINCT s.Subject)
                           FROM   dbo.tbl_AuthSessions s
                           WHERE  s.RevokedAt IS NULL
                             AND  s.Subject LIKE N'user:%'
                             AND  s.LastActivityAt > DATEADD(MINUTE, -15, SYSUTCDATETIME())),
        FilesThisMonth  = (SELECT COUNT(*) FROM dbo.UploadedFileLog
                           WHERE ISNULL(IsDeleted, 0) = 0 AND UploadedAt >= @MonthStart),
        FilesLastMonth  = (SELECT COUNT(*) FROM dbo.UploadedFileLog
                           WHERE ISNULL(IsDeleted, 0) = 0 AND UploadedAt >= @LastMonthStart AND UploadedAt < @MonthStart);
END
GO

/* ############################################################################
   033_drop_unused_procedures.sql
   ############################################################################ */
PRINT '=== 033_drop_unused_procedures.sql';
GO
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

/* ############################################################################
   034_archive_unused_tables.sql
   ############################################################################ */
PRINT '=== 034_archive_unused_tables.sql';
GO
/* =============================================================================
   034_archive_unused_tables.sql

   Moves 22 tables and one procedure that nothing uses into a new [archive]
   schema. Clean-up step 6 of the unused-code inventory.

   Moved, not dropped: ALTER SCHEMA ... TRANSFER keeps each table's rows, keys,
   indexes and constraints exactly as they are; the app simply stops seeing them
   under dbo. Once production has run without them, a later script can drop the
   [archive] schema.

   How "unused" was decided (September 2026, against master + db_ACA360)
     - No C#, Razor or JavaScript file names the table, and no procedure / function /
       view / trigger that is used names it.
     - No table name is built at run time in the code.
     - None of the kept tables has a foreign key to them (checked below).

   Kept on purpose
     - HangFire.AggregatedCounter / JobParameter / JobQueue (the Hangfire library's).
     - tbl_AccessPermissions_Backup_030, tbl_DroppedProcedures_Backup_033 (rollback
       copies from scripts 030 and 033).
     - tbl_Medtracker_AcctManager, tbl_Medtracker_Bricktbl_Medtracker_Broker: the live
       tbl_Medtracker_Employer has foreign keys to them (sp_AddEmployerCommon inserts
       into it).

   Safety
     1. Stops before changing anything if a table that is NOT being moved has a
        foreign key to one that is, or if a procedure / function / view / trigger
        that is not being moved still names one. sp_ResetOperationalData is exempt:
        it only lists table names in its keep-list and reports names that no longer
        exist.
     2. Moves everything inside one transaction. Objects already in [archive] are
        skipped, so it is safe to re-run.

   To move one back:
       ALTER SCHEMA dbo TRANSFER archive.[TableName];
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'archive') IS NULL
    EXEC (N'CREATE SCHEMA archive AUTHORIZATION dbo;');
GO

DECLARE @Move TABLE (ObjName SYSNAME PRIMARY KEY, Grp VARCHAR(40) NOT NULL);
INSERT INTO @Move (ObjName, Grp) VALUES
    -- Old Medtracker tables (the parents tbl_Medtracker_AcctManager and
    -- tbl_Medtracker_Bricktbl_Medtracker_Broker stay; see header)
    (N'tbl_Medtracker_Account_Manager_History', 'Medtracker'),
    (N'tbl_Medtracker_Brokertbl_Medtracker_Contact', 'Medtracker'),
    (N'tbl_Medtracker_Data_Analyst_History', 'Medtracker'),
    (N'tbl_Medtracker_DataAnalyst', 'Medtracker'),
    (N'tbl_Medtracker_Firm', 'Medtracker'),
    (N'tbl_Medtracker_Location', 'Medtracker'),
    (N'tbl_Medtracker_ImplementationCategory', 'Medtracker'),
    (N'tbl_Medtracker_Permission', 'Medtracker'),

    -- Duplicates of, or replaced by, tables the app uses
    (N'CommunicationsLog', 'Duplicate'),          -- the app uses CommunicationLog
    (N'tbl_AccountManager', 'Duplicate'),
    (N'Payments', 'Duplicate'),
    (N'UserLoginHistory', 'Duplicate'),           -- login tracking removed from the code

    -- Features that were never finished or were removed
    (N'ServiceEngagements', 'Unused feature'),
    (N'WorkflowItems', 'Unused feature'),
    (N'WorkflowTemplates', 'Unused feature'),
    (N'EmployerACAProfile', 'Unused feature'),
    (N'ImportDiffLog', 'Unused feature'),
    (N'tbl_FileImportImpactAnalysis', 'Unused feature'),
    (N'Tracker_BillingContact', 'Unused feature'),
    (N'tbl_IntegrationSettings', 'Unused feature'),
    (N'mcMailServer', 'Unused feature'),

    -- Old migration backup
    (N'_ParamMigration_Backup', 'Old backup'),

    -- Procedure whose last caller (ShortcutController.Reorder) was removed
    (N'sp_ReorderUserShortcuts', 'Procedure');

-- 1a. Stop if a table that stays has a foreign key to a table that moves.
DECLARE @Blocked TABLE (Reason NVARCHAR(400));
INSERT INTO @Blocked (Reason)
SELECT N'FK ' + fk.name + N' on ' + OBJECT_SCHEMA_NAME(fk.parent_object_id) + N'.' + OBJECT_NAME(fk.parent_object_id)
       + N' references dbo.' + m.ObjName
FROM @Move m
JOIN sys.foreign_keys fk ON fk.referenced_object_id = OBJECT_ID(N'dbo.' + QUOTENAME(m.ObjName))
WHERE NOT (OBJECT_SCHEMA_NAME(fk.parent_object_id) = N'dbo'
           AND OBJECT_NAME(fk.parent_object_id) IN (SELECT ObjName FROM @Move));

-- 1b. Stop if a module that stays still names one of them (substring first, then a
--     whole-word match; '_' is escaped because it is a LIKE wildcard).
INSERT INTO @Blocked (Reason)
SELECT N'dbo.' + m.ObjName + N' is named by ' + OBJECT_SCHEMA_NAME(sm.object_id) + N'.' + OBJECT_NAME(sm.object_id)
FROM @Move m
JOIN sys.sql_modules sm ON CHARINDEX(m.ObjName, sm.definition) > 0
WHERE OBJECT_NAME(sm.object_id) NOT IN (SELECT ObjName FROM @Move)
  AND OBJECT_NAME(sm.object_id) <> N'sp_ResetOperationalData'
  AND OBJECT_SCHEMA_NAME(sm.object_id) <> N'archive'
  AND N' ' + sm.definition + N' ' LIKE N'%[^A-Za-z0-9_]' + REPLACE(m.ObjName, N'_', N'[_]') + N'[^A-Za-z0-9_]%';

IF EXISTS (SELECT 1 FROM @Blocked)
BEGIN
    SELECT Reason AS BlockedBecause FROM @Blocked ORDER BY Reason;
    RAISERROR(N'034: something that stays still depends on an object on the list (see result above). Nothing was moved.', 16, 1);
    RETURN;
END;

-- 2. Move.
BEGIN TRANSACTION;

DECLARE @Name SYSNAME, @Sql NVARCHAR(400), @Moved INT = 0;
DECLARE move_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT m.ObjName FROM @Move m
    WHERE OBJECT_ID(N'dbo.' + QUOTENAME(m.ObjName)) IS NOT NULL
      AND OBJECT_ID(N'archive.' + QUOTENAME(m.ObjName)) IS NULL
    ORDER BY m.ObjName;
OPEN move_cursor;
FETCH NEXT FROM move_cursor INTO @Name;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Sql = N'ALTER SCHEMA archive TRANSFER dbo.' + QUOTENAME(@Name) + N';';
    EXEC sys.sp_executesql @Sql;
    SET @Moved += 1;
    FETCH NEXT FROM move_cursor INTO @Name;
END;
CLOSE move_cursor;
DEALLOCATE move_cursor;

COMMIT TRANSACTION;

-- 3. Report.
SELECT @Moved AS MovedNow,
       (SELECT COUNT(*) FROM @Move) AS OnList,
       (SELECT COUNT(*) FROM @Move m WHERE OBJECT_ID(N'dbo.' + QUOTENAME(m.ObjName)) IS NOT NULL) AS StillInDbo,
       (SELECT COUNT(*) FROM @Move m WHERE OBJECT_ID(N'archive.' + QUOTENAME(m.ObjName)) IS NOT NULL) AS InArchive;
GO

/* ############################################################################
   036_fix_menu_rows.sql
   ############################################################################ */
PRINT '=== 036_fix_menu_rows.sql';
GO
/* =============================================================================
   036_fix_menu_rows.sql

   Two menu-data fixes found while checking which menu rows lead to missing pages.

   1. Whitespace in Controller values. Permission claims are built as
      "Controller:Action", so a tab or space in the value produces claims nothing
      matches (row 1049 "Report" under Tracker held TAB + "Tracker"). Every
      Controller value is trimmed of spaces, tabs, carriage returns and line feeds.
      The web app now trims the value when a menu is saved.

   2. The "Filing Year" menu (Controller 'FilingYear'). There is no FilingYear
      controller any more - filing years are managed on the System settings page -
      so the item would link to a missing page. It is set inactive (not deleted).

   Group headings that have child menus (Tracker, Reports, Administration) and the
   hidden permission-only rows (ACA Calculation, Consent, Portal Admin) are correct
   as they are: headings render as toggles, hidden rows are never shown.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

-- 1. Trim whitespace (incl. tab / CR / LF) from Controller values.
DECLARE @Trimmed TABLE (Id INT, OldValue NVARCHAR(200), NewValue NVARCHAR(200));

UPDATE m
SET    m.Controller = LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(m.Controller, CHAR(9), N''), CHAR(13), N''), CHAR(10), N''))),
       m.DateModified = GETDATE()
OUTPUT inserted.Id, deleted.Controller, inserted.Controller INTO @Trimmed (Id, OldValue, NewValue)
FROM   dbo.tbl_Menus m
WHERE  m.Controller IS NOT NULL
  AND  m.Controller <> LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(m.Controller, CHAR(9), N''), CHAR(13), N''), CHAR(10), N'')));

-- 2. Retire the Filing Year menu item (its controller no longer exists).
DECLARE @Retired TABLE (Id INT, MenuName NVARCHAR(200));

UPDATE m
SET    m.IsActive = 0,
       m.DateModified = GETDATE()
OUTPUT inserted.Id, inserted.MenuName INTO @Retired (Id, MenuName)
FROM   dbo.tbl_Menus m
WHERE  m.Controller = N'FilingYear'
  AND  m.IsActive = 1;

COMMIT TRANSACTION;

SELECT Id, N'Controller trimmed' AS Change,
       REPLACE(REPLACE(OldValue, CHAR(9), N'<TAB>'), CHAR(13), N'<CR>') AS OldValue, NewValue
FROM @Trimmed
UNION ALL
SELECT Id, N'Set inactive', MenuName, NULL FROM @Retired;
GO

/* ############################################################################
   037_disable_broken_logins.sql
   ############################################################################ */
PRINT '=== 037_disable_broken_logins.sql';
GO
/* =============================================================================
   037_disable_broken_logins.sql

   Logins whose link to their account record is broken. Nothing is deleted: a login
   is switched off with Flag = 0 (dbo.fn_UserLoginAllowed refuses it), and can be
   switched back on with Flag = 1.

   1. Duplicate staff logins (always applied).
      Two staff logins pointing at the same StaffAccounts row share one person's
      profile, dashboard scope and assignments. The login whose User_Name is NOT the
      staff record's e-mail is the stray one and is switched off; the login that
      matches the record is kept. (Found locally: james123@gmail.com on staff #2,
      whose record belongs to jamesmicheal007@gmail.com; it never signed in.)
      Logins without any staff link (Ref_ID NULL, e.g. the built-in admin) are not
      touched.

   2. Broker logins with no BrokerAccounts row (OFF by default).
      Such a login signs in with no name and an empty book of business. Locally this
      is broker@agency.com (Ref_ID 99992), which looks like a demo login and was
      used recently, so it is only switched off when @DisableOrphanBrokerLogins = 1.

   Each section prints the logins it changed (or would change). Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @DisableOrphanBrokerLogins BIT = 0;   -- set to 1 to also run section 2

BEGIN TRANSACTION;

-- 1. Duplicate staff logins.
DECLARE @Dupes TABLE (User_ID BIGINT, [User_Name] NVARCHAR(256), Ref_ID BIGINT, KeptLogin NVARCHAR(256));

INSERT @Dupes (User_ID, [User_Name], Ref_ID, KeptLogin)
SELECT u.User_ID, u.[User_Name], u.Ref_ID, keep.[User_Name]
FROM   dbo.tbl_User u
JOIN   dbo.StaffAccounts s ON s.Staff_ID = u.Ref_ID
JOIN   dbo.tbl_User keep
       ON  keep.Ref_ID = u.Ref_ID
       AND keep.User_ID <> u.User_ID
       AND ISNULL(keep.Role_ID, 0) NOT IN (10, 13)
       AND keep.[User_Name] = s.Staff_Email
WHERE  ISNULL(u.Role_ID, 0) NOT IN (10, 13)
  AND  u.Ref_ID IS NOT NULL
  AND  u.[User_Name] <> s.Staff_Email
  AND  ISNULL(u.Flag, 1) <> 0;

UPDATE u
SET    u.Flag = 0, u.ModifiedOn = GETDATE()
FROM   dbo.tbl_User u
JOIN   @Dupes d ON d.User_ID = u.User_ID;

SELECT 'Duplicate staff login switched off' AS [Change], User_ID, [User_Name], Ref_ID, KeptLogin
FROM   @Dupes;

-- 2. Broker logins with no broker record.
DECLARE @Orphans TABLE (User_ID BIGINT, [User_Name] NVARCHAR(256), Ref_ID BIGINT);

INSERT @Orphans (User_ID, [User_Name], Ref_ID)
SELECT u.User_ID, u.[User_Name], u.Ref_ID
FROM   dbo.tbl_User u
WHERE  u.Role_ID = 10
  AND  ISNULL(u.Flag, 1) <> 0
  AND  NOT EXISTS (SELECT 1 FROM dbo.BrokerAccounts b WHERE b.Broker_ID = u.Ref_ID);

IF @DisableOrphanBrokerLogins = 1
    UPDATE u
    SET    u.Flag = 0, u.ModifiedOn = GETDATE()
    FROM   dbo.tbl_User u
    JOIN   @Orphans o ON o.User_ID = u.User_ID;

SELECT CASE WHEN @DisableOrphanBrokerLogins = 1 THEN 'Broker login without broker record switched off'
            ELSE 'Broker login without broker record (not changed; set @DisableOrphanBrokerLogins = 1)' END AS [Change],
       User_ID, [User_Name], Ref_ID
FROM   @Orphans;

COMMIT TRANSACTION;
GO

/* ############################################################################
   038_employee_employerid_on_manual_add.sql
   ############################################################################ */
PRINT '=== 038_employee_employerid_on_manual_add.sql';
GO
/* =============================================================================
   038_employee_employerid_on_manual_add.sql

   Employee.EmployerId is what the web app checks before it lets anyone open or
   change an employee (BaseController.CheckEmployeeAccessAsync). The file import
   fills it in, but usp_employee_basic_details_action - the "Add employee" form -
   never did: it wrote the employer to EmployeeCode only. A manually added employee
   therefore had no employer, failed the check, and could not be opened by anyone,
   admins included. (Found locally: 1 of 927 employees, id 927, employer 1021.)

   1. usp_employee_basic_details_action
      - Insert: Employee.EmployerId is set from @EmployerId.
      - Update: Employee.EmployerId is kept in step with EmployeeCode.employerId,
        which this procedure already overwrites with @EmployerId. A NULL
        @EmployerId leaves Employee.EmployerId as it was.
      Nothing else in the procedure is changed.

   2. Backfill
      Employees with no EmployerId take the employer of their latest EmployeeCode
      row (highest filingYear, then highest id). Employees with no EmployeeCode row
      stay NULL and are listed. Rows whose EmployerId disagrees with their
      EmployeeCode rows are listed only, not changed.

   Prints what it changed. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1. Procedure ---------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_employee_basic_details_action]
    @EmployeeID bigint = null,
    @IsCorrected BIT = null,
    @FirstName NVARCHAR(100) = null,
    @MiddleName NVARCHAR(100) = NULL,
    @LastName NVARCHAR(100) = null,
    @SSN NVARCHAR(20) = null,
    @Suffix NVARCHAR(20) = NULL,
    @Birthday DATE = null,
    @Email NVARCHAR(255) = null,
    @Address NVARCHAR(255) = null,
    @Address2 NVARCHAR(255) = NULL,
    @City NVARCHAR(100) = null,
    @State NVARCHAR(50) = null,
    @Zip NVARCHAR(20)= null,
    @Country NVARCHAR(100) = NULL,
    @IsForeign BIT = null,
    @getForm BIT = null,
    @ConsentedToElectronic BIT = null,
    @IsExPatriot BIT= null,
    @EmployerId bigint = null,
    @FilingYear int = null,
    @StateId INT = null,
    @CountryId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        ---------------------------------------------------------------
        -- 0a. DOB cannot be a future date
        ---------------------------------------------------------------
        IF @Birthday IS NOT NULL AND @Birthday > CAST(GETDATE() AS DATE)
        BEGIN
            RAISERROR('INVALID_DOB: Date of Birth cannot be a future date.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END

        ---------------------------------------------------------------
        -- 0b. Duplicate SSN check (scoped to same Employer + FilingYear)
        ---------------------------------------------------------------
        IF @SSN IS NOT NULL AND LEN(LTRIM(RTRIM(@SSN))) > 0
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM Employee e
                INNER JOIN EmployeeCode ec ON ec.employeeId = e.id
                WHERE e.SSN = @SSN
                  AND ec.employerId = @EmployerId
                  AND ec.filingYear = @FilingYear
                  AND e.id <> ISNULL(@EmployeeID, 0)   -- exclude self on update
            )
            BEGIN
                RAISERROR('DUPLICATE_SSN: An employee with this SSN already exists for this employer and filing year.', 16, 1);
                ROLLBACK TRANSACTION;
                RETURN;
            END
        END

        ---------------------------------------------------------------
        -- Declare locals AFTER validation passes
        ---------------------------------------------------------------
        DECLARE @NewEmployeeCodeID INT = NULL;

        IF EXISTS (SELECT 1 FROM Employee WHERE id = @EmployeeID)
        BEGIN
            -- 1. Update existing employee
            UPDATE Employee
            SET
               -- IsCorrected = @IsCorrected,
                FirstName   = @FirstName,
                MiddleName  = @MiddleName,
                LastName    = @LastName,
                SSN         = @SSN,
                Suffix      = @Suffix,
                Birthday    = @Birthday,
                email       = @Email,
                [Address]   = @Address,
                Address2    = @Address2,
                City        = @City,
                stateId     = @StateId,
                [state]     = @State,
                Zip         = @Zip,
                countryId   = @CountryId,
                IsForeign   = @IsForeign,
                HasConsentedToElectronic = @ConsentedToElectronic,
                IsExPatriot = @IsExPatriot,
                -- 038: kept in step with EmployeeCode.employerId below
                EmployerId  = COALESCE(@EmployerId, EmployerId)
            WHERE id = @EmployeeID;
            --update the isCorrected , getForm in employeedcode
            UPDATE EmployeeCode set isCorrected = @IsCorrected , getForm = @getForm ,employerId = @EmployerId  where employeeId = @EmployeeID;
        END
        ELSE
        BEGIN
            -- 2. Insert new employee
            INSERT INTO Employee ( --IsCorrected,
                fullName, FirstName, MiddleName, LastName, SSN, Suffix,
                Birthday, email, [Address], Address2, City, stateId,[state], Zip,
                countryId, IsForeign, HasConsentedToElectronic, IsExPatriot,
                EmployerId   -- 038
            )
            VALUES (
                --@IsCorrected,
                LTRIM(RTRIM(
                    ISNULL(LTRIM(RTRIM(@FirstName)) + ' ', '') +
                    ISNULL(LTRIM(RTRIM(@LastName)), '')
                )), @FirstName, @MiddleName, @LastName, @SSN, @Suffix,
                @Birthday, @Email, @Address, @Address2, @City, @StateId,@State,@Zip,
                @CountryId, @IsForeign,@ConsentedToElectronic ,@IsExPatriot,
                @EmployerId   -- 038
            );

            SET @EmployeeID = SCOPE_IDENTITY();

            -- 3. Insert into EmployeeCode
            INSERT INTO EmployeeCode (
                employeeId,
                employerId,
                filingYear,
                getForm,
                isCorrected,
                IsLocked
            )
            VALUES (
                @EmployeeID,
                @EmployerId,
                @FilingYear,
                @getForm,
                @IsCorrected,
                0
            );

           -- SET @NewEmployeeCodeID = SCOPE_IDENTITY();
        END

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        DECLARE @ErrorMessage  NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT            = ERROR_SEVERITY();
        DECLARE @ErrorState    INT            = ERROR_STATE();

        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END;
GO

-- 2. Backfill ----------------------------------------------------------------
BEGIN TRANSACTION;

DECLARE @Filled TABLE (EmployeeId INT, EmployerId BIGINT, FromFilingYear INT);

UPDATE e
SET    e.EmployerId = latest.employerId
OUTPUT inserted.id, inserted.EmployerId, latest.filingYear
INTO   @Filled (EmployeeId, EmployerId, FromFilingYear)
FROM   dbo.Employee e
CROSS APPLY (
    SELECT TOP (1) ec.employerId, ec.filingYear
    FROM   dbo.EmployeeCode ec
    WHERE  ec.employeeId = e.id
    ORDER BY ec.filingYear DESC, ec.id DESC
) latest
WHERE  e.EmployerId IS NULL;

COMMIT TRANSACTION;

DECLARE @FilledCount INT = (SELECT COUNT(*) FROM @Filled);
PRINT CONCAT('Employees given an EmployerId: ', @FilledCount);
SELECT EmployeeId, EmployerId, FromFilingYear FROM @Filled ORDER BY EmployeeId;

-- Still NULL: no EmployeeCode row to take an employer from.
SELECT e.id AS EmployeeId, e.fullName, e.IsDeleted
FROM   dbo.Employee e
WHERE  e.EmployerId IS NULL;

-- Report only: EmployerId not matching any of the employee's EmployeeCode rows.
SELECT e.id AS EmployeeId, e.EmployerId AS EmployeeEmployerId,
       STRING_AGG(CONCAT(ec.filingYear, ':', ec.employerId), ', ') AS EmployeeCodeEmployers
FROM   dbo.Employee e
JOIN   dbo.EmployeeCode ec ON ec.employeeId = e.id
WHERE  e.EmployerId IS NOT NULL
  AND  NOT EXISTS (SELECT 1 FROM dbo.EmployeeCode x
                   WHERE x.employeeId = e.id AND x.employerId = e.EmployerId)
GROUP BY e.id, e.EmployerId;
GO

/* ############################################################################
   039_tracker_list_employer_scope.sql
   ############################################################################ */
PRINT '=== 039_tracker_list_employer_scope.sql';
GO
/* =============================================================================
   039_tracker_list_employer_scope.sql

   The Tracker employer list (sp_Tracker_GetEmployerList) showed every employer to
   everyone who can open the Tracker. Account Managers and Data Analysts work on their
   own employers everywhere else, so the list now takes the caller's employers:

   - New optional parameter @EmployerIDs (comma-separated employer ids). The web app
     passes it only for roles that do not see every employer; NULL keeps today's
     behaviour, so SuperAdmin / Admin / ACA Director / supervisors are unchanged.
   - A row is shown when the employer, or its primary employer (affiliates), is in
     the list.
   Everything else in the procedure is unchanged.

   Run before deploying the web app that passes @EmployerIDs. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_Tracker_GetEmployerList]

    -- %% Search %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @SearchText             NVARCHAR(200)   = NULL,
    @MainEmployerId         INT             = NULL,
    @AffiliateEmployerId    INT             = NULL,
    -- %% Organization %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @FirmId                 INT             = NULL,
    @BrokerId               INT             = NULL,
    @State                  NVARCHAR(5)     = NULL,
    @FilingYear             NVARCHAR(10)    = NULL,

    -- %% Team %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @AcctManagerId          INT             = NULL,
    @DataAnalystId          INT             = NULL,
    @SalesRepId             INT             = NULL,

    -- %% Configuration %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @FundingType            NVARCHAR(100)   = NULL,
    @FormType               NVARCHAR(50)    = NULL,
    @LevelOfComplexity      NVARCHAR(50)    = NULL,
    @IndustryId             INT             = NULL,

    -- %% Flags %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @IsPriority             BIT             = NULL,
    @NoAutoRenewal          BIT             = NULL,
    @SFTP                   BIT             = NULL,
    @AnnualCOI              BIT             = NULL,

    -- %% Access (039) %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    -- Comma-separated employer ids the caller may see. NULL = no restriction (roles that
    -- see every employer); "-1" or an empty list = nothing.
    @EmployerIDs            NVARCHAR(MAX)   = NULL,

    -- %% Pagination / sorting %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @SortColumn             NVARCHAR(50)    = 'EmployerName',
    @SortOrder              NVARCHAR(4)     = 'ASC',
    @PageNumber             INT             = 1,
    @PageSize               INT             = 15

AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;

    -- %% Sanitise sort inputs (prevent dynamic SQL injection) %%
    SET @SortColumn = CASE
        WHEN @SortColumn IN ('EmployerName', 'FirmName', 'State', 'City', 'AccountManagerName', 'DataAnalyst') THEN @SortColumn
        ELSE 'EmployerName'
    END;
    SET @SortOrder = CASE WHEN @SortOrder = 'DESC' THEN 'DESC' ELSE 'ASC' END;

    SELECT
        e.id                                                    AS EmployerId,
        e.[name]                                                AS EmployerName,
        e.taxid                                                  AS EIN,
        e.city                                                   AS City,
        e.[state]                                                AS State,
        e.zip                                                     AS Zip,
        f.FirmName                                                AS FirmName,
        CAST(ISNULL(ext.Priority,            0) AS BIT)           AS IsPriority,
        CAST(ISNULL(ext._noAutoRenewal,      0) AS BIT)           AS NoAutoRenewal,
        CAST(ISNULL(ext.SFTP,                0) AS BIT)           AS SFTP,
        CAST(ISNULL(ext._AnnualCOI,          0) AS BIT)           AS AnnualCOI,
        ext._fundingtype                                          AS FundingType,
        ext._FormType                                             AS FormType,
        ext._LevelofComplexity                                    AS LevelOfComplexity,
        ext._planyear                                             AS PlanYear,
        am.Staff_FName + ' ' + am.Staff_LName                     AS AccountManagerName,
        da.Staff_FName + ' ' + da.Staff_LName                     AS DataAnalyst,
        COUNT(*) OVER ()                                          AS TotalCount
    FROM            [dbo].[Employer]                     e
    LEFT JOIN       [dbo].[Tracker_EmployerExtension]    ext  ON e.id = ext.EmployerId
    LEFT JOIN       [dbo].[Tracker_Firm]                 f    ON ext.firmid = f.FirmId
    LEFT JOIN       [dbo].[Tracker_Broker]               b    ON ext.brokerid = b.BrokerId
    LEFT JOIN       [dbo].[StaffAccounts]                am   ON ext.AcctManagerid = am.Staff_ID
    LEFT JOIN       [dbo].[StaffAccounts]                da   ON ext._dataAnalyst = da.Staff_ID
    LEFT JOIN       [dbo].[tbl_Medtracker_SalesRep]      sr   ON ext.srepid = sr.srepid
    WHERE
        -- %% Preserved exactly from original SP %%%%%%%%%%%%%%%%%%%%%%%%%%%%
        e.IsDeleted = 0

        -- %% Access (039): only the caller's employers, and affiliates of them %%
        AND (@EmployerIDs IS NULL
             OR e.id        IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@EmployerIDs, ','))
             OR e.companyId IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@EmployerIDs, ',')))

        -- %% Existing filters (preserved, converted to static) %
        AND (@SearchText IS NULL OR @SearchText = ''
                  OR e.[name] LIKE '%' + @SearchText + '%'
                  OR e.taxid   LIKE '%' + @SearchText + '%')
        AND (@FirmId           IS NULL OR ext.firmid           = @FirmId)
        AND (@BrokerId         IS NULL OR ext.brokerid         = @BrokerId)
        AND (@FundingType      IS NULL OR @FundingType = ''
                  OR ext._fundingtype = @FundingType)
        AND (@IsPriority       IS NULL OR ext.Priority         = @IsPriority)

        -- %% New filters (all nullable, no effect when NULL) %%%
        AND (@State                    IS NULL OR e.[state]                          = @State)
        AND (@FilingYear                IS NULL OR e.filingYear						 = @FilingYear)
        AND (@AcctManagerId             IS NULL OR ext.AcctManagerid                 = @AcctManagerId)
        AND (@DataAnalystId             IS NULL OR TRY_CAST(ext._dataAnalyst AS INT)  = @DataAnalystId)
        AND (@SalesRepId                IS NULL OR ext.srepid                        = @SalesRepId)
        AND (@FormType                  IS NULL OR ext._FormType                     = @FormType)
        AND (@LevelOfComplexity         IS NULL OR ext._LevelofComplexity            = @LevelOfComplexity)
        AND (@IndustryId                IS NULL OR ext._IndustryId                   = @IndustryId)
        AND (@NoAutoRenewal             IS NULL OR ext._noAutoRenewal                = @NoAutoRenewal)
        AND (@SFTP                      IS NULL OR ext.SFTP                          = @SFTP)
        AND (@AnnualCOI                 IS NULL OR ext._AnnualCOI                    = @AnnualCOI)
        -- If affiliate selected -> show only that affiliate
        -- If main selected (no affiliate) -> show main + all its affiliates
        -- If nothing selected -> show everything (existing companyId logic)
        AND (
            @AffiliateEmployerId IS NOT NULL AND e.id = @AffiliateEmployerId
            OR
            @AffiliateEmployerId IS NULL
            AND @MainEmployerId IS NOT NULL
            AND (e.id = @MainEmployerId OR e.companyId = @MainEmployerId)
            OR
            @AffiliateEmployerId IS NULL
            AND @MainEmployerId IS NULL
            AND (e.companyId IS NULL OR e.companyId = 0)
        )
    ORDER BY
        CASE WHEN @SortColumn = 'EmployerName'       AND @SortOrder = 'ASC'  THEN e.[name]                              END ASC,
        CASE WHEN @SortColumn = 'EmployerName'       AND @SortOrder = 'DESC' THEN e.[name]                              END DESC,
        CASE WHEN @SortColumn = 'FirmName'           AND @SortOrder = 'ASC'  THEN f.FirmName                           END ASC,
        CASE WHEN @SortColumn = 'FirmName'           AND @SortOrder = 'DESC' THEN f.FirmName                           END DESC,
        CASE WHEN @SortColumn = 'State'              AND @SortOrder = 'ASC'  THEN e.[state]                            END ASC,
        CASE WHEN @SortColumn = 'State'              AND @SortOrder = 'DESC' THEN e.[state]                            END DESC,
        CASE WHEN @SortColumn = 'AccountManagerName' AND @SortOrder = 'ASC'  THEN am.Staff_FName + ' ' + am.Staff_LName END ASC,
        CASE WHEN @SortColumn = 'AccountManagerName' AND @SortOrder = 'DESC' THEN am.Staff_FName + ' ' + am.Staff_LName END DESC,
        CASE WHEN @SortColumn = 'DataAnalyst'        AND @SortOrder = 'ASC'  THEN da.Staff_FName + ' ' + da.Staff_LName END ASC,
        CASE WHEN @SortColumn = 'DataAnalyst'      AND @SortOrder = 'DESC' THEN da.Staff_FName + ' ' + da.Staff_LName END DESC,
        e.[name] ASC -- tiebreaker

    OFFSET @Offset ROWS
    FETCH NEXT @PageSize ROWS ONLY;

END
GO

/* ############################################################################
   040_disable_orphan_logins.sql
   ############################################################################ */
PRINT '=== 040_disable_orphan_logins.sql';
GO
/* =============================================================================
   040_disable_orphan_logins.sql

   Broker and Employer logins whose account record does not exist. Such a login
   signs in with no name and no employers, so it can do nothing useful and is
   only a way in. Nothing is deleted: the login is switched off with Flag = 0
   (dbo.fn_UserLoginAllowed refuses it; Flag = 1 switches it back on) and any
   session still open for it is revoked.

   How a login is linked (tbl_User.Ref_ID):
     Broker   (Role_ID 10) -> dbo.BrokerAccounts.Broker_ID
     Employer (Role_ID 13) -> dbo.Employer.id (the employer portal login; see
                              TrackerEmployerRepository.GetPortalUserByEmployerAsync)
   An Employer login whose employer exists is kept even when there is no
   EmployerAccounts row (e.g. marees2808@gmail.com -> 1013,
   test_employer1@gmail.com -> 1021).

   Found locally: employer@client.com (Ref_ID 99991) and broker@agency.com
   (Ref_ID 99992), demo logins created 2025-11-20.

   This replaces section 2 of 037 (brokers only, off by default). Prints the
   logins it changed. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @Orphans TABLE (User_ID BIGINT, [User_Name] NVARCHAR(256), Role_ID INT, Ref_ID BIGINT);

INSERT @Orphans (User_ID, [User_Name], Role_ID, Ref_ID)
SELECT u.User_ID, u.[User_Name], u.Role_ID, u.Ref_ID
FROM   dbo.tbl_User u
WHERE  ISNULL(u.Flag, 1) <> 0
  AND  (   (u.Role_ID = 10 AND NOT EXISTS (SELECT 1 FROM dbo.BrokerAccounts b WHERE b.Broker_ID = u.Ref_ID))
        OR (u.Role_ID = 13 AND NOT EXISTS (SELECT 1 FROM dbo.Employer e WHERE e.id = u.Ref_ID)));

UPDATE u
SET    u.Flag = 0, u.ModifiedOn = GETDATE()
FROM   dbo.tbl_User u
JOIN   @Orphans o ON o.User_ID = u.User_ID;

-- Sign them out now rather than when their session expires.
UPDATE s
SET    s.RevokedAt = SYSUTCDATETIME()
FROM   dbo.tbl_AuthSessions s
JOIN   @Orphans o ON s.Subject = CONCAT('user:', o.User_ID)
WHERE  s.RevokedAt IS NULL;

SELECT 'Login without account record switched off' AS [Change],
       User_ID, [User_Name],
       CASE Role_ID WHEN 10 THEN 'Broker' ELSE 'Employer' END AS [Role], Ref_ID
FROM   @Orphans;

COMMIT TRANSACTION;
GO

/* ############################################################################
   041_file_visible_to_user.sql
   ############################################################################ */
PRINT '=== 041_file_visible_to_user.sql';
GO
/* =============================================================================
   041_file_visible_to_user.sql

   dbo.fn_FileVisibleToUser(@FileLogId, @UserId): may this user open this uploaded
   file? The same rule sp_SearchFileLogsPaginated uses to decide which files the
   File Upload list shows, so a file can be opened exactly when it is listed:

     - SuperAdmin, Admin, ACA Director, DA Supervisor, AM Supervisor, StaffAU
       (Role_ID 2, 3, 4, 5, 6, 15) see every file;
     - anyone sees a file they uploaded or that is assigned to them;
     - otherwise the file's employer (matched on EIN and plan year, as the list
       does) must be theirs: the Employer login's own employer, an Account
       Manager's portfolio, a Data Analyst's operational assignment, or a
       Broker's assigned employers.

   Before this, the per-file checks in FileUploadWorkflowService disagreed with the
   list: downloads, archive and delete let every Account Manager open any file by
   id, while the status / details checks refused supervisors and employer-scoped
   users files the list showed them. Keep this function and the list procedure's
   "Security Scoping" block in step.

   Read-only (creates a function). Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER FUNCTION dbo.fn_FileVisibleToUser (@FileLogId INT, @UserId INT)
RETURNS TABLE
AS
RETURN
    SELECT CAST(CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.UploadedFileLog log
        JOIN dbo.tbl_User u ON u.User_ID = @UserId
        WHERE log.FileLogId = @FileLogId
          AND (
                u.Role_ID IN (2, 3, 4, 5, 6, 15)
             OR log.UploadedByUserId = CAST(@UserId AS VARCHAR(20))
             OR log.AssignedToUserId = CAST(@UserId AS VARCHAR(20))
             OR EXISTS (
                    SELECT 1
                    FROM dbo.Employer emp
                    WHERE emp.taxid = log.TaxID
                      AND emp.filingYear = log.PlanYear
                      AND (
                            (u.Role_ID = 13 AND emp.id = u.Ref_ID)
                         OR (u.Role_ID = 16 AND EXISTS (SELECT 1 FROM dbo.tbl_Portfolio_Assignment pa
                                                        WHERE pa.AM_UserID = @UserId AND pa.EmployerID = emp.id AND pa.IsActive = 1))
                         OR (u.Role_ID = 11 AND EXISTS (SELECT 1 FROM dbo.tbl_Operational_Assignment oa
                                                        WHERE oa.DA_UserID = @UserId AND oa.EmployerID = emp.id AND oa.IsActive = 1))
                         OR (u.Role_ID = 10 AND EXISTS (SELECT 1 FROM dbo.tbl_Employer_Broker_Assignment ba
                                                        WHERE ba.Broker_UserID = @UserId AND ba.EmployerID = emp.id AND ba.IsActive = 1))
                      )
                )
              )
    ) THEN 1 ELSE 0 END AS BIT) AS IsVisible;
GO

PRINT '041: dbo.fn_FileVisibleToUser created.';
GO

/* ############################################################################
   042_user_accounts_list_login_state.sql
   ############################################################################ */
PRINT '=== 042_user_accounts_list_login_state.sql';
GO
/* =============================================================================
   042_user_accounts_list_login_state.sql

   Administration > User Accounts listed logins wrongly in two ways:

   1. A login switched off with tbl_User.Flag = 0 (scripts 037 and 040, or by hand)
      still showed as Active, because Status came only from the account record
      (Staff_Status / Broker_Status / Employer_Status). The list now returns
      LoginEnabled, and reports such a login as Inactive (2), so the Active
      filter leaves it out; the page shows "Login off".

   2. The staff rows joined every login to StaffAccounts on Ref_ID, whatever its
      role. A Broker or Employer login whose Ref_ID happened to equal a Staff_ID
      was listed a second time, under that staff member's name and e-mail (for
      example an Employer login shown as "Super Admin"). Staff rows now take only
      logins of a staff role, the same rule sp_Login_access uses.

   Otherwise the procedure is unchanged (same parameters and result sets).
   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_UserAccountsList]
    @PageIndex INT = 1,
    @PageSize INT = 20,
    @Search VARCHAR(100) = NULL,
    @SortColumn INT = 0,
    @SortOrder VARCHAR(5) = 'asc',
    @RoleId int =0,
    @StatusId int =0
AS
BEGIN
    DECLARE @RecordCount INT;
    DECLARE @StartPage INT;
    DECLARE @EndPage INT;

    BEGIN TRY
        BEGIN TRANSACTION;
        SET NOCOUNT ON;

        -- Create temporary table to hold combined data
        CREATE TABLE #CombinedUsers
        (
            UserAccounts_ID INT,
            UserAccounts_FName NVARCHAR(100),
            UserAccounts_LName NVARCHAR(100),
            UserAccounts_Addr1 NVARCHAR(255),
            UserAccounts_Addr2 NVARCHAR(255),
            UserAccounts_City NVARCHAR(100),
            UserAccounts_State NVARCHAR(50),
            UserAccounts_Zip NVARCHAR(20),
            UserAccounts_Phone NVARCHAR(20),
            UserAccounts_Email NVARCHAR(100),
            UserAccounts_Status INT,
            CreatedOn DATETIME,
            CreatedBy INT,
            ModifiedOn DATETIME,
            ModifiedBy INT,
            UserType NVARCHAR(20),
            UserAccounts_Role INT,
            Temp_Password NVARCHAR(255),
            ProfilePicture NVARCHAR(MAX),
            UserAccounts_Role_Name NVARCHAR(100),
            UserAccounts_Full_Name NVARCHAR(201),
            AssginCount INT,
            IsMFA INT,
            LoginEnabled BIT
        );

        -------------------------------------------------------
        -- INSERT STAFF DATA
        -------------------------------------------------------
        INSERT INTO #CombinedUsers
        SELECT
            staf.Staff_ID AS UserAccounts_ID,
            staf.Staff_FName AS UserAccounts_FName,
            staf.Staff_LName AS UserAccounts_LName,
            staf.Staff_Addr1 AS UserAccounts_Addr1,
            staf.Staff_Addr2 AS UserAccounts_Addr2,
            staf.Staff_City AS UserAccounts_City,
            staf.Staff_State AS UserAccounts_State,
            staf.Staff_Zip AS UserAccounts_Zip,
            staf.Staff_Phone AS UserAccounts_Phone,
            staf.Staff_Email AS UserAccounts_Email,
            CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 2 ELSE staf.Staff_Status END AS UserAccounts_Status,
            staf.CreatedOn,
            staf.CreatedBy,
            staf.ModifiedOn,
            staf.ModifiedBy,
            'Staff' AS UserType,
            us.Role_ID,
            us.Temp_Password,
            us.Profile_Picture,
            roles.RoleName,
            --us.[User_ID],
            COALESCE(staf.Staff_FName, '') + ' ' + COALESCE(staf.Staff_LName, '') AS UserAccounts_Full_Name,
            CASE
        WHEN us.Role_ID = 16 OR us.Role_ID = 6 -- Account Manager
        THEN (SELECT COUNT(*) FROM tbl_Portfolio_Assignment WHERE AM_UserID = us.User_ID And IsActive = 1)

        WHEN us.Role_ID = 11 OR us.Role_ID = 5 -- Data Analyst
        THEN (SELECT COUNT(*) FROM tbl_Operational_Assignment WHERE DA_UserID = us.User_ID And IsActive = 1)

    ELSE 0
        END AS ExtraCount,
        us.isMFA,
        CAST(CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 0 ELSE 1 END AS BIT)
        FROM StaffAccounts staf
        INNER JOIN tbl_user us ON us.Ref_ID = staf.Staff_ID
        INNER JOIN tbl_roles roles ON roles.ID = us.Role_ID
        WHERE staf.Staff_Status != 4
          AND ISNULL(us.Role_ID, 0) NOT IN (10, 13);   -- each login only with its own account type, as sp_Login_access

        -------------------------------------------------------
        -- INSERT BROKER DATA
        -------------------------------------------------------
        INSERT INTO #CombinedUsers
        SELECT
            br.Broker_ID AS UserAccounts_ID,
           br.Broker_FName,
            br.Broker_LName,
            br.Broker_Addr1,
            br.Broker_Addr2,
            br.Broker_City,
            br.Broker_State,
            br.Broker_Zip,
            br.Broker_Phone,
            br.Broker_Email,
            CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 2 ELSE br.Broker_Status END,
            br.CreatedOn,
            br.CreatedBy,
            br.ModifiedOn,
            br.ModifiedBy,
           'Broker' AS UserType,
            us.Role_ID,
            us.Temp_Password,
            us.Profile_Picture,
            roles.RoleName,
            COALESCE(br.Broker_FName, '') + ' ' + COALESCE(br.Broker_LName, '') AS UserAccounts_Full_Name,
            (SELECT COUNT(*) FROM tbl_Employer_Broker_Assignment WHERE Broker_UserID = us.User_ID And IsActive = 1) AS ExtraCount,
            us.isMFA,
        CAST(CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 0 ELSE 1 END AS BIT)
        FROM BrokerAccounts br
        INNER JOIN tbl_user us ON us.Ref_ID = br.Broker_ID
        INNER JOIN tbl_roles roles ON roles.ID = us.Role_ID
        WHERE roles.ID = 10 and br.Broker_Status != 4;

        -------------------------------------------------------
        -- INSERT EMPLOYER DATA
        -------------------------------------------------------
        INSERT INTO #CombinedUsers
        SELECT
            emp.Employer_ID AS UserAccounts_ID,
            emp.Employer_FName,
            emp.Employer_LName,
            emp.Employer_Addr1,
            emp.Employer_Addr2,
            emp.Employer_City,
            emp.Employer_State,
            emp.Employer_Zip,
            emp.Employer_Phone,
            emp.Employer_Email,
            CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 2 ELSE emp.Employer_Status END,
            emp.CreatedOn,
            emp.CreatedBy,
            emp.ModifiedOn,
            emp.ModifiedBy,
            'Employer' AS UserType,
            us.Role_ID,
            us.Temp_Password,
            us.Profile_Picture,
            roles.RoleName,
            COALESCE(emp.Employer_FName, '') + ' ' + COALESCE(emp.Employer_LName, '') AS UserAccounts_Full_Name,
            NULL AS ExtraCount,
            us.isMFA,
        CAST(CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 0 ELSE 1 END AS BIT)
        FROM EmployerAccounts emp
        INNER JOIN tbl_user us ON us.Ref_ID = emp.Employer_ID
        INNER JOIN tbl_roles roles ON roles.ID = us.Role_ID
        WHERE roles.ID = 13 and emp.Employer_Status != 4;

        -------------------------------------------------------
        -- Get total record count with search filter
        -------------------------------------------------------
        SELECT @RecordCount = COUNT(*)
        FROM #CombinedUsers
        WHERE (@Search IS NULL
            OR UserAccounts_Full_Name LIKE '%' + @Search + '%'
               OR UserAccounts_Email LIKE '%' + @Search + '%')
               and(@RoleId =0 OR UserAccounts_Role = @RoleId)
               and(@StatusId =0 OR UserAccounts_Status = @StatusId);


        -- Calculate pagination values
        SET @StartPage = (@PageIndex - 1) * @PageSize + 1;
        SET @EndPage = @PageIndex * @PageSize;

        -------------------------------------------------------
        -- Main query with pagination and sorting
        -------------------------------------------------------
        SELECT
            UserAccounts_ID,
            UserAccounts_FName,
            UserAccounts_LName,
            UserAccounts_Addr1,
            UserAccounts_Addr2,
        UserAccounts_City,
            UserAccounts_State,
            UserAccounts_Zip,
            UserAccounts_Phone,
            UserAccounts_Email,
            UserAccounts_Status,
            CreatedOn,
            CreatedBy,
            ModifiedOn,
            ModifiedBy,
            UserType,
            UserAccounts_Role,
            Temp_Password,
            CASE
                WHEN ProfilePicture IS NOT NULL AND ProfilePicture != '' THEN ProfilePicture
                ELSE UPPER(
                    ISNULL(LEFT(NULLIF(UserAccounts_FName, ''), 1), '') +
                    ISNULL(LEFT(NULLIF(UserAccounts_LName, ''), 1), '')
                )
            END AS ProfilePicture,
            UserAccounts_Role_Name,
            UserAccounts_Full_Name,
            AssginCount,
            IsMFA,
            LoginEnabled
        FROM #CombinedUsers
        WHERE (@Search IS NULL
               OR UserAccounts_Full_Name LIKE '%' + @Search + '%'
               OR UserAccounts_Email LIKE '%' + @Search + '%')
               and(@RoleId =0 OR UserAccounts_Role = @RoleId)
               and(@StatusId =0 OR UserAccounts_Status = @StatusId)
        ORDER BY
            CASE WHEN (@SortColumn = 0 AND @SortOrder = 'asc') THEN UserAccounts_Full_Name END ASC,
            CASE WHEN (@SortColumn = 0 AND @SortOrder = 'desc') THEN UserAccounts_Full_Name END DESC,
            CASE WHEN (@SortColumn = 1 AND @SortOrder = 'asc') THEN UserAccounts_Email END ASC,
            CASE WHEN (@SortColumn = 1 AND @SortOrder = 'desc') THEN UserAccounts_Email END DESC,
            CASE WHEN (@SortColumn = 2 AND @SortOrder = 'asc') THEN CreatedOn END ASC,
            CASE WHEN (@SortColumn = 2 AND @SortOrder = 'desc') THEN CreatedOn END DESC
        OFFSET @PageSize * (@PageIndex - 1) ROWS
        FETCH NEXT @PageSize ROWS ONLY;

        -------------------------------------------------------
        -- Return pagination metadata
        -------------------------------------------------------
        SELECT
            @RecordCount as [RowCount],
            @StartPage as StartPage,
            CASE WHEN @EndPage > @RecordCount THEN @RecordCount ELSE @EndPage END as EndPage,
            @PageIndex as PageNumber,
            @RecordCount as TotalCount;




       -------------------------------------------------------
-- Summary: Count users by each role with sample details
-------------------------------------------------------
SELECT
    CU.UserAccounts_Role,
    R.RoleName,
    COUNT(*) AS TotalUsers,

    -- ALL users for this role
    STRING_AGG(CU.UserAccounts_Full_Name, ', ') AS AllUsers,

    -- ALL profile pictures for this role (using same initials logic)
    STRING_AGG(
        CASE
            WHEN CU.ProfilePicture IS NOT NULL AND CU.ProfilePicture != ''
                THEN CU.ProfilePicture
            ELSE UPPER(
                    ISNULL(LEFT(NULLIF(CU.UserAccounts_FName, ''), 1), '') +
                    ISNULL(LEFT(NULLIF(CU.UserAccounts_LName, ''), 1), '')
                 )
        END,
        ', '
    ) AS AllProfilePictures ,



  SUM(
    CASE
        WHEN CU.ProfilePicture IS NOT NULL AND CU.ProfilePicture != ''
            THEN 1   -- this is real picture
        WHEN CU.ProfilePicture IS NULL OR CU.ProfilePicture = ''
            THEN 1   -- this is initials avatar
        ELSE 0
    END
) AS TotalProfileCount




FROM #CombinedUsers CU
INNER JOIN tbl_roles R ON R.ID = CU.UserAccounts_Role
GROUP BY CU.UserAccounts_Role, R.RoleName
ORDER BY R.RoleName;


        -- Clean up temporary table
        DROP TABLE #CombinedUsers;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- Clean up temporary table if it exists
        IF OBJECT_ID('tempdb..#CombinedUsers') IS NOT NULL
            DROP TABLE #CombinedUsers;

        INSERT INTO dbo.DB_Errors
        VALUES
        (SUSER_SNAME(),
         ERROR_NUMBER(),
         ERROR_STATE(),
         ERROR_SEVERITY(),
         ERROR_LINE(),
         ERROR_PROCEDURE(),
         ERROR_MESSAGE(),
         GETDATE());

        IF (XACT_STATE()) = -1
            ROLLBACK TRANSACTION
        IF (XACT_STATE()) = 1
            COMMIT TRANSACTION

        THROW;
    END CATCH
END
GO

PRINT '042: sp_UserAccountsList updated.';
GO

/* ############################################################################
   043_tracker_list_filing_year.sql
   ############################################################################ */
PRINT '=== 043_tracker_list_filing_year.sql';
GO
/* =============================================================================
   043_tracker_list_filing_year.sql

   The Tracker employer list shows every employer record, and an employer has one
   record per filing year, so the same employer (same name, same EIN) appeared
   several times with nothing to tell the rows apart. sp_Tracker_GetEmployerList
   now also returns the record's filing year (e.filingYear AS FilingYear), which
   the list shows next to the EIN.

   Otherwise identical to 039 (same parameters, filters, paging and employer
   scope). Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_Tracker_GetEmployerList]

    -- %% Search %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @SearchText             NVARCHAR(200)   = NULL,
    @MainEmployerId         INT             = NULL,
    @AffiliateEmployerId    INT             = NULL,
    -- %% Organization %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @FirmId                 INT             = NULL,
    @BrokerId               INT             = NULL,
    @State                  NVARCHAR(5)     = NULL,
    @FilingYear             NVARCHAR(10)    = NULL,

    -- %% Team %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @AcctManagerId          INT             = NULL,
    @DataAnalystId          INT             = NULL,
    @SalesRepId             INT             = NULL,

    -- %% Configuration %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @FundingType            NVARCHAR(100)   = NULL,
    @FormType               NVARCHAR(50)    = NULL,
    @LevelOfComplexity      NVARCHAR(50)    = NULL,
    @IndustryId             INT             = NULL,

    -- %% Flags %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @IsPriority             BIT             = NULL,
    @NoAutoRenewal          BIT             = NULL,
    @SFTP                   BIT             = NULL,
    @AnnualCOI              BIT             = NULL,

    -- %% Access (039) %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    -- Comma-separated employer ids the caller may see. NULL = no restriction (roles that
    -- see every employer); "-1" or an empty list = nothing.
    @EmployerIDs            NVARCHAR(MAX)   = NULL,

    -- %% Pagination / sorting %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @SortColumn             NVARCHAR(50)    = 'EmployerName',
    @SortOrder              NVARCHAR(4)     = 'ASC',
    @PageNumber             INT             = 1,
    @PageSize               INT             = 15

AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;

    -- %% Sanitise sort inputs (prevent dynamic SQL injection) %%
    SET @SortColumn = CASE
        WHEN @SortColumn IN ('EmployerName', 'FirmName', 'State', 'City', 'AccountManagerName', 'DataAnalyst') THEN @SortColumn
        ELSE 'EmployerName'
    END;
    SET @SortOrder = CASE WHEN @SortOrder = 'DESC' THEN 'DESC' ELSE 'ASC' END;

    SELECT
        e.id                                                    AS EmployerId,
        e.[name]                                                AS EmployerName,
        e.taxid                                                  AS EIN,
        e.city                                                   AS City,
        e.[state]                                                AS State,
        e.zip                                                     AS Zip,
        f.FirmName                                                AS FirmName,
        CAST(ISNULL(ext.Priority,            0) AS BIT)           AS IsPriority,
        CAST(ISNULL(ext._noAutoRenewal,      0) AS BIT)           AS NoAutoRenewal,
        CAST(ISNULL(ext.SFTP,                0) AS BIT)           AS SFTP,
        CAST(ISNULL(ext._AnnualCOI,          0) AS BIT)           AS AnnualCOI,
        ext._fundingtype                                          AS FundingType,
        ext._FormType                                             AS FormType,
        ext._LevelofComplexity                                    AS LevelOfComplexity,
        ext._planyear                                             AS PlanYear,
        e.filingYear                                                AS FilingYear,
        am.Staff_FName + ' ' + am.Staff_LName                     AS AccountManagerName,
        da.Staff_FName + ' ' + da.Staff_LName                     AS DataAnalyst,
        COUNT(*) OVER ()                                          AS TotalCount
    FROM            [dbo].[Employer]                     e
    LEFT JOIN       [dbo].[Tracker_EmployerExtension]    ext  ON e.id = ext.EmployerId
    LEFT JOIN       [dbo].[Tracker_Firm]                 f    ON ext.firmid = f.FirmId
    LEFT JOIN       [dbo].[Tracker_Broker]               b    ON ext.brokerid = b.BrokerId
    LEFT JOIN       [dbo].[StaffAccounts]                am   ON ext.AcctManagerid = am.Staff_ID
    LEFT JOIN       [dbo].[StaffAccounts]                da   ON ext._dataAnalyst = da.Staff_ID
    LEFT JOIN       [dbo].[tbl_Medtracker_SalesRep]      sr   ON ext.srepid = sr.srepid
    WHERE
        -- %% Preserved exactly from original SP %%%%%%%%%%%%%%%%%%%%%%%%%%%%
        e.IsDeleted = 0

        -- %% Access (039): only the caller's employers, and affiliates of them %%
        AND (@EmployerIDs IS NULL
             OR e.id        IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@EmployerIDs, ','))
             OR e.companyId IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@EmployerIDs, ',')))

        -- %% Existing filters (preserved, converted to static) %
        AND (@SearchText IS NULL OR @SearchText = ''
                  OR e.[name] LIKE '%' + @SearchText + '%'
                  OR e.taxid   LIKE '%' + @SearchText + '%')
        AND (@FirmId           IS NULL OR ext.firmid           = @FirmId)
        AND (@BrokerId         IS NULL OR ext.brokerid         = @BrokerId)
        AND (@FundingType      IS NULL OR @FundingType = ''
                  OR ext._fundingtype = @FundingType)
        AND (@IsPriority       IS NULL OR ext.Priority         = @IsPriority)

        -- %% New filters (all nullable, no effect when NULL) %%%
        AND (@State                    IS NULL OR e.[state]                          = @State)
        AND (@FilingYear                IS NULL OR e.filingYear						 = @FilingYear)
        AND (@AcctManagerId             IS NULL OR ext.AcctManagerid                 = @AcctManagerId)
        AND (@DataAnalystId             IS NULL OR TRY_CAST(ext._dataAnalyst AS INT)  = @DataAnalystId)
        AND (@SalesRepId                IS NULL OR ext.srepid                        = @SalesRepId)
        AND (@FormType                  IS NULL OR ext._FormType                     = @FormType)
        AND (@LevelOfComplexity         IS NULL OR ext._LevelofComplexity            = @LevelOfComplexity)
        AND (@IndustryId                IS NULL OR ext._IndustryId                   = @IndustryId)
        AND (@NoAutoRenewal             IS NULL OR ext._noAutoRenewal                = @NoAutoRenewal)
        AND (@SFTP                      IS NULL OR ext.SFTP                          = @SFTP)
        AND (@AnnualCOI                 IS NULL OR ext._AnnualCOI                    = @AnnualCOI)
        -- If affiliate selected -> show only that affiliate
        -- If main selected (no affiliate) -> show main + all its affiliates
        -- If nothing selected -> show everything (existing companyId logic)
        AND (
            @AffiliateEmployerId IS NOT NULL AND e.id = @AffiliateEmployerId
            OR
            @AffiliateEmployerId IS NULL
            AND @MainEmployerId IS NOT NULL
            AND (e.id = @MainEmployerId OR e.companyId = @MainEmployerId)
            OR
            @AffiliateEmployerId IS NULL
            AND @MainEmployerId IS NULL
            AND (e.companyId IS NULL OR e.companyId = 0)
        )
    ORDER BY
        CASE WHEN @SortColumn = 'EmployerName'       AND @SortOrder = 'ASC'  THEN e.[name]                              END ASC,
        CASE WHEN @SortColumn = 'EmployerName'       AND @SortOrder = 'DESC' THEN e.[name]                              END DESC,
        CASE WHEN @SortColumn = 'FirmName'           AND @SortOrder = 'ASC'  THEN f.FirmName                           END ASC,
        CASE WHEN @SortColumn = 'FirmName'           AND @SortOrder = 'DESC' THEN f.FirmName                           END DESC,
        CASE WHEN @SortColumn = 'State'              AND @SortOrder = 'ASC'  THEN e.[state]                            END ASC,
        CASE WHEN @SortColumn = 'State'              AND @SortOrder = 'DESC' THEN e.[state]                            END DESC,
        CASE WHEN @SortColumn = 'AccountManagerName' AND @SortOrder = 'ASC'  THEN am.Staff_FName + ' ' + am.Staff_LName END ASC,
        CASE WHEN @SortColumn = 'AccountManagerName' AND @SortOrder = 'DESC' THEN am.Staff_FName + ' ' + am.Staff_LName END DESC,
        CASE WHEN @SortColumn = 'DataAnalyst'        AND @SortOrder = 'ASC'  THEN da.Staff_FName + ' ' + da.Staff_LName END ASC,
        CASE WHEN @SortColumn = 'DataAnalyst'      AND @SortOrder = 'DESC' THEN da.Staff_FName + ' ' + da.Staff_LName END DESC,
        e.[name] ASC -- tiebreaker

    OFFSET @Offset ROWS
    FETCH NEXT @PageSize ROWS ONLY;

END
GO

PRINT '043: sp_Tracker_GetEmployerList returns FilingYear.';
GO

/* ############################################################################
   044_employer_logins_from_tracker.sql
   ############################################################################ */
PRINT '=== 044_employer_logins_from_tracker.sql';
GO
/* =============================================================================
   044_employer_logins_from_tracker.sql

   Employer logins were linked two ways. Every employer-scope check, the File Upload
   list and Tracker's Portal login treat an Employer login's tbl_User.Ref_ID as the
   Employer it belongs to (Employer.id). User Accounts > Add, through
   sp_CreateUserAccounts, set Ref_ID to the id of a new EmployerAccounts profile row
   instead, so such a login reached whichever employer happened to share that number.

   Decided 2026-10-01: Employer logins are made only from Tracker > Employer > Portal
   login (which links them correctly). This script:
     1. sp_CreateUserAccounts refuses the Employer role (the web app refuses it too).
     2. sp_UserAccountsList lists every Employer login from the employer it points at
        (UserType 'EmployerPortal', named after the employer, read-only), instead of
        joining EmployerAccounts on Ref_ID, which matched profiles by coincidence and
        left Tracker-made logins out of the list.
   Otherwise both procedures are unchanged. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_UserAccountsList]
    @PageIndex INT = 1,
    @PageSize INT = 20,
    @Search VARCHAR(100) = NULL,
    @SortColumn INT = 0,
    @SortOrder VARCHAR(5) = 'asc',
    @RoleId int =0,
    @StatusId int =0
AS
BEGIN
    DECLARE @RecordCount INT;
    DECLARE @StartPage INT;
    DECLARE @EndPage INT;

    BEGIN TRY
        BEGIN TRANSACTION;
        SET NOCOUNT ON;

        -- Create temporary table to hold combined data
        CREATE TABLE #CombinedUsers
        (
            UserAccounts_ID INT,
            UserAccounts_FName NVARCHAR(100),
            UserAccounts_LName NVARCHAR(100),
            UserAccounts_Addr1 NVARCHAR(255),
            UserAccounts_Addr2 NVARCHAR(255),
            UserAccounts_City NVARCHAR(100),
            UserAccounts_State NVARCHAR(50),
            UserAccounts_Zip NVARCHAR(20),
            UserAccounts_Phone NVARCHAR(20),
            UserAccounts_Email NVARCHAR(100),
            UserAccounts_Status INT,
            CreatedOn DATETIME,
            CreatedBy INT,
            ModifiedOn DATETIME,
            ModifiedBy INT,
            UserType NVARCHAR(20),
            UserAccounts_Role INT,
            Temp_Password NVARCHAR(255),
            ProfilePicture NVARCHAR(MAX),
            UserAccounts_Role_Name NVARCHAR(100),
            UserAccounts_Full_Name NVARCHAR(201),
            AssginCount INT,
            IsMFA INT,
            LoginEnabled BIT
        );

        -------------------------------------------------------
        -- INSERT STAFF DATA
        -------------------------------------------------------
        INSERT INTO #CombinedUsers
        SELECT
            staf.Staff_ID AS UserAccounts_ID,
            staf.Staff_FName AS UserAccounts_FName,
            staf.Staff_LName AS UserAccounts_LName,
            staf.Staff_Addr1 AS UserAccounts_Addr1,
            staf.Staff_Addr2 AS UserAccounts_Addr2,
            staf.Staff_City AS UserAccounts_City,
            staf.Staff_State AS UserAccounts_State,
            staf.Staff_Zip AS UserAccounts_Zip,
            staf.Staff_Phone AS UserAccounts_Phone,
            staf.Staff_Email AS UserAccounts_Email,
            CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 2 ELSE staf.Staff_Status END AS UserAccounts_Status,
            staf.CreatedOn,
            staf.CreatedBy,
            staf.ModifiedOn,
            staf.ModifiedBy,
            'Staff' AS UserType,
            us.Role_ID,
            us.Temp_Password,
            us.Profile_Picture,
            roles.RoleName,
            --us.[User_ID],
            COALESCE(staf.Staff_FName, '') + ' ' + COALESCE(staf.Staff_LName, '') AS UserAccounts_Full_Name,
            CASE
        WHEN us.Role_ID = 16 OR us.Role_ID = 6 -- Account Manager
        THEN (SELECT COUNT(*) FROM tbl_Portfolio_Assignment WHERE AM_UserID = us.User_ID And IsActive = 1)

        WHEN us.Role_ID = 11 OR us.Role_ID = 5 -- Data Analyst
        THEN (SELECT COUNT(*) FROM tbl_Operational_Assignment WHERE DA_UserID = us.User_ID And IsActive = 1)

    ELSE 0
        END AS ExtraCount,
        us.isMFA,
        CAST(CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 0 ELSE 1 END AS BIT)
        FROM StaffAccounts staf
        INNER JOIN tbl_user us ON us.Ref_ID = staf.Staff_ID
        INNER JOIN tbl_roles roles ON roles.ID = us.Role_ID
        WHERE staf.Staff_Status != 4
          AND ISNULL(us.Role_ID, 0) NOT IN (10, 13);   -- each login only with its own account type, as sp_Login_access

        -------------------------------------------------------
        -- INSERT BROKER DATA
        -------------------------------------------------------
        INSERT INTO #CombinedUsers
        SELECT
            br.Broker_ID AS UserAccounts_ID,
           br.Broker_FName,
            br.Broker_LName,
            br.Broker_Addr1,
            br.Broker_Addr2,
            br.Broker_City,
            br.Broker_State,
            br.Broker_Zip,
            br.Broker_Phone,
            br.Broker_Email,
            CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 2 ELSE br.Broker_Status END,
            br.CreatedOn,
            br.CreatedBy,
            br.ModifiedOn,
            br.ModifiedBy,
           'Broker' AS UserType,
            us.Role_ID,
            us.Temp_Password,
            us.Profile_Picture,
            roles.RoleName,
            COALESCE(br.Broker_FName, '') + ' ' + COALESCE(br.Broker_LName, '') AS UserAccounts_Full_Name,
            (SELECT COUNT(*) FROM tbl_Employer_Broker_Assignment WHERE Broker_UserID = us.User_ID And IsActive = 1) AS ExtraCount,
            us.isMFA,
        CAST(CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 0 ELSE 1 END AS BIT)
        FROM BrokerAccounts br
        INNER JOIN tbl_user us ON us.Ref_ID = br.Broker_ID
        INNER JOIN tbl_roles roles ON roles.ID = us.Role_ID
        WHERE roles.ID = 10 and br.Broker_Status != 4;

        -------------------------------------------------------
        -- INSERT EMPLOYER LOGINS (044)
        -- An Employer login points at the employer it belongs to (Ref_ID = Employer.id) and
        -- is made and managed from Tracker > Employer > Portal login. Listed read-only, named
        -- after the employer; UserAccounts_ID is the employer id (the page links to Tracker).
        -------------------------------------------------------
        INSERT INTO #CombinedUsers
        SELECT
            e.id AS UserAccounts_ID,
            e.Name,
            '',
            NULL, NULL, NULL, NULL, NULL, NULL,
            us.[User_Name],
            CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 2 WHEN ISNULL(e.IsDeleted, 0) = 1 THEN 3 ELSE 1 END,
            us.CreatedOn,
            NULL,
            us.ModifiedOn,
            NULL,
            'EmployerPortal' AS UserType,
            us.Role_ID,
            us.Temp_Password,
            us.Profile_Picture,
            roles.RoleName,
            e.Name AS UserAccounts_Full_Name,
            NULL AS ExtraCount,
            us.isMFA,
            CAST(CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 0 ELSE 1 END AS BIT)
        FROM tbl_user us
        INNER JOIN dbo.Employer e ON e.id = us.Ref_ID
        INNER JOIN tbl_roles roles ON roles.ID = us.Role_ID
        WHERE us.Role_ID = 13;

        -------------------------------------------------------
        -- Get total record count with search filter
        -------------------------------------------------------
        SELECT @RecordCount = COUNT(*)
        FROM #CombinedUsers
        WHERE (@Search IS NULL
            OR UserAccounts_Full_Name LIKE '%' + @Search + '%'
               OR UserAccounts_Email LIKE '%' + @Search + '%')
               and(@RoleId =0 OR UserAccounts_Role = @RoleId)
               and(@StatusId =0 OR UserAccounts_Status = @StatusId);


        -- Calculate pagination values
        SET @StartPage = (@PageIndex - 1) * @PageSize + 1;
        SET @EndPage = @PageIndex * @PageSize;

        -------------------------------------------------------
        -- Main query with pagination and sorting
        -------------------------------------------------------
        SELECT
            UserAccounts_ID,
            UserAccounts_FName,
            UserAccounts_LName,
            UserAccounts_Addr1,
            UserAccounts_Addr2,
        UserAccounts_City,
            UserAccounts_State,
            UserAccounts_Zip,
            UserAccounts_Phone,
            UserAccounts_Email,
            UserAccounts_Status,
            CreatedOn,
            CreatedBy,
            ModifiedOn,
            ModifiedBy,
            UserType,
            UserAccounts_Role,
            Temp_Password,
            CASE
                WHEN ProfilePicture IS NOT NULL AND ProfilePicture != '' THEN ProfilePicture
                ELSE UPPER(
                    ISNULL(LEFT(NULLIF(UserAccounts_FName, ''), 1), '') +
                    ISNULL(LEFT(NULLIF(UserAccounts_LName, ''), 1), '')
                )
            END AS ProfilePicture,
            UserAccounts_Role_Name,
            UserAccounts_Full_Name,
            AssginCount,
            IsMFA,
            LoginEnabled
        FROM #CombinedUsers
        WHERE (@Search IS NULL
               OR UserAccounts_Full_Name LIKE '%' + @Search + '%'
               OR UserAccounts_Email LIKE '%' + @Search + '%')
               and(@RoleId =0 OR UserAccounts_Role = @RoleId)
               and(@StatusId =0 OR UserAccounts_Status = @StatusId)
        ORDER BY
            CASE WHEN (@SortColumn = 0 AND @SortOrder = 'asc') THEN UserAccounts_Full_Name END ASC,
            CASE WHEN (@SortColumn = 0 AND @SortOrder = 'desc') THEN UserAccounts_Full_Name END DESC,
            CASE WHEN (@SortColumn = 1 AND @SortOrder = 'asc') THEN UserAccounts_Email END ASC,
            CASE WHEN (@SortColumn = 1 AND @SortOrder = 'desc') THEN UserAccounts_Email END DESC,
            CASE WHEN (@SortColumn = 2 AND @SortOrder = 'asc') THEN CreatedOn END ASC,
            CASE WHEN (@SortColumn = 2 AND @SortOrder = 'desc') THEN CreatedOn END DESC
        OFFSET @PageSize * (@PageIndex - 1) ROWS
        FETCH NEXT @PageSize ROWS ONLY;

        -------------------------------------------------------
        -- Return pagination metadata
        -------------------------------------------------------
        SELECT
            @RecordCount as [RowCount],
            @StartPage as StartPage,
            CASE WHEN @EndPage > @RecordCount THEN @RecordCount ELSE @EndPage END as EndPage,
            @PageIndex as PageNumber,
            @RecordCount as TotalCount;




       -------------------------------------------------------
-- Summary: Count users by each role with sample details
-------------------------------------------------------
SELECT
    CU.UserAccounts_Role,
    R.RoleName,
    COUNT(*) AS TotalUsers,

    -- ALL users for this role
    STRING_AGG(CU.UserAccounts_Full_Name, ', ') AS AllUsers,

    -- ALL profile pictures for this role (using same initials logic)
    STRING_AGG(
        CASE
            WHEN CU.ProfilePicture IS NOT NULL AND CU.ProfilePicture != ''
                THEN CU.ProfilePicture
            ELSE UPPER(
                    ISNULL(LEFT(NULLIF(CU.UserAccounts_FName, ''), 1), '') +
                    ISNULL(LEFT(NULLIF(CU.UserAccounts_LName, ''), 1), '')
                 )
        END,
        ', '
    ) AS AllProfilePictures ,



  SUM(
    CASE
        WHEN CU.ProfilePicture IS NOT NULL AND CU.ProfilePicture != ''
            THEN 1   -- this is real picture
        WHEN CU.ProfilePicture IS NULL OR CU.ProfilePicture = ''
            THEN 1   -- this is initials avatar
        ELSE 0
    END
) AS TotalProfileCount




FROM #CombinedUsers CU
INNER JOIN tbl_roles R ON R.ID = CU.UserAccounts_Role
GROUP BY CU.UserAccounts_Role, R.RoleName
ORDER BY R.RoleName;


        -- Clean up temporary table
        DROP TABLE #CombinedUsers;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- Clean up temporary table if it exists
        IF OBJECT_ID('tempdb..#CombinedUsers') IS NOT NULL
            DROP TABLE #CombinedUsers;

        INSERT INTO dbo.DB_Errors
        VALUES
        (SUSER_SNAME(),
         ERROR_NUMBER(),
         ERROR_STATE(),
         ERROR_SEVERITY(),
         ERROR_LINE(),
         ERROR_PROCEDURE(),
         ERROR_MESSAGE(),
         GETDATE());

        IF (XACT_STATE()) = -1
            ROLLBACK TRANSACTION
        IF (XACT_STATE()) = 1
            COMMIT TRANSACTION

        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_CreateUserAccounts
    @UserAccounts_FName    NVARCHAR(100),
    @UserAccounts_LName    NVARCHAR(100),
    @UserAccounts_Addr1    NVARCHAR(100) = NULL,
    @UserAccounts_Addr2    NVARCHAR(50)  = NULL,
    @UserAccounts_City     NVARCHAR(50)  = NULL,
    @UserAccounts_State    BIGINT        = NULL,
    @UserAccounts_Zip      NVARCHAR(10)  = NULL,
    @UserAccounts_Phone    NVARCHAR(25)  = NULL,
    @UserAccounts_Email    NVARCHAR(100),
    @UserAccounts_Status   BIGINT        = NULL,
    @UserAccounts_Role     BIGINT        = NULL,
    @UserAccounts_Password NVARCHAR(100) = NULL,   -- BCrypt hash from the app
    @ProfilePicture        NVARCHAR(MAX) = NULL,
    @IsMFA                 INT           = NULL,
    @createdby             BIGINT        = NULL
AS
BEGIN
    -- 044: an Employer login must point at its employer (Ref_ID = Employer.id). This
    -- procedure would point it at a new EmployerAccounts row instead, whose id the app then
    -- reads as an employer id. Employer logins are made from Tracker > Employer > Portal login.
    IF (@UserAccounts_Role = 13)
        THROW 50044, 'Employer logins are created from Tracker (Employer > Portal login), not from User Accounts.', 1;
    SET NOCOUNT ON;
    DECLARE @UserAccounts_ID BIGINT;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@UserAccounts_Role = 10)
        BEGIN
            INSERT INTO dbo.BrokerAccounts (
                Broker_FName, Broker_LName, Broker_Addr1, Broker_Addr2,
                Broker_City, Broker_State, Broker_Zip, Broker_Phone,
                Broker_Email, Broker_Status, createdon, createdby)
            VALUES (
                @UserAccounts_FName, @UserAccounts_LName, @UserAccounts_Addr1, @UserAccounts_Addr2,
                @UserAccounts_City, @UserAccounts_State, @UserAccounts_Zip, @UserAccounts_Phone,
                @UserAccounts_Email, @UserAccounts_Status, GETDATE(), @createdby);
        END
        ELSE IF (@UserAccounts_Role = 13)
        BEGIN
            INSERT INTO dbo.EmployerAccounts (
                Employer_FName, Employer_LName, Employer_Addr1, Employer_Addr2,
                Employer_City, Employer_State, Employer_Zip, Employer_Phone,
                Employer_Email, Employer_Status, createdon, createdby)
            VALUES (
                @UserAccounts_FName, @UserAccounts_LName, @UserAccounts_Addr1, @UserAccounts_Addr2,
                @UserAccounts_City, @UserAccounts_State, @UserAccounts_Zip, @UserAccounts_Phone,
                @UserAccounts_Email, @UserAccounts_Status, GETDATE(), @createdby);
        END
        ELSE
        BEGIN
            INSERT INTO dbo.StaffAccounts (
                Staff_FName, Staff_LName, Staff_Addr1, Staff_Addr2,
                Staff_City, Staff_State, Staff_Zip, Staff_Phone,
                Staff_Email, Staff_Status, createdon, createdby)
            VALUES (
                @UserAccounts_FName, @UserAccounts_LName, @UserAccounts_Addr1, @UserAccounts_Addr2,
                @UserAccounts_City, @UserAccounts_State, @UserAccounts_Zip, @UserAccounts_Phone,
                @UserAccounts_Email, @UserAccounts_Status, GETDATE(), @createdby);
        END

        SET @UserAccounts_ID = SCOPE_IDENTITY();

        INSERT INTO dbo.tbl_User ([User_Name], Temp_Password, Ref_ID, Role_ID, Profile_Picture, isMFA)
        VALUES (@UserAccounts_Email, @UserAccounts_Password, @UserAccounts_ID, @UserAccounts_Role, @ProfilePicture, @IsMFA);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        INSERT INTO dbo.DB_Errors
        VALUES (SUSER_SNAME(), ERROR_NUMBER(), ERROR_STATE(), ERROR_SEVERITY(),
                ERROR_LINE(), ERROR_PROCEDURE(), ERROR_MESSAGE(), GETDATE());

        THROW;
    END CATCH
END
GO

PRINT '044: Employer logins are listed from their employer; User Accounts cannot create them.';
GO

/* ############################################################################
   047_broker_employer_scope.sql
   ############################################################################ */
PRINT '=== 047_broker_employer_scope.sql';
GO
/* =============================================================================
   047_broker_employer_scope.sql

   A Broker's employers were never looked up. User Accounts > Assign saves them in
   tbl_Employer_Broker_Assignment, and the dashboard and file rules already read it
   (fn_DashboardEmployerScope, fn_FileVisibleToUser), but the procedure the web app
   uses for a user's employers on every page returned nothing for a Broker, so a
   Broker could open none of the employers assigned to them.

   And the Select Employer list applied no filter for roles it did not name, so a
   Broker or an Employer login was shown every employer's name, EIN and contact
   (choosing one was refused).

     1. sp_GetEmployersByStaffId: a Broker gets its active Broker Assignments.
     2. sp_EmployersOnlyList: a Broker sees its assigned employers (any filing year of
        the same EIN, like Account Managers and Data Analysts); an Employer login sees
        its own employer; any other role that is not an all-employer role sees none.
   Otherwise both procedures are unchanged. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

-- =====================================================================================
-- sp_GetEmployersByStaffId - supervisor resolves like its manager role
--
-- Used at LOGIN (EmployerService.GetEmployersForUserAsync) for employer auto-select and
-- the "do you still have access?" check. The base proc only handled Account Manager and
-- Data Analyst; this adds the two supervisor roles, pointing them at the SAME shared
-- tables as the manager role (no separate AMS/DAS tables):
--
--   Account Manager + Account Manager Supervisor -> tbl_Portfolio_Assignment   (AM_UserID)
--   Data Analyst    + DA Supervisor              -> tbl_Operational_Assignment (DA_UserID)
--
-- @StaffId is the logged-in tbl_User.User_ID. Safe to re-run.
-- =====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[sp_GetEmployersByStaffId]
    @StaffId  INT,
    @RoleName NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    -- Data Analyst + DA Supervisor -> Operational Assignments
    IF @RoleName IN ('Data Analyst', 'DA Supervisor')
        SELECT e.id AS Id, e.filingYear AS FilingYear, e.companyId AS CompanyId,
               e.taxid AS TaxId, e.name AS Name, 1 AS IsAssigned
        FROM   employer e
        INNER JOIN tbl_Operational_Assignment oa ON e.id = oa.EmployerID
        WHERE  oa.DA_UserID = @StaffId AND oa.IsActive = 1
        ORDER BY e.name;

    -- Account Manager + Account Manager Supervisor -> Portfolio Assignments
    ELSE IF @RoleName IN ('Account Manager', 'Account Manager Supervisor')
        SELECT e.id AS Id, e.filingYear AS FilingYear, e.companyId AS CompanyId,
               e.taxid AS TaxId, e.name AS Name, 1 AS IsAssigned
        FROM   employer e
        INNER JOIN tbl_Portfolio_Assignment pa ON e.id = pa.EmployerID
        WHERE  pa.AM_UserID = @StaffId AND pa.IsActive = 1
        ORDER BY e.name;

    -- Broker -> Broker Assignments (User Accounts > Assign), as fn_DashboardEmployerScope (047)
    ELSE IF @RoleName = 'Broker'
        SELECT e.id AS Id, e.filingYear AS FilingYear, e.companyId AS CompanyId,
               e.taxid AS TaxId, e.name AS Name, 1 AS IsAssigned
        FROM   employer e
        INNER JOIN tbl_Employer_Broker_Assignment ba ON e.id = ba.EmployerID
        WHERE  ba.Broker_UserID = @StaffId AND ba.IsActive = 1 AND ba.UnassignedDate IS NULL
        ORDER BY e.name;

    -- Any other role -> no assignment-based employers
    ELSE
        SELECT TOP 0 id AS Id FROM employer;
END
GO

-- =====================================================================================
-- sp_EmployersOnlyList - role-based Select Employer list (cross-year by EIN)
--
-- The ONLY database change needed for the role-based Select Employer list. Pair it with
-- the 3 code edits (IEmployerService / EmployerService.GetEmployersListViewAsync +
-- SelectEmployerController Index & SearchEmployers) that pass @UserId and @RoleName.
--
-- Filtering (supervisor shares the manager's table):
--   Account Manager + Account Manager Supervisor -> tbl_Portfolio_Assignment   (AM_UserID)
--   Data Analyst    + DA Supervisor              -> tbl_Operational_Assignment (DA_UserID)
--   Everyone else (Admin, Director, etc.)        -> full employer list (unchanged)
--
-- CROSS-YEAR: employers are separate rows per filing year, linked by taxid (EIN). An
-- assignment points at one year's row, but the user must be able to open PREVIOUS/OTHER
-- years for the SAME company. So we allow an employer when:
--     (a) its id is directly assigned to the user, OR
--     (b) its taxid (EIN) matches the taxid of any employer assigned to the user.
-- (a) still covers assigned rows that have a NULL/blank EIN (test data).
--
-- @UserId  = logged-in tbl_User.User_ID   (GetCurrentUserId)
-- @RoleName = logged-in role name          (ClaimTypes.Role)
--
-- No new tables/types. Uses the existing assignment tables. Safe to re-run.
-- =====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[sp_EmployersOnlyList]
(
    @search       NVARCHAR(100) = NULL,
    @PageSize     INT           = NULL,
    @PageIndex    INT           = NULL,
    @fillingyear  INT           = NULL,
    @SortColumn   INT           = NULL,
    @SortOrder    VARCHAR(5)    = NULL,
    @TypeFilter   NVARCHAR(20)  = NULL,
    @UserId       INT           = NULL,   -- logged-in tbl_User.User_ID
    @RoleName     NVARCHAR(100) = NULL    -- logged-in role name
)
AS
BEGIN TRY
    BEGIN TRANSACTION
    SET NOCOUNT ON;

    -- ------------------------------------------------------------------
    -- Resolve, ONCE, what this user is allowed to see (restricted roles only).
    -- @Restricted = 1 means "apply the assignment filter".
    -- ------------------------------------------------------------------
    DECLARE @Restricted BIT = 0;
    DECLARE @AssignedIds    TABLE (id INT PRIMARY KEY);
    DECLARE @AssignedTaxids TABLE (taxid VARCHAR(50));

    IF @UserId IS NOT NULL AND @RoleName IN ('Account Manager','Account Manager Supervisor')
    BEGIN
        SET @Restricted = 1;
        INSERT INTO @AssignedIds (id)
            SELECT EmployerID FROM tbl_Portfolio_Assignment WHERE AM_UserID = @UserId AND IsActive = 1;
        INSERT INTO @AssignedTaxids (taxid)
            SELECT DISTINCT a.taxid
            FROM tbl_Portfolio_Assignment pa JOIN Employer a ON a.id = pa.EmployerID
            WHERE pa.AM_UserID = @UserId AND pa.IsActive = 1 AND ISNULL(a.taxid,'') <> '';
    END
    ELSE IF @UserId IS NOT NULL AND @RoleName IN ('Data Analyst','DA Supervisor')
    BEGIN
        SET @Restricted = 1;
        INSERT INTO @AssignedIds (id)
            SELECT EmployerID FROM tbl_Operational_Assignment WHERE DA_UserID = @UserId AND IsActive = 1;
        INSERT INTO @AssignedTaxids (taxid)
            SELECT DISTINCT a.taxid
            FROM tbl_Operational_Assignment oa JOIN Employer a ON a.id = oa.EmployerID
            WHERE oa.DA_UserID = @UserId AND oa.IsActive = 1 AND ISNULL(a.taxid,'') <> '';
    END

    ELSE IF @UserId IS NOT NULL AND @RoleName = 'Broker'
    BEGIN
        -- 047: a Broker's book is its Broker Assignments (User Accounts > Assign).
        SET @Restricted = 1;
        INSERT INTO @AssignedIds (id)
            SELECT DISTINCT EmployerID FROM tbl_Employer_Broker_Assignment
            WHERE Broker_UserID = @UserId AND IsActive = 1 AND UnassignedDate IS NULL;
        INSERT INTO @AssignedTaxids (taxid)
            SELECT DISTINCT a.taxid
            FROM tbl_Employer_Broker_Assignment ba JOIN Employer a ON a.id = ba.EmployerID
            WHERE ba.Broker_UserID = @UserId AND ba.IsActive = 1 AND ba.UnassignedDate IS NULL
              AND ISNULL(a.taxid,'') <> '';
    END
    ELSE IF @UserId IS NOT NULL AND @RoleName = 'Employer'
    BEGIN
        -- 047: an Employer login sees only the employer it points at (tbl_User.Ref_ID = Employer.id).
        SET @Restricted = 1;
        INSERT INTO @AssignedIds (id)
            SELECT a.id FROM tbl_User u JOIN Employer a ON a.id = u.Ref_ID
            WHERE u.User_ID = @UserId AND u.Role_ID = 13;
    END
    ELSE IF ISNULL(@RoleName, '') NOT IN ('SuperAdmin', 'Admin', 'ACA Director', 'StaffAC', 'StaffAU')
        -- 047: any other role (or none) sees no employers here. This list used to apply
        -- no filter at all for roles it did not name, so a Broker or Employer login was
        -- shown every employer (selecting one was refused, but the list itself leaked).
        SET @Restricted = 1;

    DECLARE @RecordCount INT;

    SELECT @RecordCount = COUNT(*)
    FROM Employer AS emp
    WHERE emp.filingYear = @fillingyear
      AND emp.name LIKE '%' + ISNULL(@search, emp.name) + '%'
      AND (@TypeFilter IS NULL
           OR (@TypeFilter = 'Main'      AND emp.companyId IS NULL)
           OR (@TypeFilter = 'Affiliate' AND emp.companyId IS NOT NULL))
      AND (
            @Restricted = 0
            OR emp.id    IN (SELECT id FROM @AssignedIds)
            OR emp.taxid IN (SELECT taxid FROM @AssignedTaxids)
          );

    IF @RecordCount = 0
    BEGIN
        SELECT CAST(NULL AS INT) AS id, CAST(NULL AS INT) AS filingYear, CAST(NULL AS INT) AS companyId,
               CAST(NULL AS VARCHAR(50)) AS taxid, CAST(NULL AS VARCHAR(100)) AS name,
               CAST(NULL AS VARCHAR(100)) AS contactName, CAST(NULL AS VARCHAR(15)) AS phoneNumber,
               CAST(NULL AS VARCHAR(10)) AS [Type], 0 AS TotalCount;
        SELECT 0 AS [RowCount], 0 AS StartPage, 0 AS EndPage, @PageIndex AS PageNumber;
        COMMIT TRANSACTION; RETURN;
    END

    DECLARE @StartPage INT = (@PageIndex - 1) * @PageSize + 1;
    DECLARE @EndPage   INT = @PageIndex * @PageSize;

    SELECT emp.id, emp.filingYear, emp.companyId, emp.taxid, emp.name, emp.contactName, emp.phoneNumber,
           CASE WHEN emp.companyId IS NULL THEN 'Main' ELSE 'Affiliate' END AS [Type],
           @RecordCount AS TotalCount
    FROM Employer AS emp
    WHERE emp.filingYear = @fillingyear
      AND emp.name LIKE '%' + ISNULL(@search, emp.name) + '%'
      AND (@TypeFilter IS NULL
           OR (@TypeFilter = 'Main'      AND emp.companyId IS NULL)
           OR (@TypeFilter = 'Affiliate' AND emp.companyId IS NOT NULL))
      AND (
            @Restricted = 0
            OR emp.id    IN (SELECT id FROM @AssignedIds)
            OR emp.taxid IN (SELECT taxid FROM @AssignedTaxids)
          )
    ORDER BY
        CASE WHEN @SortColumn = 0 AND @SortOrder = 'asc'  THEN emp.name  END ASC,
        CASE WHEN @SortColumn = 0 AND @SortOrder = 'desc' THEN emp.name  END DESC,
        CASE WHEN @SortColumn = 1 AND @SortOrder = 'asc'  THEN emp.taxid END ASC,
        CASE WHEN @SortColumn = 1 AND @SortOrder = 'desc' THEN emp.taxid END DESC,
        emp.name
    OFFSET @PageSize * (@PageIndex - 1) ROWS FETCH NEXT @PageSize ROWS ONLY;

    SELECT @RecordCount AS [RowCount], @StartPage AS StartPage,
           CASE WHEN @EndPage > @RecordCount THEN @RecordCount ELSE @EndPage END AS EndPage,
           @PageIndex AS PageNumber;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO dbo.DB_Errors
    VALUES (SUSER_SNAME(), ERROR_NUMBER(), ERROR_STATE(), ERROR_SEVERITY(),
            ERROR_LINE(), ERROR_PROCEDURE(), ERROR_MESSAGE(), GETDATE());
    THROW;
END CATCH
GO

PRINT '047: Brokers reach their assigned employers; the Select Employer list is scoped for every role.';
GO

/* ############################################################################
   048_irs_and_import_fixes.sql
   ############################################################################ */
PRINT '=== 048_irs_and_import_fixes.sql';
GO
/* =============================================================================
   048_irs_and_import_fixes.sql

   1. sp_ImportData_FromStaging: an Employer row with a blank Affiliated EIN (the
      normal way to describe the primary employer only) passed validation with a
      warning, then stopped the import: step 3 copied it into
      Employer_AggregatedGroupMembers, whose MemberEIN does not allow NULL. Rows
      without an Affiliated EIN are now left out of the affiliate employers (step 2)
      and of the aggregated group members (step 3). Nothing else changes.

   2. dbo.TransmitterConfig gets the transmitter's mailing address (AddressLine1,
      City, State, ZipCode). The IRS manifest (ACATransmitterManifestReqDtl /
      CompanyInformationGrp) requires it. Set it once for the active row, e.g.
        UPDATE dbo.TransmitterConfig SET AddressLine1 = '...', City = '...',
               State = 'NJ', ZipCode = '07001' WHERE IsActive = 1;
      Until it is set, generating IRS files stops with a message saying so.

   3. dbo.sp_GetEmployerFormCount is dropped: nothing calls it (the web app, the
      other procedures). Its definition is saved first in
      dbo.tbl_DroppedProcedures_Backup_033, as 033 does.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

-- 1. Import: rows without an Affiliated EIN ------------------------------------
/* =============================================================================
   PART 1 -- sp_ImportData_FromStaging: resolve an employee's plan anywhere in
             the group, not only under their own employer record.
   ============================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_ImportData_FromStaging]  
    @FileLogId INT,  
    @ImportMode VARCHAR(20) = 'Merge'   
AS  
BEGIN  
    SET NOCOUNT ON;  
    DECLARE @TranStarted BIT = 0;  
  
    IF @@TRANCOUNT = 0  
    BEGIN  
        BEGIN TRANSACTION;  
        SET @TranStarted = 1;  
    END  
  
    BEGIN TRY  
        DECLARE      
            @PlanYear INT,      
            @PrimaryEIN VARCHAR(15),  
            @ParentEmployerId INT,  
            @ParentNameFromLog NVARCHAR(255);      
  
        SELECT      
            @PlanYear   = PlanYear,      
            @PrimaryEIN = LTRIM(RTRIM(TaxID)),  
            @ParentNameFromLog = EmployerName  
        FROM UploadedFileLog      
        WHERE FileLogId = @FileLogId;      
  
        IF @PrimaryEIN IS NULL OR @PrimaryEIN = ''  
        BEGIN  
            SELECT TOP 1 @PrimaryEIN = LTRIM(RTRIM(PrimaryEIN))  
            FROM Staging_Employers   
            WHERE FileLogId = @FileLogId AND PrimaryEIN IS NOT NULL AND LTRIM(RTRIM(PrimaryEIN)) <> '';  
              
            IF @PrimaryEIN IS NULL OR @PrimaryEIN = ''  
            BEGIN  
                SELECT TOP 1 @PrimaryEIN = LTRIM(RTRIM(AffiliatedEIN))  
                FROM Staging_Employers   
                WHERE FileLogId = @FileLogId AND AffiliatedEIN IS NOT NULL AND LTRIM(RTRIM(AffiliatedEIN)) <> '';  
            END  
        END  
  
        IF @PrimaryEIN IS NULL OR @PrimaryEIN = ''  
        BEGIN  
            THROW 50011, 'Critical Error: Primary EIN is missing. Import aborted.', 1;  
        END  
  
        IF @PlanYear IS NULL OR @PlanYear = 0  
        BEGIN  
            SELECT TOP 1 @PlanYear = YEAR(StartDate) FROM Staging_Premiums WHERE FileLogId = @FileLogId AND StartDate IS NOT NULL;  
            IF @PlanYear IS NULL  
                SELECT TOP 1 @PlanYear = YEAR(StatusStartDate) FROM Staging_Employees WHERE FileLogId = @FileLogId AND StatusStartDate IS NOT NULL;  
        END  
        SET @PlanYear = ISNULL(@PlanYear, YEAR(GETDATE()));  
  
        UPDATE UploadedFileLog SET TaxID = @PrimaryEIN, PlanYear = @PlanYear WHERE FileLogId = @FileLogId;  
  
        /* 1. ESTABLISH PARENT EMPLOYER */   
        MERGE Employer AS T  
        USING (  
            SELECT TOP 1   
                PrimaryEIN as taxid, EmployerName, Address, Address2, City, StateOrProvince, Zip, Contact, Phone, Title  
            FROM Staging_Employers   
            WHERE FileLogId = @FileLogId AND PrimaryEIN = @PrimaryEIN  
        ) AS S  
        ON T.taxid = S.taxid AND T.filingYear = @PlanYear  
        WHEN MATCHED THEN   
            UPDATE SET   
                T.name = ISNULL(S.EmployerName, T.name),  
                T.address = ISNULL(S.Address, T.address),  
                T.address2 = ISNULL(S.Address2, T.address2),  
                T.city = ISNULL(S.City, T.city),  
                T.state = ISNULL(S.StateOrProvince, T.state),  
                T.zip = ISNULL(S.Zip, T.zip),  
                T.contactName = ISNULL(S.Contact, T.contactName),  
                T.phoneNumber = ISNULL(S.Phone, T.phoneNumber),  
                T.signTitle = ISNULL(S.Title, T.signTitle),  
                T.FileLogId = @FileLogId,  
                T.companyId = NULL  
        WHEN NOT MATCHED THEN  
            INSERT (FileLogId, taxid, name, filingYear, address, address2, city, state, zip, contactName, phoneNumber, signTitle, companyId, isAuthoritative)  
            VALUES (@FileLogId, @PrimaryEIN, S.EmployerName, @PlanYear, S.Address, S.Address2, S.City, S.StateOrProvince, S.Zip, S.Contact, S.Phone, S.Title, NULL, 1);  
  
        SELECT @ParentEmployerId = id FROM Employer WHERE taxid = @PrimaryEIN AND filingYear = @PlanYear;  
  
        IF @ParentEmployerId IS NULL  
            THROW 50010, 'Critical Error: Could not establish ParentEmployerId. Import aborted.', 1;  
  
        /* 0.5. OVERWRITE CLEANUP */   
        IF @ImportMode = 'Overwrite'  
        BEGIN  
            DECLARE @TargetEmployeeCodes TABLE (Id INT);  
            INSERT INTO @TargetEmployeeCodes (Id) SELECT id FROM EmployeeCode WHERE employerId = @ParentEmployerId AND filingYear = @PlanYear;  
  
            DELETE FROM EmployeeEnrollment WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM EmployeePayroll WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM CoveredIndividual WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM EmployeeHireSpan WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM Premium WHERE EmployerId = @ParentEmployerId;  
            DELETE FROM Employer_AggregatedGroupMembers WHERE EmployerId = @ParentEmployerId;  
        END  
  
        /* 2. AFFILIATE EMPLOYERS (FIXED DUPLICATE CRASH) */   
        MERGE Employer AS T      
        USING (      
            SELECT AffiliatedEIN, EmployerName, Address, Address2, City, StateOrProvince, Zip, Contact, Phone, Title
            FROM (
                SELECT AffiliatedEIN, EmployerName, Address, Address2, City, StateOrProvince, Zip, Contact, Phone, Title,
                       ROW_NUMBER() OVER(PARTITION BY AffiliatedEIN ORDER BY (SELECT NULL)) as rn
                FROM Staging_Employers
                WHERE FileLogId = @FileLogId AND IsValid = 1 AND AffiliatedEIN <> @PrimaryEIN
                  AND NULLIF(LTRIM(RTRIM(AffiliatedEIN)), '') IS NOT NULL   -- 048: a row without an affiliate EIN is not an affiliate
            ) Sub WHERE rn = 1
        ) AS S      
        ON T.taxid = S.AffiliatedEIN AND T.filingYear = @PlanYear      
        WHEN MATCHED THEN      
            UPDATE SET   
                name = S.EmployerName, address = S.Address, address2 = S.Address2, city = S.City, state = S.StateOrProvince, zip = S.Zip,  
                companyId = @ParentEmployerId, FileLogId = @FileLogId      
        WHEN NOT MATCHED THEN      
            INSERT (FileLogId, taxid, name, filingYear, address, address2, city, state, zip, contactName, phoneNumber, signTitle, companyId, isAuthoritative)      
            VALUES (@FileLogId, S.AffiliatedEIN, S.EmployerName, @PlanYear, S.Address, S.Address2, S.City, S.StateOrProvince, S.Zip, S.Contact, S.Phone, S.Title, @ParentEmployerId, 0);      
        
       /* 2.5. TRACKER EXTENSION HEALING (FIXED) */   
        INSERT INTO [dbo].[Tracker_EmployerExtension] (EmployerId)
        SELECT DISTINCT E.id
        FROM [dbo].[Employer] E
        LEFT JOIN [dbo].[Tracker_EmployerExtension] EE ON E.id = EE.EmployerId
        WHERE (E.FileLogId = @FileLogId OR E.id = @ParentEmployerId)
          AND EE.EmployerId IS NULL;
  
        /* 3. AGGREGATED GROUP MEMBERS */      
        INSERT INTO Employer_AggregatedGroupMembers (EmployerId, MemberName, MemberEIN)      
        SELECT DISTINCT @ParentEmployerId, S.EmployerName, S.AffiliatedEIN      
        FROM Staging_Employers S      
        WHERE S.FileLogId = @FileLogId      
          AND NULLIF(LTRIM(RTRIM(S.AffiliatedEIN)), '') IS NOT NULL   -- 048: MemberEIN is NOT NULL; a blank one stopped the import
          AND NOT EXISTS (SELECT 1 FROM Employer_AggregatedGroupMembers AG WHERE AG.MemberEIN = S.AffiliatedEIN AND AG.EmployerId = @ParentEmployerId);      
  
        /* 4. EMPLOYEES (FIXED DUPLICATE CRASH) */      
        MERGE Employee AS T      
        USING (      
            SELECT * FROM (
                SELECT      
                    S.EmployeeSSN, S.EmployeeLegalFirstName, S.EmployeeLegalLastName, S.EmployeeMiddleInitial, S.EmployeeSuffix,  
                    S.Address1, S.Address2, S.City, S.StateOrProvince, S.Zip, S.Country,  
                    S.EmailAddress,  
                    S.CoverageElected,   
                    S.LowestCostPlanOffered,  
                    TRY_CAST(S.CoverageStartDate AS DATE) AS CoverageStartDate,  
                    TRY_CAST(S.CoverageEndDate AS DATE) AS CoverageEndDate,  
                    TRY_CAST(S.DateEligibleForCoverage AS DATE) AS DateEligibleForCoverage,  
                    TRY_CAST(S.EmployeeBirthdate AS DATE) AS BirthDate,      
                    TRY_CAST(S.HireDate AS DATE) AS HireDate,      
                    TRY_CAST(S.TerminationDate AS DATE) AS TerminationDate,      
                    TRY_CAST(S.Status AS INT) AS Status,      
                    COALESCE(E.id, @ParentEmployerId) AS FinalEmployerId,
                    ROW_NUMBER() OVER(PARTITION BY S.EmployeeSSN, COALESCE(E.id, @ParentEmployerId) ORDER BY ISNULL(TRY_CAST(S.StatusStartDate AS DATE), TRY_CAST(S.HireDate AS DATE)) DESC) as rn
                FROM Staging_Employees S      
                LEFT JOIN Employer E ON S.EINAssociatedWithEE = E.taxid AND E.filingYear = @PlanYear      
                WHERE S.FileLogId = @FileLogId AND S.IsValid = 1  
            ) Sub WHERE rn = 1
        ) AS S      
        ON T.ssn = S.EmployeeSSN AND T.EmployerId = S.FinalEmployerId      
        WHEN MATCHED THEN      
            UPDATE SET   
                firstName = S.EmployeeLegalFirstName,   
                lastName = S.EmployeeLegalLastName,   
                middleName = S.EmployeeMiddleInitial,   
                suffix = S.EmployeeSuffix,  
                address = S.Address1,   
                address2 = S.Address2,   
                city = S.City,   
                state = S.StateOrProvince,   
                zip = S.Zip,  
                birthday = S.BirthDate,   
                HireDate = S.HireDate,   
                TerminationDate = S.TerminationDate,   
                Status = S.Status,  
                CoverageElected = S.CoverageElected,  
                CoverageStartDate = S.CoverageStartDate,  
                CoverageEndDate = S.CoverageEndDate,  
                LowestCostPlanOffered = S.LowestCostPlanOffered,  
                DateEligibleForCoverage = S.DateEligibleForCoverage,  
                email = S.EmailAddress,  
                IsDeleted = 0      
        WHEN NOT MATCHED THEN      
            INSERT (  
                FileLogId, EmployerId, fullName, firstName, lastName, middleName, suffix,   
                ssn, address, address2, city, state, zip, birthday, HireDate, TerminationDate,   
                Status, CoverageElected, CoverageStartDate, CoverageEndDate,   
                LowestCostPlanOffered, DateEligibleForCoverage, email, IsDeleted  
            )      
            VALUES (  
                @FileLogId, S.FinalEmployerId, S.EmployeeLegalFirstName + ' ' + S.EmployeeLegalLastName,   
                S.EmployeeLegalFirstName, S.EmployeeLegalLastName, S.EmployeeMiddleInitial, S.EmployeeSuffix,   
                S.EmployeeSSN, S.Address1, S.Address2, S.City, S.StateOrProvince, S.Zip,   
                S.BirthDate, S.HireDate, S.TerminationDate, S.Status,   
                S.CoverageElected, S.CoverageStartDate, S.CoverageEndDate,   
                S.LowestCostPlanOffered, S.DateEligibleForCoverage, S.EmailAddress, 0  
            );  

        /* 5. EMPLOYEE CODES & HIRE SPANS (FIXED MULTI-SPAN ISSUE) */      
        INSERT INTO EmployeeCode (employeeId, employerId, filingYear, getForm, isCorrected, isVoid, disableCoding)      
        SELECT E.id, E.EmployerId, @PlanYear, 1, 0, 0, 0      
        FROM Employee E WHERE E.FileLogId = @FileLogId      
        AND NOT EXISTS (SELECT 1 FROM EmployeeCode EC WHERE EC.employeeId = E.id AND EC.filingYear = @PlanYear);      
  
               INSERT INTO EmployeeHireSpan (employeeCodeId, startDate, endDate)
        SELECT DISTINCT EC.id, TRY_CAST(S.HireDate AS DATE), TRY_CAST(S.TerminationDate AS DATE)
        FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON S.EmployeeSSN = E.ssn AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1 AND S.HireDate IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM EmployeeHireSpan HS WHERE HS.employeeCodeId = EC.id AND HS.startDate = TRY_CAST(S.HireDate AS DATE));
  
        /* 5b. EMPLOYEE STATUS PERIODS (one row per distinct status span) -- FIX 1 (NEW) */
               INSERT INTO EmployeeStatus (employeeCodeId, status, startDate, endDate)
        SELECT DISTINCT EC.id, TRY_CAST(S.Status AS INT), TRY_CAST(S.StatusStartDate AS DATE), TRY_CAST(S.StatusEndDate AS DATE)
        FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON E.ssn = S.EmployeeSSN AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1
          AND TRY_CAST(S.StatusStartDate AS DATE) IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM EmployeeStatus ST WHERE ST.employeeCodeId = EC.id
                AND ST.startDate = TRY_CAST(S.StatusStartDate AS DATE)
                AND ISNULL(ST.status,-1) = ISNULL(TRY_CAST(S.Status AS INT),-1));
  
  
        /* 6. PLANS & PREMIUMS */      
        MERGE EmployerPlan AS T      
        USING (  
            SELECT DISTINCT   
                SP.PlanName,   
                E.id AS EmpId,   
                CASE WHEN LTRIM(RTRIM(SP.PlanType)) = 'Medical' THEN 1 ELSE 0 END AS PlanTypeInt,  
                TRY_CAST(SP.OfferedToSpouse AS SMALLINT) AS OfferedToSpouseInt,   
                TRY_CAST(SP.OfferedToDependents AS SMALLINT) AS OfferedToDependentsInt,  
                SP.MinimumValue,   
                SP.FundingType,   
                SP.BandingType,   
                SP.PlanRenewalMonth,   
                SP.WaitingPeriodNumberOfDays,  
                SP.EligibleFirstOfTheMonth,  
                SP.PlanTerminatesOnDateOfTermination  
            FROM Staging_Plans SP   
            JOIN Employer E ON SP.PrimaryEIN = E.taxid AND E.filingYear = @PlanYear    
            WHERE SP.FileLogId = @FileLogId AND SP.IsValid = 1  
        ) AS S      
        ON T.employerId = S.EmpId AND T.name = S.PlanName      
        WHEN MATCHED THEN   
            UPDATE SET FileLogId = @FileLogId,    
                medicalPlan = S.PlanTypeInt,  
                offeredSpouse = S.OfferedToSpouseInt,   
                offeredDependents = S.OfferedToDependentsInt,  
                minimumValue = S.MinimumValue,   
                fundingType = S.FundingType,   
                bandingType = S.BandingType,   
                planRenewal = S.PlanRenewalMonth,   
                waitingDays = S.WaitingPeriodNumberOfDays,  
                eligibile1stOfMonth = S.EligibleFirstOfTheMonth,  
                planTermTermination = S.PlanTerminatesOnDateOfTermination  
        WHEN NOT MATCHED THEN   
            INSERT (  
                FileLogId, employerId, name, medicalPlan,   
                offeredSpouse, offeredDependents,   
                minimumValue, fundingType, bandingType, planRenewal, waitingDays,   
                eligibile1stOfMonth, planTermTermination,   
                code1A, code2F, code2G, code2H   
            )   
            VALUES (  
                @FileLogId, S.EmpId, S.PlanName, S.PlanTypeInt,  
                S.OfferedToSpouseInt, S.OfferedToDependentsInt,  
                S.MinimumValue, S.FundingType, S.BandingType, S.PlanRenewalMonth, S.WaitingPeriodNumberOfDays,   
                S.EligibleFirstOfTheMonth, S.PlanTerminatesOnDateOfTermination,   
                0, 1, 1, 1  
            );  
  
        /* 7. PREMIUMS */  
        INSERT INTO Premium (EmployerPlanId, bandingValueStart, bandingValueEnd, bandingStartDate, bandingEndDate, amount, EmployerId, FileLogId)      
        SELECT   
            EP.id,   
            ISNULL(NULLIF(LTRIM(RTRIM(SP.[Start])), ''), SP.BandingType),   
            ISNULL(NULLIF(LTRIM(RTRIM(SP.[End])),   ''), SP.BandingType),   
            SP.StartDate,   
            SP.EndDate,   
            SP.EEMonthlyContribution,   
            EP.employerId,   
            @FileLogId   
        FROM Staging_Premiums SP   
        JOIN EmployerPlan EP ON   
            REPLACE(LTRIM(RTRIM(EP.name)), CHAR(160), '') = REPLACE(LTRIM(RTRIM(SP.PlanName)), CHAR(160), '')  
            AND EP.FileLogId = @FileLogId   
        WHERE SP.FileLogId = @FileLogId   
            AND SP.IsValid = 1   
            AND NOT EXISTS (  
                SELECT 1 FROM Premium P   
                WHERE P.EmployerPlanId = EP.id   
                AND P.bandingStartDate = SP.StartDate   
                AND P.amount = SP.EEMonthlyContribution  
            );  
  
        /* 8. ENROLLMENT & PAYROLL & DEPENDENTS */      
        INSERT INTO EmployeeEnrollment
            (employeeCodeId, planId, isEnrolled, coverageOfferDate, coverageStartDate, coverageEndDate,
             unionMember, contributionStartDate, contributionEndDate,
             COBRAEnrolled, COBRAStartDate, COBRAEndDate, RetireeEnrolled, RetireeStartDate, RetireeEndDate)   -- FIX 2
        SELECT DISTINCT
            EC.id, EP.id, ISNULL(TRY_CAST(S.CoverageElected AS SMALLINT),0),
            TRY_CAST(S.DateEligibleForCoverage AS DATE), TRY_CAST(S.CoverageStartDate AS DATE), TRY_CAST(S.CoverageEndDate AS DATE),
            ISNULL(TRY_CAST(S.UnionEmployee AS SMALLINT),0), TRY_CAST(S.DateEmployerFirstPaysToUnionBenefits AS DATE), TRY_CAST(S.DateEmployerLastPaysToUnionBenefits AS DATE),
            ISNULL(TRY_CAST(S.COBRABenefitsElected AS SMALLINT),0), TRY_CAST(S.COBRACoverageEffectiveDate AS DATE), TRY_CAST(S.COBRACoverageEndDate AS DATE),
            ISNULL(TRY_CAST(S.RetireeBenefitsElected AS SMALLINT),0), TRY_CAST(S.RetireePlanStartDate AS DATE), TRY_CAST(S.RetireePlanEndDate AS DATE)
                FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON E.ssn = S.EmployeeSSN AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        LEFT JOIN EmployerPlan EP ON REPLACE(LTRIM(RTRIM(EP.name)),CHAR(160),'') = REPLACE(LTRIM(RTRIM(S.LowestCostPlanOffered)),CHAR(160),'') AND EP.employerId IN (SELECT g.id FROM Employer g WHERE g.id = EC.employerId OR g.id = (SELECT p2.companyId FROM Employer p2 WHERE p2.id = EC.employerId))
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1
            AND ( S.LowestCostPlanOffered IS NOT NULL
                 OR ISNULL(TRY_CAST(S.UnionEmployee AS SMALLINT),0)=1
                 OR ISNULL(TRY_CAST(S.COBRABenefitsElected AS SMALLINT),0)=1
                 OR ISNULL(TRY_CAST(S.RetireeBenefitsElected AS SMALLINT),0)=1 )
            AND NOT EXISTS (SELECT 1 FROM EmployeeEnrollment EE WHERE EE.employeeCodeId = EC.id
                    AND ISNULL(EE.planId,-1) = ISNULL(EP.id,-1)
                    AND ISNULL(EE.coverageStartDate,'1900-01-01') = ISNULL(TRY_CAST(S.CoverageStartDate AS DATE),'1900-01-01')
                    AND ISNULL(EE.coverageOfferDate,'1900-01-01')  = ISNULL(TRY_CAST(S.DateEligibleForCoverage AS DATE),'1900-01-01'));      
  
        INSERT INTO EmployeePayroll
            (employeeCodeId, payPeriodStartDate, payPeriodEndDate, payPeriodTotalHours, payPeriodSalaryAmount, payPeriodAdditional)   -- FIX 3
        SELECT
            EC.id, ISNULL(TRY_CAST(S.StatusStartDate AS DATE), E.HireDate), ISNULL(TRY_CAST(S.StatusEndDate AS DATE), '2099-12-31'),
            TRY_CAST(S.HourlyRate AS DECIMAL(18,2)),                 -- convention: hourly RATE stored here
            TRY_CAST(S.W2IncomeOrAnnualSalary AS DECIMAL(18,2)),     -- convention: W-2 / annual salary
            TRY_CAST(S.AdditionalIncome AS DECIMAL(18,2))            -- NEW: additional income (was dropped)
                FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON E.ssn = S.EmployeeSSN AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId
            AND (S.W2IncomeOrAnnualSalary IS NOT NULL OR S.HourlyRate IS NOT NULL OR S.AdditionalIncome IS NOT NULL)
            AND NOT EXISTS (SELECT 1 FROM EmployeePayroll PR WHERE PR.employeeCodeId = EC.id AND PR.payPeriodStartDate = ISNULL(TRY_CAST(S.StatusStartDate AS DATE), E.HireDate));      
  
        /* F-07: carry the dependent's own coverage dates, relationship, middle
           name and suffix across. Staging_Dependents supplies all five and the
           import previously took none of them, so every dependent inherited the
           employee's coverage span via the fallback in ACALogicService.
           CalculateMonths -- correct only while the two spans happen to agree. */
        INSERT INTO CoveredIndividual (employeeCodeId, firstName, middleName, lastName, suffix,
                                       ssn, birthday, coverageStartDate, coverageEndDate,
                                       Relationship, EmployerId)
        SELECT
            EC.id,
            S.DependentLegalFirstName,
            NULLIF(LTRIM(RTRIM(S.DependentMiddleInitial)), ''),
            S.DependentLegalLastName,
            NULLIF(LTRIM(RTRIM(S.DependentSuffix)), ''),
            S.DependentSSN,
            TRY_CAST(S.DependentBirthdate AS DATE),
            TRY_CAST(S.DependentCoverageStartDate AS DATE),
            TRY_CAST(S.DependentCoverageEndDate   AS DATE),
            NULLIF(LTRIM(RTRIM(S.Relationship)), ''),
            EC.employerId
                FROM Staging_Dependents S
        JOIN Employee E ON S.EmployeeSSN = E.ssn
             AND (E.EmployerId = @ParentEmployerId
                  OR E.EmployerId IN (SELECT id FROM Employer WHERE companyId = @ParentEmployerId))
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1
            /* The guard used to be CI.ssn = S.DependentSSN alone. A dependent with
               no SSN -- routine for young children -- compares NULL to NULL, which
               is never true, so NOT EXISTS always passed and the row was inserted
               again on every re-import. Fall back to name plus date of birth when
               there is no SSN to match on. */
            AND NOT EXISTS (
                    SELECT 1
                    FROM   CoveredIndividual CI
                    WHERE  CI.employeeCodeId = EC.id
                      AND  (
                             (   NULLIF(LTRIM(RTRIM(S.DependentSSN)), '') IS NOT NULL
                             AND CI.ssn = S.DependentSSN )
                          OR (   NULLIF(LTRIM(RTRIM(S.DependentSSN)), '') IS NULL
                             AND ISNULL(LTRIM(RTRIM(CI.ssn)), '') = ''
                             AND ISNULL(LTRIM(RTRIM(CI.firstName)), '') = ISNULL(LTRIM(RTRIM(S.DependentLegalFirstName)), '')
                             AND ISNULL(LTRIM(RTRIM(CI.lastName)),  '') = ISNULL(LTRIM(RTRIM(S.DependentLegalLastName)),  '')
                             AND ISNULL(CI.birthday, '1900-01-01') = ISNULL(TRY_CAST(S.DependentBirthdate AS DATE), '1900-01-01') )
                           )
                );
  
        /* 9. FINALIZE & SUMMARY */      
        DELETE FROM ImportValidationSummary WHERE FileLogId = @FileLogId;  
        INSERT INTO ImportValidationSummary (FileLogId, TotalEmployees, EmployeesWithHireDate, EmployeesWithStatus, PayrollRows, EnrollmentRows, PlanCount, IsImportComplete, ValidationMessage, DependentRows, TotalPayrollImported)      
        SELECT @FileLogId, (SELECT COUNT(*) FROM Employee WHERE FileLogId = @FileLogId), (SELECT COUNT(*) FROM Employee WHERE FileLogId = @FileLogId AND HireDate IS NOT NULL), (SELECT COUNT(*) FROM Employee WHERE FileLogId = @FileLogId AND Status IS NOT 
        NULL), (SELECT COUNT(*) FROM EmployeePayroll EP JOIN EmployeeCode EC ON EP.employeeCodeId = EC.id JOIN Employee EMP ON EC.employeeId = EMP.id WHERE EMP.FileLogId = @FileLogId), (SELECT COUNT(*) FROM EmployeeEnrollment EE JOIN EmployeeCode EC ON EE.employeeCodeId = EC.id JOIN Employee EMP ON EC.employeeId = EMP.id WHERE EMP.FileLogId = @FileLogId), (SELECT COUNT(*) FROM EmployerPlan WHERE FileLogId = @FileLogId), 1, 'Import complete', (SELECT COUNT(*) FROM CoveredIndividual CI JOIN EmployeeCode EC ON CI.employeeCodeId = EC.id JOIN Employee E ON EC.employeeId = E.id WHERE E.FileLogId = @FileLogId), (SELECT ISNULL(SUM(payPeriodSalaryAmount), 0) FROM EmployeePayroll EP JOIN EmployeeCode EC ON EP.employeeCodeId = EC.id JOIN Employee EMP ON EC.employeeId = EMP.id WHERE EMP.FileLogId = @FileLogId) FROM UploadedFileLog WHERE FileLogId = @FileLogId;      
  
        UPDATE UploadedFileLog SET WorkflowStatusId = 40, ValidationStatusId = 5, ValidationStatus = 'Imported', ProcessingEndTime = SYSUTCDATETIME() WHERE FileLogId = @FileLogId;      
  
        IF @TranStarted = 1  
            COMMIT TRANSACTION;  
    END TRY  
    BEGIN CATCH  
        IF @@TRANCOUNT > 0   
            ROLLBACK TRANSACTION;  
  
        THROW;  
    END CATCH  
END
GO

-- 2. Transmitter mailing address -------------------------------------------------
IF COL_LENGTH('dbo.TransmitterConfig', 'AddressLine1') IS NULL
    ALTER TABLE dbo.TransmitterConfig ADD AddressLine1 NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.TransmitterConfig', 'City') IS NULL
    ALTER TABLE dbo.TransmitterConfig ADD City NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.TransmitterConfig', 'State') IS NULL
    ALTER TABLE dbo.TransmitterConfig ADD State VARCHAR(2) NULL;
IF COL_LENGTH('dbo.TransmitterConfig', 'ZipCode') IS NULL
    ALTER TABLE dbo.TransmitterConfig ADD ZipCode VARCHAR(10) NULL;
GO

-- 3. Unused procedure ----------------------------------------------------------
IF OBJECT_ID(N'dbo.sp_GetEmployerFormCount', N'P') IS NOT NULL
BEGIN
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
    SELECT o.name, '048 unused', m.definition, o.create_date, o.modify_date
    FROM sys.objects o JOIN sys.sql_modules m ON m.object_id = o.object_id
    WHERE o.object_id = OBJECT_ID(N'dbo.sp_GetEmployerFormCount')
      AND NOT EXISTS (SELECT 1 FROM dbo.tbl_DroppedProcedures_Backup_033 b WHERE b.ProcName = o.name);

    DROP PROCEDURE dbo.sp_GetEmployerFormCount;
END
GO

PRINT '048: import skips blank Affiliated EINs; TransmitterConfig has a mailing address; sp_GetEmployerFormCount dropped.';
GO

/* ############################################################################
   049_irs_records_ack_and_age.sql
   ############################################################################ */
PRINT '=== 049_irs_records_ack_and_age.sql';
GO
/* =============================================================================
   049_irs_records_ack_and_age.sql

   IRS AIR filing (IrsXmlService), following IRS Publication 5165:

   1. dbo.IrsSubmissionRecord: which employee each 1095 in a submission is. The
      IRS requires Record IDs to run 1, 2, 3... within a submission (Pub 5165,
      3.2), so they can no longer be employee ids; the acknowledgement's errors
      (ReceiptId|SubmissionId|RecordId) are traced back to employees through this
      table, and corrections name the record they correct from it.
      dbo.IrsSubmissionRecordList is the table type the web app passes the rows in.

   2. dbo.IrsSubmissionErrors gets UniqueRecordId (ReceiptId|SubmissionId|RecordId
      or ReceiptId|SubmissionId for errors on the 1094, as the IRS reports them).
      Nothing ever wrote to this table; the acknowledgement upload now does.

   3. Procedures:
      sp_SaveSubmissionRecords   replace a submission's record list
      sp_GetSubmissionRecords    read it
      sp_ClearSubmissionErrors   remove a submission's errors (before re-reading an acknowledgement)
      sp_AddSubmissionError      add one error (employee name looked up)
      sp_GetSubmissionErrors     unchanged columns plus UniqueRecordId (SELECT *)
      sp_UpdateSubmissionFiles   regenerated files are a new transmission: the old
                                 Receipt ID, acknowledgement and errors are cleared
      sp_GetSubmissionByEmployerYear  the original filing only (never a correction row)

   4. sp_GetEmployeeCodesList also returns the employee's date of birth
      (BirthDate): the 1095-C needs the age on January 1 with ICHRA codes 1L-1S.
      Otherwise unchanged.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1. Record list -----------------------------------------------------------------
IF OBJECT_ID(N'dbo.IrsSubmissionRecord', N'U') IS NULL
    CREATE TABLE dbo.IrsSubmissionRecord (
        SubmissionId INT      NOT NULL,
        RecordId     INT      NOT NULL,
        EmployeeId   INT      NOT NULL,
        CreatedDate  DATETIME NOT NULL CONSTRAINT DF_IrsSubmissionRecord_Created DEFAULT GETDATE(),
        CONSTRAINT PK_IrsSubmissionRecord PRIMARY KEY (SubmissionId, RecordId),
        CONSTRAINT FK_IrsSubmissionRecord_Log FOREIGN KEY (SubmissionId)
            REFERENCES dbo.IrsSubmissionLog (SubmissionId) ON DELETE CASCADE
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_IrsSubmissionRecord_Employee' AND object_id = OBJECT_ID(N'dbo.IrsSubmissionRecord'))
    CREATE INDEX IX_IrsSubmissionRecord_Employee ON dbo.IrsSubmissionRecord (SubmissionId, EmployeeId);
GO
IF TYPE_ID(N'dbo.IrsSubmissionRecordList') IS NULL
    CREATE TYPE dbo.IrsSubmissionRecordList AS TABLE (RecordId INT NOT NULL PRIMARY KEY, EmployeeId INT NOT NULL);
GO

-- 2. Error rows carry the IRS identifier -----------------------------------------
IF COL_LENGTH('dbo.IrsSubmissionErrors', 'UniqueRecordId') IS NULL
    ALTER TABLE dbo.IrsSubmissionErrors ADD UniqueRecordId VARCHAR(120) NULL;
GO

-- 3. Procedures -----------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_SaveSubmissionRecords
    @SubmissionId INT,
    @Records      dbo.IrsSubmissionRecordList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.IrsSubmissionRecord WHERE SubmissionId = @SubmissionId;
    INSERT INTO dbo.IrsSubmissionRecord (SubmissionId, RecordId, EmployeeId)
    SELECT @SubmissionId, RecordId, EmployeeId FROM @Records;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetSubmissionRecords
    @SubmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT RecordId, EmployeeId FROM dbo.IrsSubmissionRecord WHERE SubmissionId = @SubmissionId ORDER BY RecordId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_ClearSubmissionErrors
    @SubmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.IrsSubmissionErrors WHERE SubmissionId = @SubmissionId;
END;
GO

-- RecordId keeps its meaning for the correction screen: the employee id (NULL for an
-- error on the 1094 itself).
CREATE OR ALTER PROCEDURE dbo.sp_AddSubmissionError
    @SubmissionId   INT,
    @EmployeeId     INT           = NULL,
    @UniqueRecordId VARCHAR(120)  = NULL,
    @ErrorCode      VARCHAR(50)   = NULL,
    @ErrorMessage   NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.IrsSubmissionErrors (SubmissionId, RecordId, EmployeeName, ErrorCode, ErrorMessage, IsFixed, CreatedDate, UniqueRecordId)
    SELECT @SubmissionId,
           CAST(@EmployeeId AS VARCHAR(50)),
           CASE WHEN @EmployeeId IS NULL THEN 'Form 1094 (whole submission)'
                ELSE LEFT(LTRIM(RTRIM(CONCAT(e.firstName, ' ', e.lastName))), 100) END,
           LEFT(@ErrorCode, 50), @ErrorMessage, 0, GETDATE(), @UniqueRecordId
    FROM (SELECT 1 AS one) x
    LEFT JOIN dbo.Employee e ON e.id = @EmployeeId;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_UpdateSubmissionFiles]
    @Id               INT,
    @FormType         NVARCHAR(50),
    @TransmissionId   UNIQUEIDENTIFIER,
    @ManifestFilePath NVARCHAR(1024),
    @FormFilePath     NVARCHAR(1024),
    @RecordCount      INT,
    @GeneratedBy      NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    -- New files are a new transmission: the previous one's receipt, acknowledgement and
    -- errors no longer describe this row (049).
    DELETE FROM dbo.IrsSubmissionErrors WHERE SubmissionId = @Id;
    UPDATE dbo.IrsSubmissionLog
    SET    FormType         = @FormType,
           TransmissionId   = @TransmissionId,
           ManifestFilePath = @ManifestFilePath,
           FormFilePath     = @FormFilePath,
           RecordCount      = @RecordCount,
           StatusId         = 0,
           StatusMessage    = 'Regenerated',
           ReceiptId        = NULL,
           AckFilePath      = NULL,
           TransmittedDate  = NULL,
           GeneratedBy      = @GeneratedBy,
           GeneratedDate    = GETDATE()
    WHERE  SubmissionId = @Id;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_GetSubmissionByEmployerYear]
    @EmployerId INT,
    @TaxYear    INT
AS
BEGIN
    SET NOCOUNT ON;
    -- The original filing; correction rows (SubmissionType C) are separate transmissions (049).
    SELECT TOP 1 *
    FROM   dbo.IrsSubmissionLog
    WHERE  EmployerId = @EmployerId AND TaxYear = @TaxYear
      AND  ISNULL(SubmissionType, 'O') <> 'C'
    ORDER BY GeneratedDate DESC;
END;
GO

-- 4. Employee date of birth for the 1095-C --------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[sp_GetEmployeeCodesList]
    @EmployerId INT,
    @Year INT
AS
BEGIN
    BEGIN TRY
        SELECT
            ec.*,
            e.FirstName, e.LastName, e.SSN,
            e.[address]  AS Address1,
            e.[address2] AS Address2,
            e.[city]     AS City,
            e.[state]    AS [State],
            e.[zip]      AS Zip,
            e.[birthday] AS BirthDate   -- 049: age on January 1 for ICHRA codes 1L-1S
        FROM EmployeeCode ec
        INNER JOIN Employee e ON ec.employeeId = e.id
        WHERE ec.employerId = @EmployerId AND ec.filingYear = @Year
        ORDER BY e.LastName;
    END TRY
    BEGIN CATCH
        SELECT
            ERROR_NUMBER() AS ErrorNumber,
            ERROR_LINE() AS ErrorLine,
            ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO

PRINT '049: IRS record list, acknowledgement errors, regenerated files reset, employee date of birth.';
GO

/* ############################################################################
   050_irs_replacements_and_filed_values.sql
   ############################################################################ */
PRINT '=== 050_irs_replacements_and_filed_values.sql';
GO
/* =============================================================================
   050_irs_replacements_and_filed_values.sql

   IRS AIR corrections and replacements, as IRS Publication 5165 section 7 sets them out:

   1. dbo.IrsSubmissionLog
      - ReplacesReceiptId: a rejected original is re-sent as a replacement
        (TransmissionTypeCd R) naming the rejected transmission's Receipt ID
        (manifest OriginalReceiptId, 7.3.1). Kept on the row so the replacement
        stays a replacement if it is regenerated again before it is sent.
      - FiledPayerName, FiledPayerEin: the employer name and EIN as written on the
        1094-C. A correction of the 1094-C must name them as filed (7.1.2).

   2. dbo.IrsSubmissionRecord: FiledFirstName, FiledLastName, FiledTin - the
      recipient's name and TIN as written on each 1095. A correction must give
      them "from the original record" (7.1.3). (The same values are in the form
      file on disk; the TIN is the SSN as filed.) dbo.IrsSubmissionRecordList gains
      the same columns (re-created, with the procedure that uses it).

   3. Procedures: sp_SaveSubmissionLog and sp_UpdateSubmissionFiles take the new
      values; sp_SaveSubmissionRecords / sp_GetSubmissionRecords carry the filed
      name and TIN; sp_GetChildSubmissions lists the corrections of a submission
      (a record already corrected is corrected again through its latest accepted
      correction, 7.1.1).

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1. Submission row ----------------------------------------------------------------
IF COL_LENGTH('dbo.IrsSubmissionLog', 'ReplacesReceiptId') IS NULL
    ALTER TABLE dbo.IrsSubmissionLog ADD ReplacesReceiptId VARCHAR(80) NULL;
IF COL_LENGTH('dbo.IrsSubmissionLog', 'FiledPayerName') IS NULL
    ALTER TABLE dbo.IrsSubmissionLog ADD FiledPayerName NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.IrsSubmissionLog', 'FiledPayerEin') IS NULL
    ALTER TABLE dbo.IrsSubmissionLog ADD FiledPayerEin CHAR(9) NULL;
GO

-- 2. Record list ------------------------------------------------------------------
IF COL_LENGTH('dbo.IrsSubmissionRecord', 'FiledFirstName') IS NULL
    ALTER TABLE dbo.IrsSubmissionRecord ADD FiledFirstName NVARCHAR(40) NULL;
IF COL_LENGTH('dbo.IrsSubmissionRecord', 'FiledLastName') IS NULL
    ALTER TABLE dbo.IrsSubmissionRecord ADD FiledLastName NVARCHAR(40) NULL;
IF COL_LENGTH('dbo.IrsSubmissionRecord', 'FiledTin') IS NULL
    ALTER TABLE dbo.IrsSubmissionRecord ADD FiledTin CHAR(9) NULL;
GO

-- The table type gains columns: drop the procedure that uses it, re-create both.
IF OBJECT_ID(N'dbo.sp_SaveSubmissionRecords', N'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_SaveSubmissionRecords;
IF TYPE_ID(N'dbo.IrsSubmissionRecordList') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.table_types tt JOIN sys.columns c ON c.object_id = tt.type_table_object_id
                   WHERE tt.name = N'IrsSubmissionRecordList' AND c.name = N'FiledTin')
    DROP TYPE dbo.IrsSubmissionRecordList;
IF TYPE_ID(N'dbo.IrsSubmissionRecordList') IS NULL
    CREATE TYPE dbo.IrsSubmissionRecordList AS TABLE (
        RecordId       INT          NOT NULL PRIMARY KEY,
        EmployeeId     INT          NOT NULL,
        FiledFirstName NVARCHAR(40) NULL,
        FiledLastName  NVARCHAR(40) NULL,
        FiledTin       CHAR(9)      NULL
    );
GO

-- 3. Procedures -----------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_SaveSubmissionRecords
    @SubmissionId INT,
    @Records      dbo.IrsSubmissionRecordList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.IrsSubmissionRecord WHERE SubmissionId = @SubmissionId;
    INSERT INTO dbo.IrsSubmissionRecord (SubmissionId, RecordId, EmployeeId, FiledFirstName, FiledLastName, FiledTin)
    SELECT @SubmissionId, RecordId, EmployeeId, FiledFirstName, FiledLastName, FiledTin FROM @Records;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetSubmissionRecords
    @SubmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT RecordId, EmployeeId, FiledFirstName, FiledLastName, FiledTin
    FROM dbo.IrsSubmissionRecord WHERE SubmissionId = @SubmissionId ORDER BY RecordId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetChildSubmissions
    @ParentSubmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.IrsSubmissionLog WHERE ParentSubmissionId = @ParentSubmissionId ORDER BY GeneratedDate, SubmissionId;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_SaveSubmissionLog]
    @EmployerId         INT,
    @TaxYear            INT,
    @FormType           NVARCHAR(50),
    @TransmissionId     UNIQUEIDENTIFIER,
    @ManifestFilePath   NVARCHAR(1024),
    @FormFilePath       NVARCHAR(1024),
    @RecordCount        INT,
    @StatusId           INT,
    @GeneratedBy        NVARCHAR(256),
    @SubmissionType     VARCHAR(1)    = 'O',
    @ParentSubmissionId INT           = NULL,
    @StatusMessage      NVARCHAR(512) = NULL,
    @FiledPayerName     NVARCHAR(100) = NULL,
    @FiledPayerEin      CHAR(9)       = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.IrsSubmissionLog
           (EmployerId, TaxYear, FormType, TransmissionId,
            ManifestFilePath, FormFilePath, RecordCount,
            StatusId, GeneratedBy, SubmissionType, ParentSubmissionId,
            StatusMessage, FiledPayerName, FiledPayerEin)
    VALUES (@EmployerId, @TaxYear, @FormType, @TransmissionId,
            @ManifestFilePath, @FormFilePath, @RecordCount,
            @StatusId, @GeneratedBy, @SubmissionType, @ParentSubmissionId,
            @StatusMessage, @FiledPayerName, @FiledPayerEin);

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS SubmissionId;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_UpdateSubmissionFiles]
    @Id                INT,
    @FormType          NVARCHAR(50),
    @TransmissionId    UNIQUEIDENTIFIER,
    @ManifestFilePath  NVARCHAR(1024),
    @FormFilePath      NVARCHAR(1024),
    @RecordCount       INT,
    @GeneratedBy       NVARCHAR(256),
    @ReplacesReceiptId VARCHAR(80)   = NULL,
    @FiledPayerName    NVARCHAR(100) = NULL,
    @FiledPayerEin     CHAR(9)       = NULL
AS
BEGIN
    SET NOCOUNT ON;
    -- New files are a new transmission: the previous one's receipt, acknowledgement and
    -- errors no longer describe this row (049). A replacement keeps naming the rejected
    -- transmission it replaces (050).
    DELETE FROM dbo.IrsSubmissionErrors WHERE SubmissionId = @Id;
    UPDATE dbo.IrsSubmissionLog
    SET    FormType          = @FormType,
           TransmissionId    = @TransmissionId,
           ManifestFilePath  = @ManifestFilePath,
           FormFilePath      = @FormFilePath,
           RecordCount       = @RecordCount,
           StatusId          = 0,
           StatusMessage     = CASE WHEN @ReplacesReceiptId IS NULL THEN 'Regenerated' ELSE 'Replacement for rejected receipt ' + @ReplacesReceiptId END,
           ReceiptId         = NULL,
           AckFilePath       = NULL,
           TransmittedDate   = NULL,
           ReplacesReceiptId = @ReplacesReceiptId,
           FiledPayerName    = @FiledPayerName,
           FiledPayerEin     = @FiledPayerEin,
           GeneratedBy       = @GeneratedBy,
           GeneratedDate     = GETDATE()
    WHERE  SubmissionId = @Id;
END;
GO

PRINT '050: IRS replacements (ReplacesReceiptId), filed name/TIN for corrections, child submissions.';
GO
/* ############################################################################
   051_supervisor_employer_scope.sql
   ############################################################################ */
PRINT '=== 051_supervisor_employer_scope.sql';
GO
/* =============================================================================
   051_supervisor_employer_scope.sql

   The all-employer roles (decided 2026-09-25, UserRoles.AllEmployerRoles in the web
   app, dbo.fn_DashboardEmployerScope in 029): SuperAdmin, Admin, ACA Director,
   DA Supervisor, Account Manager Supervisor, StaffAC and StaffAU see every employer.
   Two procedures did not follow it:

     1. sp_EmployersOnlyList (the Select Employer list, 047) limited DA Supervisor and
        Account Manager Supervisor to their own assignments, like a Data Analyst and an
        Account Manager. The dashboard, the employer pages and choosing an employer
        already let them reach every employer, so the list hid employers they could
        open. They now see the full list. Account Manager, Data Analyst, Broker and
        Employer logins are unchanged.
     2. sp_GetAllEmployers (a user's employers at sign-in, for View As and for the
        Select Employer empty-list message) gave StaffAC only its assignments (it named
        the full-access roles by id and left out StaffAC). It now goes by role name,
        with StaffAC included.

   sp_GetEmployersByStaffId is unchanged: the web app never asks it about these roles.
   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

-- =====================================================================================
-- sp_EmployersOnlyList - role-based Select Employer list (cross-year by EIN)
--
--   Account Manager -> tbl_Portfolio_Assignment   (AM_UserID)
--   Data Analyst    -> tbl_Operational_Assignment (DA_UserID)
--   Broker          -> tbl_Employer_Broker_Assignment
--   Employer        -> its own employer
--   SuperAdmin, Admin, ACA Director, DA Supervisor, Account Manager Supervisor,
--   StaffAC, StaffAU -> every employer (051)
--   Any other role (or none) -> none
--
-- CROSS-YEAR: employers are separate rows per filing year, linked by taxid (EIN). An
-- assignment points at one year's row, but the user must be able to open other years
-- of the SAME company, so an employer is allowed when its id is assigned or its EIN
-- matches an assigned employer's EIN.
-- =====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[sp_EmployersOnlyList]
(
    @search       NVARCHAR(100) = NULL,
    @PageSize     INT           = NULL,
    @PageIndex    INT           = NULL,
    @fillingyear  INT           = NULL,
    @SortColumn   INT           = NULL,
    @SortOrder    VARCHAR(5)    = NULL,
    @TypeFilter   NVARCHAR(20)  = NULL,
    @UserId       INT           = NULL,   -- logged-in tbl_User.User_ID
    @RoleName     NVARCHAR(100) = NULL    -- logged-in role name
)
AS
BEGIN TRY
    BEGIN TRANSACTION
    SET NOCOUNT ON;

    -- ------------------------------------------------------------------
    -- Resolve, ONCE, what this user is allowed to see (restricted roles only).
    -- @Restricted = 1 means "apply the assignment filter".
    -- ------------------------------------------------------------------
    DECLARE @Restricted BIT = 0;
    DECLARE @AssignedIds    TABLE (id INT PRIMARY KEY);
    DECLARE @AssignedTaxids TABLE (taxid VARCHAR(50));

    IF @UserId IS NOT NULL AND @RoleName = 'Account Manager'
    BEGIN
        SET @Restricted = 1;
        INSERT INTO @AssignedIds (id)
            SELECT EmployerID FROM tbl_Portfolio_Assignment WHERE AM_UserID = @UserId AND IsActive = 1;
        INSERT INTO @AssignedTaxids (taxid)
            SELECT DISTINCT a.taxid
            FROM tbl_Portfolio_Assignment pa JOIN Employer a ON a.id = pa.EmployerID
            WHERE pa.AM_UserID = @UserId AND pa.IsActive = 1 AND ISNULL(a.taxid,'') <> '';
    END
    ELSE IF @UserId IS NOT NULL AND @RoleName = 'Data Analyst'
    BEGIN
        SET @Restricted = 1;
        INSERT INTO @AssignedIds (id)
            SELECT EmployerID FROM tbl_Operational_Assignment WHERE DA_UserID = @UserId AND IsActive = 1;
        INSERT INTO @AssignedTaxids (taxid)
            SELECT DISTINCT a.taxid
            FROM tbl_Operational_Assignment oa JOIN Employer a ON a.id = oa.EmployerID
            WHERE oa.DA_UserID = @UserId AND oa.IsActive = 1 AND ISNULL(a.taxid,'') <> '';
    END

    ELSE IF @UserId IS NOT NULL AND @RoleName = 'Broker'
    BEGIN
        -- 047: a Broker's book is its Broker Assignments (User Accounts > Assign).
        SET @Restricted = 1;
        INSERT INTO @AssignedIds (id)
            SELECT DISTINCT EmployerID FROM tbl_Employer_Broker_Assignment
            WHERE Broker_UserID = @UserId AND IsActive = 1 AND UnassignedDate IS NULL;
        INSERT INTO @AssignedTaxids (taxid)
            SELECT DISTINCT a.taxid
            FROM tbl_Employer_Broker_Assignment ba JOIN Employer a ON a.id = ba.EmployerID
            WHERE ba.Broker_UserID = @UserId AND ba.IsActive = 1 AND ba.UnassignedDate IS NULL
              AND ISNULL(a.taxid,'') <> '';
    END
    ELSE IF @UserId IS NOT NULL AND @RoleName = 'Employer'
    BEGIN
        -- 047: an Employer login sees only the employer it points at (tbl_User.Ref_ID = Employer.id).
        SET @Restricted = 1;
        INSERT INTO @AssignedIds (id)
            SELECT a.id FROM tbl_User u JOIN Employer a ON a.id = u.Ref_ID
            WHERE u.User_ID = @UserId AND u.Role_ID = 13;
    END
    ELSE IF ISNULL(@RoleName, '') NOT IN ('SuperAdmin', 'Admin', 'ACA Director', 'DA Supervisor',
                                         'Account Manager Supervisor', 'StaffAC', 'StaffAU')
        -- 047: any other role (or none) sees no employers here. This list used to apply
        -- no filter at all for roles it did not name, so a Broker or Employer login was
        -- shown every employer (selecting one was refused, but the list itself leaked).
        SET @Restricted = 1;

    DECLARE @RecordCount INT;

    SELECT @RecordCount = COUNT(*)
    FROM Employer AS emp
    WHERE emp.filingYear = @fillingyear
      AND emp.name LIKE '%' + ISNULL(@search, emp.name) + '%'
      AND (@TypeFilter IS NULL
           OR (@TypeFilter = 'Main'      AND emp.companyId IS NULL)
           OR (@TypeFilter = 'Affiliate' AND emp.companyId IS NOT NULL))
      AND (
            @Restricted = 0
            OR emp.id    IN (SELECT id FROM @AssignedIds)
            OR emp.taxid IN (SELECT taxid FROM @AssignedTaxids)
          );

    IF @RecordCount = 0
    BEGIN
        SELECT CAST(NULL AS INT) AS id, CAST(NULL AS INT) AS filingYear, CAST(NULL AS INT) AS companyId,
               CAST(NULL AS VARCHAR(50)) AS taxid, CAST(NULL AS VARCHAR(100)) AS name,
               CAST(NULL AS VARCHAR(100)) AS contactName, CAST(NULL AS VARCHAR(15)) AS phoneNumber,
               CAST(NULL AS VARCHAR(10)) AS [Type], 0 AS TotalCount;
        SELECT 0 AS [RowCount], 0 AS StartPage, 0 AS EndPage, @PageIndex AS PageNumber;
        COMMIT TRANSACTION; RETURN;
    END

    DECLARE @StartPage INT = (@PageIndex - 1) * @PageSize + 1;
    DECLARE @EndPage   INT = @PageIndex * @PageSize;

    SELECT emp.id, emp.filingYear, emp.companyId, emp.taxid, emp.name, emp.contactName, emp.phoneNumber,
           CASE WHEN emp.companyId IS NULL THEN 'Main' ELSE 'Affiliate' END AS [Type],
           @RecordCount AS TotalCount
    FROM Employer AS emp
    WHERE emp.filingYear = @fillingyear
      AND emp.name LIKE '%' + ISNULL(@search, emp.name) + '%'
      AND (@TypeFilter IS NULL
           OR (@TypeFilter = 'Main'      AND emp.companyId IS NULL)
           OR (@TypeFilter = 'Affiliate' AND emp.companyId IS NOT NULL))
      AND (
            @Restricted = 0
            OR emp.id    IN (SELECT id FROM @AssignedIds)
            OR emp.taxid IN (SELECT taxid FROM @AssignedTaxids)
          )
    ORDER BY
        CASE WHEN @SortColumn = 0 AND @SortOrder = 'asc'  THEN emp.name  END ASC,
        CASE WHEN @SortColumn = 0 AND @SortOrder = 'desc' THEN emp.name  END DESC,
        CASE WHEN @SortColumn = 1 AND @SortOrder = 'asc'  THEN emp.taxid END ASC,
        CASE WHEN @SortColumn = 1 AND @SortOrder = 'desc' THEN emp.taxid END DESC,
        emp.name
    OFFSET @PageSize * (@PageIndex - 1) ROWS FETCH NEXT @PageSize ROWS ONLY;

    SELECT @RecordCount AS [RowCount], @StartPage AS StartPage,
           CASE WHEN @EndPage > @RecordCount THEN @RecordCount ELSE @EndPage END AS EndPage,
           @PageIndex AS PageNumber;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    INSERT INTO dbo.DB_Errors
    VALUES (SUSER_SNAME(), ERROR_NUMBER(), ERROR_STATE(), ERROR_SEVERITY(),
            ERROR_LINE(), ERROR_PROCEDURE(), ERROR_MESSAGE(), GETDATE());
    THROW;
END CATCH
GO
GO

-- =====================================================================================
-- sp_GetAllEmployers - every employer for the all-employer roles, otherwise the user's
-- own employer (Employer login) or assigned employers (Account Manager, Data Analyst,
-- Broker). Roles are matched by name, as 029 does, so the script works wherever
-- tbl_Roles has different ids.
-- =====================================================================================
CREATE OR ALTER PROCEDURE [dbo].[sp_GetAllEmployers]
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RoleName NVARCHAR(100), @RefID INT;

    SELECT @RoleName = r.RoleName, @RefID = u.Ref_ID
    FROM   tbl_User u
    LEFT JOIN tbl_Roles r ON r.ID = u.Role_ID
    WHERE  u.User_ID = @UserID;

    -- Every employer (051: StaffAC included, as in UserRoles.AllEmployerRoles).
    IF @RoleName IN ('SuperAdmin', 'Admin', 'ACA Director', 'DA Supervisor',
                     'Account Manager Supervisor', 'StaffAC', 'StaffAU')
    BEGIN
        SELECT id AS Id, filingYear AS FilingYear, companyId AS CompanyId, taxid AS TaxId, name AS Name
        FROM   employer
        ORDER BY name;
        RETURN;
    END

    -- Employer login: its own employer (tbl_User.Ref_ID = Employer.id).
    IF @RoleName = 'Employer'
    BEGIN
        SELECT id AS Id, filingYear AS FilingYear, companyId AS CompanyId, taxid AS TaxId, name AS Name
        FROM   employer
        WHERE  id = @RefID
        ORDER BY name;
        RETURN;
    END

    -- Everyone else: their assignments (Account Manager, Data Analyst, Broker).
    SELECT DISTINCT e.id AS Id, e.filingYear AS FilingYear, e.companyId AS CompanyId, e.taxid AS TaxId, e.name AS Name
    FROM   employer e
    WHERE  e.id IN (
               SELECT P.EmployerID FROM tbl_Portfolio_Assignment P WHERE P.AM_UserID = @UserID AND P.IsActive = 1
               UNION
               SELECT O.EmployerID FROM tbl_Operational_Assignment O WHERE O.DA_UserID = @UserID AND O.IsActive = 1
               UNION
               SELECT B.EmployerID FROM tbl_Employer_Broker_Assignment B WHERE B.Broker_UserID = @UserID AND B.IsActive = 1
           )
    ORDER BY e.name;
END
GO

PRINT '051: Supervisors and StaffAC see every employer in the Select Employer list and at sign-in.';
GO

/* ############################################################################
   052_employee_page_indexes.sql
   ############################################################################ */
PRINT '=== 052_employee_page_indexes.sql';
GO
/* =============================================================================
   052_employee_page_indexes.sql

   The indexes the Employee page's queries rely on (sp_EmployeeList_Filter for the
   list and the Prev/Next list, sp_Employee_QuickStats for the badge counts, the
   flag grid and the import analysis). They were added straight to the developer
   database and are in no script, and the 24 Aug 2026 database copy has only some
   of them, so a database built from it scans whole tables on every open of the
   Employee page.

   Creates each one that is missing, with the definition the developer database
   has. Existing indexes are left alone. Safe to re-run. On a large database each
   new index takes a moment to build.
   ============================================================================= */
SET NOCOUNT ON;
GO

DECLARE @Index TABLE (Seq INT IDENTITY, TableName SYSNAME, IndexName SYSNAME, KeyColumns NVARCHAR(400), IncludeColumns NVARCHAR(400));
INSERT @Index (TableName, IndexName, KeyColumns, IncludeColumns) VALUES
    (N'Employee', N'IX_ACA360_Employee_Core_Filters', N'[IsDeleted], [firstName], [lastName], [ssn]', N'[city], [countryId], [email], [Flag], [fullName], [stateId], [Status], [zip]'),
    (N'Employee', N'IX_ImportAnalysis_Employee', N'[EmployerId], [ssn]', NULL),
    (N'EmployeeCode', N'IX_ACA360_EmployeeCode_Advanced', N'[employerId], [filingYear]', N'[employeeId], [getForm]'),
    (N'EmployeeCode', N'IX_EmployeeCode_Employer_Year1', N'[employerId], [filingYear]', N'[id]'),
    (N'EmployeeCode', N'IX_FlagGrid_YearEmployer', N'[filingYear], [employerId]', N'[employeeId]'),
    (N'EmployeeCode', N'IX_ImportAnalysis_Code', N'[filingYear], [employeeId]', N'[employerId]'),
    (N'EmployeeEnrollment', N'IX_ACA360_Enrollment_Plan_Filter', N'[employeeCodeId], [planId]', N'[contributionEndDate], [contributionStartDate], [unionMember]'),
    (N'EmployeeFlags', N'IX_EmployeeFlags_EmployeeCodeId', N'[EmployeeCodeId]', N'[FlagId], [Month]'),
    (N'EmployeeFlags', N'IX_EmployeeFlags_EmployerYear', N'[EmployerId], [TaxYear]', NULL),
    (N'EmployeeFlags', N'IX_FlagGrid_CodeFlag', N'[EmployeeCodeId], [FlagId]', NULL),
    (N'EmployeeHireSpan', N'IX_FlagGrid_HireCode', N'[employeeCodeId]', NULL),
    (N'EmployeePayroll', N'IX_FlagGrid_PayrollCode', N'[employeeCodeId]', NULL),
    (N'EmployeeStatus', N'IX_ACA360_EmployeeStatus_Filter', N'[status]', NULL),
    (N'EmployeeStatus', N'IX_FlagGrid_StatusCode', N'[employeeCodeId]', NULL),
    (N'Employer', N'IX_ImportAnalysis_Employer', N'[filingYear], [taxid]', N'[companyId]');

DECLARE @Seq INT = 1, @Max INT = (SELECT MAX(Seq) FROM @Index), @Created INT = 0,
        @Table SYSNAME, @Name SYSNAME, @Keys NVARCHAR(400), @Include NVARCHAR(400), @Sql NVARCHAR(MAX);
WHILE @Seq <= @Max
BEGIN
    SELECT @Table = TableName, @Name = IndexName, @Keys = KeyColumns, @Include = IncludeColumns FROM @Index WHERE Seq = @Seq;
    IF OBJECT_ID(N'dbo.' + QUOTENAME(@Table)) IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@Table)) AND name = @Name)
    BEGIN
        SET @Sql = N'CREATE NONCLUSTERED INDEX ' + QUOTENAME(@Name) + N' ON dbo.' + QUOTENAME(@Table) + N' (' + @Keys + N')'
                 + ISNULL(N' INCLUDE (' + @Include + N')', N'') + N';';
        EXEC sys.sp_executesql @Sql;
        PRINT '  created ' + @Table + '.' + @Name;
        SET @Created += 1;
    END
    SET @Seq += 1;
END

PRINT '052: Employee page indexes in place (' + CAST(@Created AS VARCHAR(10)) + ' created).';
GO

/* ############################################################################
   053_warnings_do_not_exclude_rows.sql
   ############################################################################ */
PRINT '=== 053_warnings_do_not_exclude_rows.sql';
GO
/* =============================================================================
   053_warnings_do_not_exclude_rows.sql

   A staging row with only a warning (or an information note) was marked invalid
   (IsValid = 0), the same as a row with an error. sp_Validation_UpdateStatus set
   IsValid = 0 for any issue logged against the row, whatever its severity. So:
     - the file was "Valid with warnings", but the Import Analysis counted those
       rows as Excluded and "Confirm & Import" stayed disabled ("Resolve errors
       and excluded rows before importing."), and
     - had the import gone ahead, sp_ImportData_FromStaging (IsValid = 1 rows
       only) would have left those rows out without saying so.
   Example: a file with 156 "Zip Code Mismatch with State" warnings could not be
   imported, and those 156 employees' 726 dependents were excluded with them.

     1. sp_Validation_UpdateStatus: a row is invalid only when an issue against it
        has an error severity (Error, Fatal, Critical; an issue whose rule is not
        found counts as an error). Warnings and information notes leave it valid.
     2. Repair: rows already made invalid by warnings / information only become
        valid again, in files that have not been imported (not Importing,
        Complete or Superseded, and not marked Imported).

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_Validation_UpdateStatus]
(
    @FileLogId INT
)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TableName NVARCHAR(255), @PKColumn NVARCHAR(255), @SQL NVARCHAR(MAX), @StatsSQL NVARCHAR(MAX);

    -- 1. Force serial stats update for the error table to optimize the join
    UPDATE STATISTICS [dbo].[StagingRowErrors] WITH MAXDOP = 1;

    DECLARE TableCursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT TableName, PrimaryKeyColumn FROM StagingTableMetadata;

    OPEN TableCursor;
    FETCH NEXT FROM TableCursor INTO @TableName, @PKColumn;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- 2. Refresh stats for the current staging table
        SET @StatsSQL = 'UPDATE STATISTICS ' + QUOTENAME(@TableName) + ' WITH SAMPLE 20 PERCENT, MAXDOP = 1;';
        EXEC sp_executesql @StatsSQL;

        -- 3. Invalidate rows with an error-severity issue in this table (053: warnings and
        --    information notes do not keep a row out of the import).
        SET @SQL = '
            UPDATE T
            SET T.IsValid = 0
            FROM ' + QUOTENAME(@TableName) + ' AS T
            INNER JOIN StagingRowErrors AS E
                ON E.StagingRowId = T.' + QUOTENAME(@PKColumn) + '
            LEFT JOIN ValidationRules AS R ON R.RuleId = E.RuleId
            WHERE T.FileLogId = @FileLogId
              AND E.FileLogId = @FileLogId
              -- Row IDs only invalidate their own table
              AND E.StagingTableName = ''' + @TableName + '''
              AND ISNULL(R.Severity, ''Error'') NOT IN (''Warning'', ''Information'', ''Info'')
              AND T.IsValid <> 0
            OPTION (MAXDOP 1);';
        EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId = @FileLogId;

        FETCH NEXT FROM TableCursor INTO @TableName, @PKColumn;
    END
    CLOSE TableCursor;
    DEALLOCATE TableCursor;
END
GO

-- Repair rows that warnings alone made invalid, in files not yet imported. Each row's
-- issues are summed up once (worst severity), then the rows are updated in one join.
IF OBJECT_ID('tempdb..#RowIssue') IS NOT NULL DROP TABLE #RowIssue;
SELECT E.FileLogId, E.StagingTableName, E.StagingRowId,
       MAX(CASE WHEN ISNULL(R.Severity, 'Error') NOT IN ('Warning', 'Information', 'Info') THEN 1 ELSE 0 END) AS HasError
INTO   #RowIssue
FROM   dbo.StagingRowErrors E
JOIN   dbo.UploadedFileLog F ON F.FileLogId = E.FileLogId
LEFT JOIN dbo.ValidationRules R ON R.RuleId = E.RuleId
WHERE  ISNULL(F.WorkflowStatusId, 0) NOT IN (251, 260, 299)
  AND  ISNULL(F.ValidationStatusId, 0) <> 90
GROUP BY E.FileLogId, E.StagingTableName, E.StagingRowId;
DELETE FROM #RowIssue WHERE HasError = 1;
CREATE CLUSTERED INDEX IX_RowIssue ON #RowIssue (StagingTableName, FileLogId, StagingRowId);

DECLARE @TableName NVARCHAR(255), @PKColumn NVARCHAR(255), @SQL NVARCHAR(MAX), @Fixed INT, @Total INT = 0;
DECLARE TableCursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName, PrimaryKeyColumn FROM StagingTableMetadata;
OPEN TableCursor;
FETCH NEXT FROM TableCursor INTO @TableName, @PKColumn;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @SQL = '
        UPDATE T SET T.IsValid = 1
        FROM ' + QUOTENAME(@TableName) + ' AS T
        JOIN #RowIssue AS I ON I.StagingTableName = @TableName AND I.FileLogId = T.FileLogId
                           AND I.StagingRowId = T.' + QUOTENAME(@PKColumn) + '
        WHERE T.IsValid = 0;
        SET @Fixed = @@ROWCOUNT;';
    EXEC sp_executesql @SQL, N'@TableName NVARCHAR(255), @Fixed INT OUTPUT', @TableName = @TableName, @Fixed = @Fixed OUTPUT;
    IF @Fixed > 0 PRINT '  ' + @TableName + ': ' + CAST(@Fixed AS VARCHAR(10)) + ' row(s) valid again';
    SET @Total += @Fixed;
    FETCH NEXT FROM TableCursor INTO @TableName, @PKColumn;
END
CLOSE TableCursor;
DEALLOCATE TableCursor;
DROP TABLE #RowIssue;

PRINT '053: warnings and information notes no longer exclude rows from the import (' + CAST(@Total AS VARCHAR(10)) + ' row(s) repaired).';
GO

/* ############################################################################
   054_dummy_data_menu.sql
   ############################################################################ */
PRINT '=== 054_dummy_data_menu.sql';
GO
/* =============================================================================
   054_dummy_data_menu.sql

   Administration > Dummy Data (DummyDataController): made-up import workbooks for
   testing uploads. The page itself admits SuperAdmin only ([Authorize(Roles =
   SuperAdmin)]); this adds its menu item under Administration and gives the
   SuperAdmin role the Index and View permissions, which is what shows a menu item.
   Users see it after they next sign in.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

DECLARE @Admin INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'Administration' AND ParentMenuId IS NULL AND Flag = 0 ORDER BY Id);
IF @Admin IS NULL THROW 50054, '054: the Administration menu container was not found.', 1;

DECLARE @Menu INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'DummyData' ORDER BY Id);
IF @Menu IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (@Admin, 'Dummy Data', 'DummyData', 'bx bx-data',
            ISNULL((SELECT MAX(DisplayOrder) FROM dbo.tbl_Menus WHERE ParentMenuId = @Admin AND Flag = 0), 0) + 1,
            1, GETDATE(), 0, 1);
    SET @Menu = SCOPE_IDENTITY();
    PRINT '  menu item added';
END
ELSE
    UPDATE dbo.tbl_Menus SET ParentMenuId = @Admin, IsActive = 1, Flag = 0, IsVisible = 1, DateModified = GETDATE()
    WHERE Id = @Menu AND (ParentMenuId <> @Admin OR IsActive <> 1 OR Flag <> 0 OR IsVisible <> 1);

DECLARE @SuperAdmin INT = (SELECT TOP 1 ID FROM dbo.tbl_Roles WHERE RoleName = 'SuperAdmin');
IF @SuperAdmin IS NULL THROW 50054, '054: the SuperAdmin role was not found.', 1;

INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT @SuperAdmin, @Menu, a.Id, 1, GETDATE(), 'script 054'
FROM   dbo.tbl_ActionTypes a
WHERE  a.ActionName IN ('Index', 'View')
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions p WHERE p.RoleId = @SuperAdmin AND p.MenuId = @Menu AND p.ActionID = a.Id);

UPDATE p SET IsEnabled = 1, Date_Modified = GETDATE(), Modified_by = 'script 054'
FROM   dbo.tbl_AccessPermissions p JOIN dbo.tbl_ActionTypes a ON a.Id = p.ActionID
WHERE  p.RoleId = @SuperAdmin AND p.MenuId = @Menu AND a.ActionName IN ('Index', 'View') AND ISNULL(p.IsEnabled, 0) = 0;

PRINT '054: Administration > Dummy Data (SuperAdmin).';
GO

/* ############################################################################
   055_validation_rule_fixes.sql
   ############################################################################ */
PRINT '=== 055_validation_rule_fixes.sql';
GO
/* =============================================================================
   055_validation_rule_fixes.sql

   Validation rules that did not do what they say (found while building the
   Dummy Data page):

     1. Rules that look a value up in another staging table (EXISTS_IN_TABLE)
        checked the looked-up column against the wrong table, so their SQL came
        out empty and they never ran: rule 12 (employee's plan exists) and rule
        74 (employee's associated EIN is in the Employer tab). The engine is
        fixed. Rule 74 now runs as its own check, accepting the main employer's
        Primary EIN as well as the affiliates' EINs (an error: other employees
        are kept out of the import). Rule 12 is switched off: rule 135 already
        reports a missing plan.
        A rule whose SQL cannot be built is now written to DB_Errors.
     2. Rule 19 ("Status Active with Term Date") looked for Status = 'Active',
        but Status is 0 or 1, so it never fired. It now warns when an employee
        has a Termination Date but no Status End Date (or one after it).
     3. Affordability (rule 105) matched every banded premium by salary. Age
        bands now match the employee's age, hourly bands the hourly rate and
        years-of-service bands the years since hire (on 1 January of the plan
        year); salary bands are unchanged.
     4. ZIP code against state (rule 107) covered NY, CA, TX, FL and IL by first
        digit. It now uses dbo.ZipPrefixState, every 3-digit ZIP prefix by state
        (with DC, territories and military). A prefix or state not in the table
        is not reported; foreign addresses are skipped.
     5. Auto-correction (sp_Validation_AutoCorrect) changed values without a
        trace. Every change is now kept in dbo.StagingAutoCorrections (before and
        after) and shown on its row as an Information note ("Corrected
        automatically: ... became ..."), through new StagingRowErrors columns
        ColumnName and Detail that the issue lists show. Information notes do not
        affect the import or the file's status. Formatting (upper case, EIN
        hyphen, ZIP leading zero, phone digits) is not reported.

   The procedures are the database's current ones with only these changes.
   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

-- 1. Tables and columns ------------------------------------------------------
IF COL_LENGTH('dbo.StagingRowErrors', 'ColumnName') IS NULL
    ALTER TABLE dbo.StagingRowErrors ADD ColumnName NVARCHAR(128) NULL;
IF COL_LENGTH('dbo.StagingRowErrors', 'Detail') IS NULL
    ALTER TABLE dbo.StagingRowErrors ADD Detail NVARCHAR(500) NULL;
GO

IF OBJECT_ID('dbo.StagingAutoCorrections') IS NULL
BEGIN
    CREATE TABLE dbo.StagingAutoCorrections
    (
        StagingAutoCorrectionId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StagingAutoCorrections PRIMARY KEY,
        FileLogId        INT            NOT NULL,
        StagingTableName NVARCHAR(128)  NOT NULL,
        StagingRowId     BIGINT         NOT NULL,
        ColumnName       NVARCHAR(128)  NOT NULL,
        RuleId           INT            NULL,
        OldValue         NVARCHAR(4000) NULL,
        NewValue         NVARCHAR(4000) NULL,
        CorrectedAt      DATETIME2(0)   NOT NULL CONSTRAINT DF_StagingAutoCorrections_CorrectedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_StagingAutoCorrections_File ON dbo.StagingAutoCorrections (FileLogId, StagingTableName, StagingRowId);
END
GO

IF OBJECT_ID('dbo.ZipPrefixState') IS NULL
    CREATE TABLE dbo.ZipPrefixState
    (
        Prefix CHAR(3) NOT NULL,
        State  CHAR(2) NOT NULL,
        CONSTRAINT PK_ZipPrefixState PRIMARY KEY (Prefix, State)
    );
GO
DECLARE @Zip TABLE (Prefix CHAR(3) NOT NULL, State CHAR(2) NOT NULL, PRIMARY KEY (Prefix, State));
INSERT INTO @Zip (Prefix, State) VALUES
    ('005','NY'), ('006','PR'), ('007','PR'), ('008','VI'), ('009','PR'), ('010','MA'), ('011','MA'), ('012','MA'), ('013','MA'), ('014','MA'),
    ('015','MA'), ('016','MA'), ('017','MA'), ('018','MA'), ('019','MA'), ('020','MA'), ('021','MA'), ('022','MA'), ('023','MA'), ('024','MA'),
    ('025','MA'), ('026','MA'), ('027','MA'), ('028','RI'), ('029','RI'), ('030','NH'), ('031','NH'), ('032','NH'), ('033','NH'), ('034','NH'),
    ('035','NH'), ('036','NH'), ('037','NH'), ('038','NH'), ('039','ME'), ('040','ME'), ('041','ME'), ('042','ME'), ('043','ME'), ('044','ME'),
    ('045','ME'), ('046','ME'), ('047','ME'), ('048','ME'), ('049','ME'), ('050','VT'), ('051','VT'), ('052','VT'), ('053','VT'), ('054','VT'),
    ('055','MA'), ('056','VT'), ('057','VT'), ('058','VT'), ('059','VT'), ('060','CT'), ('061','CT'), ('062','CT'), ('063','CT'), ('064','CT'),
    ('065','CT'), ('066','CT'), ('067','CT'), ('068','CT'), ('069','CT'), ('070','NJ'), ('071','NJ'), ('072','NJ'), ('073','NJ'), ('074','NJ'),
    ('075','NJ'), ('076','NJ'), ('077','NJ'), ('078','NJ'), ('079','NJ'), ('080','NJ'), ('081','NJ'), ('082','NJ'), ('083','NJ'), ('084','NJ'),
    ('085','NJ'), ('086','NJ'), ('087','NJ'), ('088','NJ'), ('089','NJ'), ('090','AE'), ('091','AE'), ('092','AE'), ('093','AE'), ('094','AE'),
    ('095','AE'), ('096','AE'), ('097','AE'), ('098','AE'), ('100','NY'), ('101','NY'), ('102','NY'), ('103','NY'), ('104','NY'), ('105','NY'),
    ('106','NY'), ('107','NY'), ('108','NY'), ('109','NY'), ('110','NY'), ('111','NY'), ('112','NY'), ('113','NY'), ('114','NY'), ('115','NY'),
    ('116','NY'), ('117','NY'), ('118','NY'), ('119','NY'), ('120','NY'), ('121','NY'), ('122','NY'), ('123','NY'), ('124','NY'), ('125','NY'),
    ('126','NY'), ('127','NY'), ('128','NY'), ('129','NY'), ('130','NY'), ('131','NY'), ('132','NY'), ('133','NY'), ('134','NY'), ('135','NY'),
    ('136','NY'), ('137','NY'), ('138','NY'), ('139','NY'), ('140','NY'), ('141','NY'), ('142','NY'), ('143','NY'), ('144','NY'), ('145','NY'),
    ('146','NY'), ('147','NY'), ('148','NY'), ('149','NY'), ('150','PA'), ('151','PA'), ('152','PA'), ('153','PA'), ('154','PA'), ('155','PA'),
    ('156','PA'), ('157','PA'), ('158','PA'), ('159','PA'), ('160','PA'), ('161','PA'), ('162','PA'), ('163','PA'), ('164','PA'), ('165','PA'),
    ('166','PA'), ('167','PA'), ('168','PA'), ('169','PA'), ('170','PA'), ('171','PA'), ('172','PA'), ('173','PA'), ('174','PA'), ('175','PA'),
    ('176','PA'), ('177','PA'), ('178','PA'), ('179','PA'), ('180','PA'), ('181','PA'), ('182','PA'), ('183','PA'), ('184','PA'), ('185','PA'),
    ('186','PA'), ('187','PA'), ('188','PA'), ('189','PA'), ('190','PA'), ('191','PA'), ('192','PA'), ('193','PA'), ('194','PA'), ('195','PA'),
    ('196','PA'), ('197','DE'), ('198','DE'), ('199','DE'), ('200','DC'), ('201','VA'), ('202','DC'), ('203','DC'), ('204','DC'), ('205','DC');
INSERT INTO @Zip (Prefix, State) VALUES
    ('206','MD'), ('207','MD'), ('208','MD'), ('209','MD'), ('210','MD'), ('211','MD'), ('212','MD'), ('213','MD'), ('214','MD'), ('215','MD'),
    ('216','MD'), ('217','MD'), ('218','MD'), ('219','MD'), ('220','VA'), ('221','VA'), ('222','VA'), ('223','VA'), ('224','VA'), ('225','VA'),
    ('226','VA'), ('227','VA'), ('228','VA'), ('229','VA'), ('230','VA'), ('231','VA'), ('232','VA'), ('233','VA'), ('234','VA'), ('235','VA'),
    ('236','VA'), ('237','VA'), ('238','VA'), ('239','VA'), ('240','VA'), ('241','VA'), ('242','VA'), ('243','VA'), ('244','VA'), ('245','VA'),
    ('246','VA'), ('247','WV'), ('248','WV'), ('249','WV'), ('250','WV'), ('251','WV'), ('252','WV'), ('253','WV'), ('254','WV'), ('255','WV'),
    ('256','WV'), ('257','WV'), ('258','WV'), ('259','WV'), ('260','WV'), ('261','WV'), ('262','WV'), ('263','WV'), ('264','WV'), ('265','WV'),
    ('266','WV'), ('267','WV'), ('268','WV'), ('270','NC'), ('271','NC'), ('272','NC'), ('273','NC'), ('274','NC'), ('275','NC'), ('276','NC'),
    ('277','NC'), ('278','NC'), ('279','NC'), ('280','NC'), ('281','NC'), ('282','NC'), ('283','NC'), ('284','NC'), ('285','NC'), ('286','NC'),
    ('287','NC'), ('288','NC'), ('289','NC'), ('290','SC'), ('291','SC'), ('292','SC'), ('293','SC'), ('294','SC'), ('295','SC'), ('296','SC'),
    ('297','SC'), ('298','SC'), ('299','SC'), ('300','GA'), ('301','GA'), ('302','GA'), ('303','GA'), ('304','GA'), ('305','GA'), ('306','GA'),
    ('307','GA'), ('308','GA'), ('309','GA'), ('310','GA'), ('311','GA'), ('312','GA'), ('313','GA'), ('314','GA'), ('315','GA'), ('316','GA'),
    ('317','GA'), ('318','GA'), ('319','GA'), ('320','FL'), ('321','FL'), ('322','FL'), ('323','FL'), ('324','FL'), ('325','FL'), ('326','FL'),
    ('327','FL'), ('328','FL'), ('329','FL'), ('330','FL'), ('331','FL'), ('332','FL'), ('333','FL'), ('334','FL'), ('335','FL'), ('336','FL'),
    ('337','FL'), ('338','FL'), ('339','FL'), ('340','AA'), ('341','FL'), ('342','FL'), ('343','FL'), ('344','FL'), ('345','FL'), ('346','FL'),
    ('347','FL'), ('348','FL'), ('349','FL'), ('350','AL'), ('351','AL'), ('352','AL'), ('353','AL'), ('354','AL'), ('355','AL'), ('356','AL'),
    ('357','AL'), ('358','AL'), ('359','AL'), ('360','AL'), ('361','AL'), ('362','AL'), ('363','AL'), ('364','AL'), ('365','AL'), ('366','AL'),
    ('367','AL'), ('368','AL'), ('369','AL'), ('370','TN'), ('371','TN'), ('372','TN'), ('373','TN'), ('374','TN'), ('375','TN'), ('376','TN'),
    ('377','TN'), ('378','TN'), ('379','TN'), ('380','TN'), ('381','TN'), ('382','TN'), ('383','TN'), ('384','TN'), ('385','TN'), ('386','MS'),
    ('387','MS'), ('388','MS'), ('389','MS'), ('390','MS'), ('391','MS'), ('392','MS'), ('393','MS'), ('394','MS'), ('395','MS'), ('396','MS'),
    ('397','MS'), ('398','GA'), ('399','GA'), ('400','KY'), ('401','KY'), ('402','KY'), ('403','KY'), ('404','KY'), ('405','KY'), ('406','KY');
INSERT INTO @Zip (Prefix, State) VALUES
    ('407','KY'), ('408','KY'), ('409','KY'), ('410','KY'), ('411','KY'), ('412','KY'), ('413','KY'), ('414','KY'), ('415','KY'), ('416','KY'),
    ('417','KY'), ('418','KY'), ('419','KY'), ('420','KY'), ('421','KY'), ('422','KY'), ('423','KY'), ('424','KY'), ('425','KY'), ('426','KY'),
    ('427','KY'), ('430','OH'), ('431','OH'), ('432','OH'), ('433','OH'), ('434','OH'), ('435','OH'), ('436','OH'), ('437','OH'), ('438','OH'),
    ('439','OH'), ('440','OH'), ('441','OH'), ('442','OH'), ('443','OH'), ('444','OH'), ('445','OH'), ('446','OH'), ('447','OH'), ('448','OH'),
    ('449','OH'), ('450','OH'), ('451','OH'), ('452','OH'), ('453','OH'), ('454','OH'), ('455','OH'), ('456','OH'), ('457','OH'), ('458','OH'),
    ('459','OH'), ('460','IN'), ('461','IN'), ('462','IN'), ('463','IN'), ('464','IN'), ('465','IN'), ('466','IN'), ('467','IN'), ('468','IN'),
    ('469','IN'), ('470','IN'), ('471','IN'), ('472','IN'), ('473','IN'), ('474','IN'), ('475','IN'), ('476','IN'), ('477','IN'), ('478','IN'),
    ('479','IN'), ('480','MI'), ('481','MI'), ('482','MI'), ('483','MI'), ('484','MI'), ('485','MI'), ('486','MI'), ('487','MI'), ('488','MI'),
    ('489','MI'), ('490','MI'), ('491','MI'), ('492','MI'), ('493','MI'), ('494','MI'), ('495','MI'), ('496','MI'), ('497','MI'), ('498','MI'),
    ('499','MI'), ('500','IA'), ('501','IA'), ('502','IA'), ('503','IA'), ('504','IA'), ('505','IA'), ('506','IA'), ('507','IA'), ('508','IA'),
    ('509','IA'), ('510','IA'), ('511','IA'), ('512','IA'), ('513','IA'), ('514','IA'), ('515','IA'), ('516','IA'), ('517','IA'), ('518','IA'),
    ('519','IA'), ('520','IA'), ('521','IA'), ('522','IA'), ('523','IA'), ('524','IA'), ('525','IA'), ('526','IA'), ('527','IA'), ('528','IA'),
    ('530','WI'), ('531','WI'), ('532','WI'), ('533','WI'), ('534','WI'), ('535','WI'), ('536','WI'), ('537','WI'), ('538','WI'), ('539','WI'),
    ('540','WI'), ('541','WI'), ('542','WI'), ('543','WI'), ('544','WI'), ('545','WI'), ('546','WI'), ('547','WI'), ('548','WI'), ('549','WI'),
    ('550','MN'), ('551','MN'), ('552','MN'), ('553','MN'), ('554','MN'), ('555','MN'), ('556','MN'), ('557','MN'), ('558','MN'), ('559','MN'),
    ('560','MN'), ('561','MN'), ('562','MN'), ('563','MN'), ('564','MN'), ('565','MN'), ('566','MN'), ('567','MN'), ('569','DC'), ('570','SD'),
    ('571','SD'), ('572','SD'), ('573','SD'), ('574','SD'), ('575','SD'), ('576','SD'), ('577','SD'), ('580','ND'), ('581','ND'), ('582','ND'),
    ('583','ND'), ('584','ND'), ('585','ND'), ('586','ND'), ('587','ND'), ('588','ND'), ('590','MT'), ('591','MT'), ('592','MT'), ('593','MT'),
    ('594','MT'), ('595','MT'), ('596','MT'), ('597','MT'), ('598','MT'), ('599','MT'), ('600','IL'), ('601','IL'), ('602','IL'), ('603','IL'),
    ('604','IL'), ('605','IL'), ('606','IL'), ('607','IL'), ('608','IL'), ('609','IL'), ('610','IL'), ('611','IL'), ('612','IL'), ('613','IL');
INSERT INTO @Zip (Prefix, State) VALUES
    ('614','IL'), ('615','IL'), ('616','IL'), ('617','IL'), ('618','IL'), ('619','IL'), ('620','IL'), ('621','IL'), ('622','IL'), ('623','IL'),
    ('624','IL'), ('625','IL'), ('626','IL'), ('627','IL'), ('628','IL'), ('629','IL'), ('630','MO'), ('631','MO'), ('632','MO'), ('633','MO'),
    ('634','MO'), ('635','MO'), ('636','MO'), ('637','MO'), ('638','MO'), ('639','MO'), ('640','MO'), ('641','MO'), ('642','MO'), ('643','MO'),
    ('644','MO'), ('645','MO'), ('646','MO'), ('647','MO'), ('648','MO'), ('649','MO'), ('650','MO'), ('651','MO'), ('652','MO'), ('653','MO'),
    ('654','MO'), ('655','MO'), ('656','MO'), ('657','MO'), ('658','MO'), ('660','KS'), ('661','KS'), ('662','KS'), ('663','KS'), ('664','KS'),
    ('665','KS'), ('666','KS'), ('667','KS'), ('668','KS'), ('669','KS'), ('670','KS'), ('671','KS'), ('672','KS'), ('673','KS'), ('674','KS'),
    ('675','KS'), ('676','KS'), ('677','KS'), ('678','KS'), ('679','KS'), ('680','NE'), ('681','NE'), ('682','NE'), ('683','NE'), ('684','NE'),
    ('685','NE'), ('686','NE'), ('687','NE'), ('688','NE'), ('689','NE'), ('690','NE'), ('691','NE'), ('692','NE'), ('693','NE'), ('700','LA'),
    ('701','LA'), ('702','LA'), ('703','LA'), ('704','LA'), ('705','LA'), ('706','LA'), ('707','LA'), ('708','LA'), ('709','LA'), ('710','LA'),
    ('711','LA'), ('712','LA'), ('713','LA'), ('714','LA'), ('716','AR'), ('717','AR'), ('718','AR'), ('719','AR'), ('720','AR'), ('721','AR'),
    ('722','AR'), ('723','AR'), ('724','AR'), ('725','AR'), ('726','AR'), ('727','AR'), ('728','AR'), ('729','AR'), ('730','OK'), ('731','OK'),
    ('733','TX'), ('734','OK'), ('735','OK'), ('736','OK'), ('737','OK'), ('738','OK'), ('739','OK'), ('740','OK'), ('741','OK'), ('742','OK'),
    ('743','OK'), ('744','OK'), ('745','OK'), ('746','OK'), ('747','OK'), ('748','OK'), ('749','OK'), ('750','TX'), ('751','TX'), ('752','TX'),
    ('753','TX'), ('754','TX'), ('755','TX'), ('756','TX'), ('757','TX'), ('758','TX'), ('759','TX'), ('760','TX'), ('761','TX'), ('762','TX'),
    ('763','TX'), ('764','TX'), ('765','TX'), ('766','TX'), ('767','TX'), ('768','TX'), ('769','TX'), ('770','TX'), ('771','TX'), ('772','TX'),
    ('773','TX'), ('774','TX'), ('775','TX'), ('776','TX'), ('777','TX'), ('778','TX'), ('779','TX'), ('780','TX'), ('781','TX'), ('782','TX'),
    ('783','TX'), ('784','TX'), ('785','TX'), ('786','TX'), ('787','TX'), ('788','TX'), ('789','TX'), ('790','TX'), ('791','TX'), ('792','TX'),
    ('793','TX'), ('794','TX'), ('795','TX'), ('796','TX'), ('797','TX'), ('798','TX'), ('799','TX'), ('800','CO'), ('801','CO'), ('802','CO'),
    ('803','CO'), ('804','CO'), ('805','CO'), ('806','CO'), ('807','CO'), ('808','CO'), ('809','CO'), ('810','CO'), ('811','CO'), ('812','CO'),
    ('813','CO'), ('814','CO'), ('815','CO'), ('816','CO'), ('820','WY'), ('821','WY'), ('822','WY'), ('823','WY'), ('824','WY'), ('825','WY');
INSERT INTO @Zip (Prefix, State) VALUES
    ('826','WY'), ('827','WY'), ('828','WY'), ('829','WY'), ('830','WY'), ('831','WY'), ('832','ID'), ('833','ID'), ('834','ID'), ('835','ID'),
    ('836','ID'), ('837','ID'), ('838','ID'), ('840','UT'), ('841','UT'), ('842','UT'), ('843','UT'), ('844','UT'), ('845','UT'), ('846','UT'),
    ('847','UT'), ('850','AZ'), ('851','AZ'), ('852','AZ'), ('853','AZ'), ('854','AZ'), ('855','AZ'), ('856','AZ'), ('857','AZ'), ('858','AZ'),
    ('859','AZ'), ('860','AZ'), ('861','AZ'), ('862','AZ'), ('863','AZ'), ('864','AZ'), ('865','AZ'), ('870','NM'), ('871','NM'), ('872','NM'),
    ('873','NM'), ('874','NM'), ('875','NM'), ('876','NM'), ('877','NM'), ('878','NM'), ('879','NM'), ('880','NM'), ('881','NM'), ('882','NM'),
    ('883','NM'), ('884','NM'), ('885','TX'), ('889','NV'), ('890','NV'), ('891','NV'), ('892','NV'), ('893','NV'), ('894','NV'), ('895','NV'),
    ('896','NV'), ('897','NV'), ('898','NV'), ('900','CA'), ('901','CA'), ('902','CA'), ('903','CA'), ('904','CA'), ('905','CA'), ('906','CA'),
    ('907','CA'), ('908','CA'), ('909','CA'), ('910','CA'), ('911','CA'), ('912','CA'), ('913','CA'), ('914','CA'), ('915','CA'), ('916','CA'),
    ('917','CA'), ('918','CA'), ('919','CA'), ('920','CA'), ('921','CA'), ('922','CA'), ('923','CA'), ('924','CA'), ('925','CA'), ('926','CA'),
    ('927','CA'), ('928','CA'), ('929','CA'), ('930','CA'), ('931','CA'), ('932','CA'), ('933','CA'), ('934','CA'), ('935','CA'), ('936','CA'),
    ('937','CA'), ('938','CA'), ('939','CA'), ('940','CA'), ('941','CA'), ('942','CA'), ('943','CA'), ('944','CA'), ('945','CA'), ('946','CA'),
    ('947','CA'), ('948','CA'), ('949','CA'), ('950','CA'), ('951','CA'), ('952','CA'), ('953','CA'), ('954','CA'), ('955','CA'), ('956','CA'),
    ('957','CA'), ('958','CA'), ('959','CA'), ('960','CA'), ('961','CA'), ('962','AP'), ('963','AP'), ('964','AP'), ('965','AP'), ('966','AP'),
    ('967','HI'), ('968','HI'), ('967','AS'), ('969','GU'), ('969','MP'), ('969','PW'), ('969','FM'), ('969','MH'), ('970','OR'), ('971','OR'),
    ('972','OR'), ('973','OR'), ('974','OR'), ('975','OR'), ('976','OR'), ('977','OR'), ('978','OR'), ('979','OR'), ('980','WA'), ('981','WA'),
    ('982','WA'), ('983','WA'), ('984','WA'), ('985','WA'), ('986','WA'), ('987','WA'), ('988','WA'), ('989','WA'), ('990','WA'), ('991','WA'),
    ('992','WA'), ('993','WA'), ('994','WA'), ('995','AK'), ('996','AK'), ('997','AK'), ('998','AK'), ('999','AK');
INSERT INTO dbo.ZipPrefixState (Prefix, State)
SELECT Z.Prefix, Z.State FROM @Zip Z
WHERE NOT EXISTS (SELECT 1 FROM dbo.ZipPrefixState X WHERE X.Prefix = Z.Prefix AND X.State = Z.State);
GO

-- 2. Rules -------------------------------------------------------------------
-- The note auto-correction writes. Not run by the rule engine: it stays inactive.
IF NOT EXISTS (SELECT 1 FROM dbo.ValidationRules WHERE RuleCode = 'AUTO_CORRECTED')
    INSERT INTO dbo.ValidationRules (RuleName, TargetTable, TargetColumn, ValidationType, RuleParameter1, RuleParameter2,
                                     ErrorMessage, IsActive, Severity, IsCorrectable, HelpText, RuleCode, IsAutoCorrect)
    VALUES ('Value Corrected Automatically', 'Staging_Employees', 'Value', 'AUTO_CORRECTED', NULL, NULL,
            N'A value was corrected automatically.', 0, 'Information', 0,
            N'Written by sp_Validation_AutoCorrect (script 055) for each value an auto-correct rule changed; the note on the row shows the value before and after. Not run by the rule engine: leave it inactive.',
            'AUTO_CORRECTED', 0);

-- Rule 12 duplicates rule 135 (PlanName_Must_Exist), which reports a missing plan.
UPDATE dbo.ValidationRules SET IsActive = 0
WHERE  ValidationType = 'EXISTS_IN_TABLE' AND TargetTable = 'Staging_Employees' AND TargetColumn = 'LowestCostPlanOffered' AND IsActive = 1
  AND  EXISTS (SELECT 1 FROM dbo.ValidationRules WHERE RuleParameter1 = 'PlanName_Must_Exist' AND IsActive = 1);

-- Rule 74 compared the associated EIN with the Affiliated EIN column only, so employees under a main
-- employer whose row leaves Affiliated EIN blank (allowed since 048) would all be errors.
UPDATE dbo.ValidationRules
SET    ValidationType = 'CUSTOM_LOGIC', RuleParameter1 = 'AssociatedEIN_In_EmployerTab', RuleParameter2 = NULL
WHERE  TargetTable = 'Staging_Employees' AND TargetColumn = 'EINAssociatedWithEE' AND ValidationType = 'EXISTS_IN_TABLE';

UPDATE dbo.ValidationRules
SET    RuleName = 'Status Open After Termination',
       ErrorMessage = N'The employee has a Termination Date, but the Status End Date is blank or later than it.'
WHERE  RuleParameter1 = 'Status_ActiveWithTermDate';
GO

-- 3. Procedures --------------------------------------------------------------
-- sp_Validation_ExecuteRules
/* ---------------------------------------------------------------- PART 1 ----
   sp_Validation_ExecuteRules - handlers for the eight missing types, repairs for
   the two malformed blocks, and the missing CUSTOM_LOGIC keyword.
   ---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE [dbo].[sp_Validation_ExecuteRules] @FileLogId INT AS
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
DELETE FROM dbo.StagingAutoCorrections WHERE FileLogId = @FileLogId;
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
WHERE  C.FileLogId = @FileLogId;
END
GO

-- sp_ImportAnalysis_GetRows
CREATE OR ALTER PROCEDURE dbo.sp_ImportAnalysis_GetRows
    @FileLogId int, @Entity varchar(20)='Employer', @ImportMode varchar(20)='Merge',
    @Start int=0, @Length int=10, @Search nvarchar(200)=NULL,
    @SortColumn sysname='rowNumber', @SortDirection varchar(4)='asc',
    @ImpactFilter varchar(12)='All', @ValidationFilter varchar(12)='All'
AS
BEGIN
    SET NOCOUNT ON;
    IF @Start IS NULL OR @Start<0 OR @Length IS NULL OR @Length NOT BETWEEN 1 AND 100 THROW 51003,'Invalid page.',1;
    IF @ImportMode IS NULL OR @ImportMode NOT IN ('Merge','Overwrite') THROW 51001,'Invalid import mode.',1;
    IF @SortDirection IS NULL OR @SortDirection NOT IN ('asc','desc') THROW 51004,'Invalid sort direction.',1;
    IF @ImpactFilter IS NULL OR @ImpactFilter NOT IN ('All','INSERT','UPDATE','NOCHANGE','REMOVE','EXCLUDED') THROW 51005,'Invalid change filter.',1;
    IF @ValidationFilter IS NULL OR @ValidationFilter NOT IN ('All','Clean','Warning','Error','Information') THROW 51006,'Invalid validation filter.',1;
    IF NOT EXISTS (SELECT 1 FROM dbo.fn_ImportAnalysis_Context(@FileLogId) WHERE PlanYear BETWEEN 1900 AND 9999)
        THROW 51002,'File or filing year is unavailable.',1;

    DECLARE @Table sysname,@Pk sysname;
    SELECT @Table=T,@Pk=P FROM (VALUES

        ('Employer','Staging_Employers','StagingEmployerId'),
        ('Plan','Staging_Plans','StagingPlanId'),
        ('Premium','Staging_Premiums','StagingPremiumId'),
        ('Employee','Staging_Employees','StagingEmployeeId'),
        ('Dependent','Staging_Dependents','StagingDependentId')
    ) M(E,T,P) WHERE E=@Entity;
    IF @Table IS NULL THROW 51007,'Invalid sheet.',1;
    DECLARE @Columns TABLE (Ordinal int,ColumnName sysname);

    IF @Entity='Employer' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'AffiliatedEIN'),
        (2,'EmployerName'),
        (3,'ForeignAddressInd'),
        (4,'Address'),
        (5,'Address2'),
        (6,'City'),
        (7,'StateOrProvince'),
        (8,'Zip'),
        (9,'Country'),
        (10,'Phone'),
        (11,'Contact'),
        (12,'Title'),
        (13,'OriginCode'),
        (14,'SHOPIdentifier'),
        (15,'Notes');

    IF @Entity='Plan' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'PlanName'),
        (2,'PlanType'),
        (3,'OfferedToSpouse'),
        (4,'OfferedToDependents'),
        (5,'WaitingPeriodNumberOfDays'),
        (6,'EligibleFirstOfTheMonth'),
        (7,'FundingType'),
        (8,'PlanRenewalMonth'),
        (9,'PlanTerminatesOnDateOfTermination'),
        (10,'MinimumValue'),
        (11,'BandingType'),
        (12,'PremiumCap');

    IF @Entity='Premium' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'PlanName'),
        (2,'BandingType'),
        (3,'Start'),
        (4,'End'),
        (5,'StartDate'),
        (6,'EndDate'),
        (7,'EEMonthlyContribution'),
        (8,'Notes');

    IF @Entity='Employee' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'EINAssociatedWithEE'),
        (2,'EmployeeLegalFirstName'),
        (3,'EmployeeMiddleInitial'),
        (4,'EmployeeLegalLastName'),
        (5,'EmployeeSuffix'),
        (6,'EmployeeSSN'),
        (7,'EmployeeBirthdate'),
        (8,'Status'),
        (9,'Expatriate'),
        (10,'StatusStartDate'),
        (11,'StatusEndDate'),
        (12,'HireDate'),
        (13,'TerminationDate'),
        (14,'W2IncomeOrAnnualSalary'),
        (15,'AdditionalIncome'),
        (16,'HourlyRate'),
        (17,'EmailAddress'),
        (18,'ForeignAddress'),
        (19,'Address1'),
        (20,'Address2'),
        (21,'City'),
        (22,'StateOrProvince'),
        (23,'Zip'),
        (24,'Country'),
        (25,'LowestCostPlanOffered'),
        (26,'DateEligibleForCoverage'),
        (27,'CoverageElected'),
        (28,'CoverageStartDate'),
        (29,'CoverageEndDate'),
        (30,'UnionEmployee'),
        (31,'DateEmployerFirstPaysToUnionBenefits'),
        (32,'DateEmployerLastPaysToUnionBenefits'),
        (33,'COBRABenefitsElected'),
        (34,'COBRACoverageEffectiveDate'),
        (35,'COBRACoverageEndDate'),
        (36,'RetireeBenefitsElected'),
        (37,'RetireePlanStartDate'),
        (38,'RetireePlanEndDate');

    IF @Entity='Dependent' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'EINAssociatedWithEE'),
        (2,'EmployeeSSN'),
        (3,'DependentLegalFirstName'),
        (4,'DependentMiddleInitial'),
        (5,'DependentLegalLastName'),
        (6,'DependentSuffix'),
        (7,'DependentSSN'),
        (8,'DependentBirthdate'),
        (9,'DependentCoverageStartDate'),
        (10,'DependentCoverageEndDate'),
        (11,'Relationship');

    IF @SortColumn IS NULL OR (@SortColumn NOT IN ('rowNumber','actionType','validationStatus')
        AND NOT EXISTS (SELECT 1 FROM @Columns WHERE ColumnName=@SortColumn)) THROW 51008,'Invalid sort column.',1;

    SELECT * INTO #Analysis FROM dbo.fn_ImportAnalysis_Rows(@FileLogId,@Entity,@ImportMode) OPTION (RECOMPILE);
    CREATE UNIQUE CLUSTERED INDEX IX_Analysis_Source_Row ON #Analysis(RowSource,RowId);
    CREATE INDEX IX_Analysis_DefaultPage ON #Analysis(RowNumber,RowSource,RowId);

    DECLARE @Projection nvarchar(max),@SearchPredicate nvarchar(max),@LiveProjection nvarchar(max)=N'',@LiveFrom nvarchar(max)=N'';
    SELECT @Projection=STUFF((SELECT N',CONVERT(nvarchar(max),S.'+QUOTENAME(ColumnName)+N') AS '+QUOTENAME(ColumnName)
        FROM @Columns ORDER BY Ordinal FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,1,N'');
    SELECT @SearchPredicate=STUFF((SELECT N' OR R.'+QUOTENAME(ColumnName)+N' LIKE @Pattern ESCAPE N'+CHAR(39)+N'~'+CHAR(39)
        FROM @Columns ORDER BY Ordinal FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,4,N'');

    IF @Entity='Premium'
    BEGIN
        SET @LiveProjection=N'CONVERT(nvarchar(max),C.PrimaryEIN) AS [PrimaryEIN],CONVERT(nvarchar(max),P.name) AS [PlanName],CONVERT(nvarchar(max),P.bandingType) AS [BandingType],CONVERT(nvarchar(max),S.bandingValueStart) AS [Start],CONVERT(nvarchar(max),S.bandingValueEnd) AS [End],CONVERT(nvarchar(max),CONVERT(varchar(10),S.bandingStartDate,23)) AS [StartDate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.bandingEndDate,23)) AS [EndDate],CONVERT(nvarchar(max),S.amount) AS [EEMonthlyContribution],CONVERT(nvarchar(max),NULL) AS [Notes]';
        SET @LiveFrom=N'dbo.Premium S JOIN dbo.EmployerPlan P ON P.id=S.EmployerPlanId CROSS APPLY dbo.fn_ImportAnalysis_Context(@FileLogId) C';
    END;

    IF @Entity='Dependent'
    BEGIN
        SET @LiveProjection=N'CONVERT(nvarchar(max),C.PrimaryEIN) AS [PrimaryEIN],CONVERT(nvarchar(max),EM.taxid) AS [EINAssociatedWithEE],CONVERT(nvarchar(max),E.ssn) AS [EmployeeSSN],CONVERT(nvarchar(max),S.firstName) AS [DependentLegalFirstName],CONVERT(nvarchar(max),S.middleName) AS [DependentMiddleInitial],CONVERT(nvarchar(max),S.lastName) AS [DependentLegalLastName],CONVERT(nvarchar(max),S.suffix) AS [DependentSuffix],CONVERT(nvarchar(max),S.ssn) AS [DependentSSN],CONVERT(nvarchar(max),CONVERT(varchar(10),S.birthday,23)) AS [DependentBirthdate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.coverageStartDate,23)) AS [DependentCoverageStartDate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.coverageEndDate,23)) AS [DependentCoverageEndDate],CONVERT(nvarchar(max),S.Relationship) AS [Relationship]';
        SET @LiveFrom=N'dbo.CoveredIndividual S JOIN dbo.EmployeeCode EC ON EC.id=S.employeeCodeId JOIN dbo.Employee E ON E.id=EC.employeeId JOIN dbo.Employer EM ON EM.id=EC.employerId CROSS APPLY dbo.fn_ImportAnalysis_Context(@FileLogId) C';
    END;

    DECLARE @Base nvarchar(max)=N'SELECT A.RowId AS rowId,A.RowNumber AS rowNumber,A.RowSource AS rowSource,
        A.ActionType AS actionType,A.ValidationStatus AS validationStatus,A.ActionNote AS actionNote,
        S.ErrorMessages AS errorMessages,'+@Projection+N'
        FROM #Analysis A JOIN dbo.'+QUOTENAME(@Table)+N' S ON S.'+QUOTENAME(@Pk)+N'=A.RowId
        AND S.FileLogId=@FileLogId WHERE A.RowSource=''Upload''';
    IF @LiveFrom<>N'' SET @Base+=N' UNION ALL
        SELECT A.RowId,A.RowNumber,A.RowSource,A.ActionType,A.ValidationStatus,A.ActionNote,CONVERT(nvarchar(max),NULL),'+@LiveProjection+N'
        FROM '+@LiveFrom+N' JOIN #Analysis A ON A.RowSource=''Database'' AND S.id=A.RowId';

    -- Bind all values. Only catalog-approved identifiers/direction are concatenated.
    DECLARE @Pattern nvarchar(410)=NULL;
    IF NULLIF(LTRIM(RTRIM(@Search)),N'') IS NOT NULL
        SET @Pattern=N'%'+REPLACE(REPLACE(REPLACE(REPLACE(@Search,N'~',N'~~'),N'%',N'~%'),N'_',N'~_'),N'[',N'~[')+N'%';
    DECLARE @Where nvarchar(max)=N' WHERE (@ImpactFilter=''All'' OR R.actionType=@ImpactFilter)
        AND (@ValidationFilter=''All'' OR R.validationStatus=@ValidationFilter)
        AND (@Pattern IS NULL OR '+@SearchPredicate+N')';
    DECLARE @SortExpression nvarchar(300)=N'R.'+QUOTENAME(@SortColumn);
    IF @SortColumn IN ('EmployeeBirthdate','DependentBirthdate','StartDate','EndDate','StatusStartDate','StatusEndDate',
        'HireDate','TerminationDate','DateEligibleForCoverage','CoverageStartDate','CoverageEndDate',
        'DateEmployerFirstPaysToUnionBenefits','DateEmployerLastPaysToUnionBenefits','COBRACoverageEffectiveDate',
        'COBRACoverageEndDate','RetireePlanStartDate','RetireePlanEndDate','DependentCoverageStartDate','DependentCoverageEndDate')
        SET @SortExpression=N'TRY_CONVERT(date,'+@SortExpression+N')';
    ELSE IF @SortColumn IN ('PremiumCap','EEMonthlyContribution','W2IncomeOrAnnualSalary','AdditionalIncome','HourlyRate',
        'WaitingPeriodNumberOfDays','PlanRenewalMonth','Status')
        SET @SortExpression=N'TRY_CONVERT(decimal(38,10),'+@SortExpression+N')';
    DECLARE @Order nvarchar(500)=@SortExpression+N' '+@SortDirection+N',R.rowSource,R.rowId';
    DECLARE @Sql nvarchar(max)=N'
        SELECT (SELECT COUNT(*) FROM #Analysis) AS RecordsTotal,COUNT(*) AS RecordsFiltered FROM ('+@Base+N') R'+@Where+N';
        SELECT P.*,
            (SELECT ISNULL(E.ColumnName,V.TargetColumn) AS [column],V.Severity AS severity,ISNULL(E.Detail,V.ErrorMessage) AS message
             FROM dbo.StagingRowErrors E JOIN dbo.ValidationRules V ON V.RuleId=E.RuleId
             WHERE P.rowSource=''Upload'' AND E.FileLogId=@FileLogId AND E.StagingTableName=@TableName AND E.StagingRowId=P.rowId
               AND E.Status NOT IN (''Resolved'',''Fixed'',''Ignored'',''Dismissed'')
             ORDER BY E.StagingRowErrorId FOR JSON PATH) AS issuesJson
        FROM (SELECT R.* FROM ('+@Base+N') R'+@Where+N' ORDER BY '+@Order+N'
              OFFSET @Start ROWS FETCH NEXT @Length ROWS ONLY) P
        ORDER BY '+REPLACE(@SortExpression,N'R.',N'P.')+N' '+@SortDirection+N',P.rowSource,P.rowId;';
    EXEC sys.sp_executesql @Sql,
        N'@FileLogId int,@TableName sysname,@Start int,@Length int,@Pattern nvarchar(410),@ImpactFilter varchar(12),@ValidationFilter varchar(12)',
        @FileLogId,@Table,@Start,@Length,@Pattern,@ImpactFilter,@ValidationFilter;
END;
GO

-- sp_ImportAnalysis_ExportRows
CREATE OR ALTER PROCEDURE dbo.sp_ImportAnalysis_ExportRows
    @FileLogId int, @Entity varchar(20), @ImportMode varchar(20)='Merge'
AS
BEGIN
    SET NOCOUNT ON;
    IF @ImportMode IS NULL OR @ImportMode NOT IN ('Merge','Overwrite') THROW 51001,'Invalid import mode.',1;
    IF NOT EXISTS (SELECT 1 FROM dbo.fn_ImportAnalysis_Context(@FileLogId) WHERE PlanYear BETWEEN 1900 AND 9999)
        THROW 51002,'File or filing year is unavailable.',1;
    DECLARE @Table sysname,@Pk sysname;
    SELECT @Table=T,@Pk=P FROM (VALUES

        ('Employer','Staging_Employers','StagingEmployerId'),
        ('Plan','Staging_Plans','StagingPlanId'),
        ('Premium','Staging_Premiums','StagingPremiumId'),
        ('Employee','Staging_Employees','StagingEmployeeId'),
        ('Dependent','Staging_Dependents','StagingDependentId')
    ) M(E,T,P) WHERE E=@Entity;
    IF @Table IS NULL THROW 51007,'Invalid sheet.',1;
    DECLARE @Columns TABLE (Ordinal int,ColumnName sysname);

    IF @Entity='Employer' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'AffiliatedEIN'),
        (2,'EmployerName'),
        (3,'ForeignAddressInd'),
        (4,'Address'),
        (5,'Address2'),
        (6,'City'),
        (7,'StateOrProvince'),
        (8,'Zip'),
        (9,'Country'),
        (10,'Phone'),
        (11,'Contact'),
        (12,'Title'),
        (13,'OriginCode'),
        (14,'SHOPIdentifier'),
        (15,'Notes');

    IF @Entity='Plan' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'PlanName'),
        (2,'PlanType'),
        (3,'OfferedToSpouse'),
        (4,'OfferedToDependents'),
        (5,'WaitingPeriodNumberOfDays'),
        (6,'EligibleFirstOfTheMonth'),
        (7,'FundingType'),
        (8,'PlanRenewalMonth'),
        (9,'PlanTerminatesOnDateOfTermination'),
        (10,'MinimumValue'),
        (11,'BandingType'),
        (12,'PremiumCap');

    IF @Entity='Premium' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'PlanName'),
        (2,'BandingType'),
        (3,'Start'),
        (4,'End'),
        (5,'StartDate'),
        (6,'EndDate'),
        (7,'EEMonthlyContribution'),
        (8,'Notes');

    IF @Entity='Employee' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'EINAssociatedWithEE'),
        (2,'EmployeeLegalFirstName'),
        (3,'EmployeeMiddleInitial'),
        (4,'EmployeeLegalLastName'),
        (5,'EmployeeSuffix'),
        (6,'EmployeeSSN'),
        (7,'EmployeeBirthdate'),
        (8,'Status'),
        (9,'Expatriate'),
        (10,'StatusStartDate'),
        (11,'StatusEndDate'),
        (12,'HireDate'),
        (13,'TerminationDate'),
        (14,'W2IncomeOrAnnualSalary'),
        (15,'AdditionalIncome'),
        (16,'HourlyRate'),
        (17,'EmailAddress'),
        (18,'ForeignAddress'),
        (19,'Address1'),
        (20,'Address2'),
        (21,'City'),
        (22,'StateOrProvince'),
        (23,'Zip'),
        (24,'Country'),
        (25,'LowestCostPlanOffered'),
        (26,'DateEligibleForCoverage'),
        (27,'CoverageElected'),
        (28,'CoverageStartDate'),
        (29,'CoverageEndDate'),
        (30,'UnionEmployee'),
        (31,'DateEmployerFirstPaysToUnionBenefits'),
        (32,'DateEmployerLastPaysToUnionBenefits'),
        (33,'COBRABenefitsElected'),
        (34,'COBRACoverageEffectiveDate'),
        (35,'COBRACoverageEndDate'),
        (36,'RetireeBenefitsElected'),
        (37,'RetireePlanStartDate'),
        (38,'RetireePlanEndDate');

    IF @Entity='Dependent' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'EINAssociatedWithEE'),
        (2,'EmployeeSSN'),
        (3,'DependentLegalFirstName'),
        (4,'DependentMiddleInitial'),
        (5,'DependentLegalLastName'),
        (6,'DependentSuffix'),
        (7,'DependentSSN'),
        (8,'DependentBirthdate'),
        (9,'DependentCoverageStartDate'),
        (10,'DependentCoverageEndDate'),
        (11,'Relationship');

    SELECT * INTO #Analysis FROM dbo.fn_ImportAnalysis_Rows(@FileLogId,@Entity,@ImportMode) OPTION (RECOMPILE);
    CREATE UNIQUE CLUSTERED INDEX IX_Analysis_Source_Row ON #Analysis(RowSource,RowId);
    CREATE INDEX IX_Analysis_DefaultPage ON #Analysis(RowNumber,RowSource,RowId);

    DECLARE @Projection nvarchar(max),@LiveProjection nvarchar(max)=N'',@LiveFrom nvarchar(max)=N'';
    SELECT @Projection=STUFF((SELECT N',CONVERT(nvarchar(max),S.'+QUOTENAME(ColumnName)+N') AS '+QUOTENAME(ColumnName)
        FROM @Columns ORDER BY Ordinal FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,1,N'');
    IF @Entity='Premium'
    BEGIN
        SET @LiveProjection=N'CONVERT(nvarchar(max),C.PrimaryEIN) AS [PrimaryEIN],CONVERT(nvarchar(max),P.name) AS [PlanName],CONVERT(nvarchar(max),P.bandingType) AS [BandingType],CONVERT(nvarchar(max),S.bandingValueStart) AS [Start],CONVERT(nvarchar(max),S.bandingValueEnd) AS [End],CONVERT(nvarchar(max),CONVERT(varchar(10),S.bandingStartDate,23)) AS [StartDate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.bandingEndDate,23)) AS [EndDate],CONVERT(nvarchar(max),S.amount) AS [EEMonthlyContribution],CONVERT(nvarchar(max),NULL) AS [Notes]';
        SET @LiveFrom=N'dbo.Premium S JOIN dbo.EmployerPlan P ON P.id=S.EmployerPlanId CROSS APPLY dbo.fn_ImportAnalysis_Context(@FileLogId) C';
    END;

    IF @Entity='Dependent'
    BEGIN
        SET @LiveProjection=N'CONVERT(nvarchar(max),C.PrimaryEIN) AS [PrimaryEIN],CONVERT(nvarchar(max),EM.taxid) AS [EINAssociatedWithEE],CONVERT(nvarchar(max),E.ssn) AS [EmployeeSSN],CONVERT(nvarchar(max),S.firstName) AS [DependentLegalFirstName],CONVERT(nvarchar(max),S.middleName) AS [DependentMiddleInitial],CONVERT(nvarchar(max),S.lastName) AS [DependentLegalLastName],CONVERT(nvarchar(max),S.suffix) AS [DependentSuffix],CONVERT(nvarchar(max),S.ssn) AS [DependentSSN],CONVERT(nvarchar(max),CONVERT(varchar(10),S.birthday,23)) AS [DependentBirthdate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.coverageStartDate,23)) AS [DependentCoverageStartDate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.coverageEndDate,23)) AS [DependentCoverageEndDate],CONVERT(nvarchar(max),S.Relationship) AS [Relationship]';
        SET @LiveFrom=N'dbo.CoveredIndividual S JOIN dbo.EmployeeCode EC ON EC.id=S.employeeCodeId JOIN dbo.Employee E ON E.id=EC.employeeId JOIN dbo.Employer EM ON EM.id=EC.employerId CROSS APPLY dbo.fn_ImportAnalysis_Context(@FileLogId) C';
    END;

    DECLARE @Base nvarchar(max)=N'SELECT A.RowId AS rowId,A.RowNumber AS rowNumber,A.RowSource AS rowSource,
        A.ActionType AS actionType,A.ValidationStatus AS validationStatus,A.ActionNote AS actionNote,
        S.ErrorMessages AS errorMessages,'+@Projection+N'
        FROM #Analysis A JOIN dbo.'+QUOTENAME(@Table)+N' S ON S.'+QUOTENAME(@Pk)+N'=A.RowId
        AND S.FileLogId=@FileLogId WHERE A.RowSource=''Upload''';
    IF @LiveFrom<>N'' SET @Base+=N' UNION ALL
        SELECT A.RowId,A.RowNumber,A.RowSource,A.ActionType,A.ValidationStatus,A.ActionNote,CONVERT(nvarchar(max),NULL),'+@LiveProjection+N'
        FROM '+@LiveFrom+N' JOIN #Analysis A ON A.RowSource=''Database'' AND S.id=A.RowId';

    DECLARE @Sql nvarchar(max)=N'
        SELECT P.*,
            (SELECT ISNULL(E.ColumnName,V.TargetColumn) AS [column],V.Severity AS severity,ISNULL(E.Detail,V.ErrorMessage) AS message
             FROM dbo.StagingRowErrors E JOIN dbo.ValidationRules V ON V.RuleId=E.RuleId
             WHERE P.rowSource=''Upload'' AND E.FileLogId=@FileLogId AND E.StagingTableName=@TableName AND E.StagingRowId=P.rowId
               AND E.Status NOT IN (''Resolved'',''Fixed'',''Ignored'',''Dismissed'')
             ORDER BY E.StagingRowErrorId FOR JSON PATH) AS issuesJson
        FROM ('+@Base+N') P
        ORDER BY P.rowNumber,P.rowSource,P.rowId;';
    EXEC sys.sp_executesql @Sql,N'@FileLogId int,@TableName sysname',@FileLogId,@Table;
END;
GO

-- sp_GetStagingErrorsByRowId
CREATE OR ALTER PROCEDURE [dbo].[sp_GetStagingErrorsByRowId]
    @FileLogId INT,
    @StagingTableName NVARCHAR(128),
    @StagingRowId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn,
        ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status
    FROM StagingRowErrors e
    JOIN ValidationRules r ON e.RuleId = r.RuleId
    WHERE 
        e.FileLogId = @FileLogId
        AND e.StagingTableName = @StagingTableName
        AND e.StagingRowId = @StagingRowId;
END
GO

-- sp_GetStagingErrorsForTriage
CREATE OR ALTER PROCEDURE [dbo].[sp_GetStagingErrorsForTriage]   --92  
    @FileLogId INT    
AS    
BEGIN    
    SET NOCOUNT ON;    
    
    -- This query gets all errors and joins to the appropriate staging table    
    -- to retrieve the original Excel RowNumber for each error.    
    
    -- Get errors from the Staging_Employers table    
    SELECT     
        e.StagingRowErrorId,    
        e.StagingTableName,    
        e.StagingRowId,    
        st.RowNumber, -- Get the original Excel row number    
        ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn,    
        ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage,    
        r.Severity,    
        r.IsCorrectable,    
        r.HelpText,    
        e.Status,  
  r.AuditTabNote  
  
    FROM StagingRowErrors e    
    JOIN ValidationRules r ON e.RuleId = r.RuleId    
    JOIN Staging_Employers st ON e.StagingRowId = st.StagingEmployerId    
    WHERE e.FileLogId = @FileLogId AND e.StagingTableName = 'Staging_Employers'    
    
    UNION ALL    
    
    -- Get errors from the Staging_Plans table    
    SELECT e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber, ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status,r.AuditTabNote    
    FROM StagingRowErrors e JOIN ValidationRules r ON e.RuleId = r.RuleId JOIN Staging_Plans st ON e.StagingRowId = st.StagingPlanId    
    WHERE e.FileLogId = @FileLogId AND e.StagingTableName = 'Staging_Plans'    
    
    UNION ALL    
    
    -- Get errors from the Staging_Premiums table    
    SELECT e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber, ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status,r.AuditTabNote      
    FROM StagingRowErrors e JOIN ValidationRules r ON e.RuleId = r.RuleId JOIN Staging_Premiums st ON e.StagingRowId = st.StagingPremiumId    
    WHERE e.FileLogId = @FileLogId AND e.StagingTableName = 'Staging_Premiums'    
    
    UNION ALL    
    
    -- Get errors from the Staging_Employees table    
    SELECT e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber, ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status,r.AuditTabNote      
    FROM StagingRowErrors e JOIN ValidationRules r ON e.RuleId = r.RuleId JOIN Staging_Employees st ON e.StagingRowId = st.StagingEmployeeId    
    WHERE e.FileLogId = @FileLogId AND e.StagingTableName = 'Staging_Employees'    
    
    UNION ALL    
    
    -- Get errors from the Staging_Dependents table    
    SELECT e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber, ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status,r.AuditTabNote      
    FROM StagingRowErrors e JOIN ValidationRules r ON e.RuleId = r.RuleId JOIN Staging_Dependents st ON e.StagingRowId = st.StagingDependentId    
    WHERE e.FileLogId = @FileLogId AND e.StagingTableName = 'Staging_Dependents';    
    
END
GO

-- sp_GetStagingErrorsForTriage_with_filter
CREATE OR ALTER PROCEDURE [dbo].[sp_GetStagingErrorsForTriage_with_filter]          
(         
    @FileLogId INT,         
    @PageIndex INT = NULL,         
    @PageSize INT = NULL,         
    @Search NVARCHAR(100) = NULL,         
    @SortColumn INT = null,  -- 0=RowNumber, 1=ErrorMessage, 2=Severity         
    @SortOrder VARCHAR(5) = 'asc',         
    @Severity NVARCHAR(50) = NULL, 
    @SearchColumn NVARCHAR(100) = NULL -- [NEW PARAMETER IN USE]
)         
AS         
BEGIN         
    DECLARE @RecordCount INT;         
    DECLARE @StartPage INT;         
    DECLARE @EndPage INT;         
    DECLARE @ErrorCount INT = 0;  
    DECLARE @WarningCount INT = 0;  
    DECLARE @InformationCount INT = 0;  
    DECLARE @EmployerActionCount INT = 0;  
    DECLARE @TotalErrorsCount INT = 0;    

    BEGIN TRY         
        BEGIN TRANSACTION         
        SET NOCOUNT ON;         
        
        CREATE TABLE #AllErrors         
        (         
            StagingRowErrorId INT,         
            StagingTableName NVARCHAR(100),         
            StagingRowId INT,         
            RowNumber INT,         
            TargetColumn NVARCHAR(100),         
            ErrorMessage NVARCHAR(MAX),         
            Severity NVARCHAR(50),         
            IsCorrectable BIT,         
            HelpText NVARCHAR(MAX),         
            Status NVARCHAR(50)         
        );         
        
        -- ==========================================================================
        -- FILTER LOGIC HELPER
        -- We apply this same logic to every INSERT statement below
        -- ==========================================================================
        -- Logic: 
        -- 1. If @Search is empty, match everything.
        -- 2. If @SearchColumn is empty, search ALL fields (Message, Column, Table).
        -- 3. If @SearchColumn is set, search ONLY that field.
        
        -- Special handling for Employer Action filter  
        IF @Severity = 'Employer_Action'  
        BEGIN  
            INSERT INTO #AllErrors         
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Employers st ON e.StagingRowId = st.StagingEmployerId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Employers'         
                AND r.IsCorrectable = 0  
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )

            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Plans st ON e.StagingRowId = st.StagingPlanId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Plans'         
                AND r.IsCorrectable = 0  
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )

            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Premiums st ON e.StagingRowId = st.StagingPremiumId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Premiums'         
                AND r.IsCorrectable = 0  
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )

            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Employees st ON e.StagingRowId = st.StagingEmployeeId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Employees'         
                AND r.IsCorrectable = 0  
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )

            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Dependents st ON e.StagingRowId = st.StagingDependentId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Dependents'         
                AND r.IsCorrectable = 0
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                );  
        END  
        ELSE  
        BEGIN  
            -- STANDARD FILTER LOGIC (Not Employer Action)
            
            INSERT INTO #AllErrors         
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Employers st ON e.StagingRowId = st.StagingEmployerId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Employers'         
                AND (ISNULL(@Severity, '') = '' OR r.Severity = @Severity)         
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )
        
            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Plans st ON e.StagingRowId = st.StagingPlanId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Plans'         
                AND (ISNULL(@Severity, '') = '' OR r.Severity = @Severity)         
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )
        
            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Premiums st ON e.StagingRowId = st.StagingPremiumId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Premiums'         
                AND (ISNULL(@Severity, '') = '' OR r.Severity = @Severity)         
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )
        
            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Employees st ON e.StagingRowId = st.StagingEmployeeId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Employees'         
                AND (ISNULL(@Severity, '') = '' OR r.Severity = @Severity)         
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )
        
            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Dependents st ON e.StagingRowId = st.StagingDependentId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Dependents'         
                AND (ISNULL(@Severity, '') = '' OR r.Severity = @Severity)
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                );  
        END  
        
        -- Get record count         
        SELECT @RecordCount = COUNT(*) FROM #AllErrors;         
        SELECT @TotalErrorsCount = @RecordCount;  
          
        -- Get severity counts (filtered by @Severity if provided)  
        IF @Severity = 'Employer Action'  
        BEGIN  
            SELECT @EmployerActionCount = @RecordCount;  
        END  
        ELSE  
        BEGIN  
            SELECT @ErrorCount = COUNT(*) FROM #AllErrors WHERE Severity = 'Error'    
                AND (ISNULL(@Severity, '') = '' OR @Severity = 'Error');  
            SELECT @WarningCount = COUNT(*) FROM #AllErrors WHERE Severity = 'Warning'    
                AND (ISNULL(@Severity, '') = '' OR @Severity = 'Warning');  
            SELECT @InformationCount = COUNT(*) FROM #AllErrors WHERE Severity = 'Information'    
                AND (ISNULL(@Severity, '') = '' OR @Severity = 'Information');  
            SELECT @EmployerActionCount = COUNT(*) FROM #AllErrors WHERE IsCorrectable = 0;  
        END  
          
        SET @StartPage = (@PageIndex - 1) * @PageSize + 1;         
        SET @EndPage = @PageIndex * @PageSize;         
        
        -- Get paginated results with sorting         
        SELECT          
            StagingRowErrorId,         
            StagingTableName,         
            StagingRowId,         
            RowNumber,         
            TargetColumn,         
            ErrorMessage,         
            Severity,         
            IsCorrectable,         
            HelpText,         
            Status         
        FROM #AllErrors         
        ORDER BY         
            CASE WHEN (@SortColumn = 0 AND @SortOrder = 'asc') THEN RowNumber END ASC,         
            CASE WHEN (@SortColumn = 0 AND @SortOrder = 'desc') THEN RowNumber END DESC,         
            CASE WHEN (@SortColumn = 1 AND @SortOrder = 'asc') THEN ErrorMessage END ASC,         
            CASE WHEN (@SortColumn = 1 AND @SortOrder = 'desc') THEN ErrorMessage END DESC,         
            CASE WHEN (@SortColumn = 2 AND @SortOrder = 'asc') THEN Severity END ASC,         
            CASE WHEN (@SortColumn = 2 AND @SortOrder = 'desc') THEN Severity END DESC         
        OFFSET @PageSize * (@PageIndex - 1) ROWS         
        FETCH NEXT @PageSize ROWS ONLY;         
        
        -- Return pagination metadata and counts         
        SELECT        
            @RecordCount AS [RowCount],         
            @StartPage AS StartPage,         
            CASE WHEN @EndPage > @RecordCount THEN @RecordCount ELSE @EndPage END AS EndPage,         
            @PageIndex AS PageNumber,         
            @RecordCount AS TotalCount  
            
        SELECT    
            @ErrorCount AS ErrorCount,  
            @WarningCount AS WarningCount,  
            @InformationCount AS InformationCount,  
            @EmployerActionCount AS EmployerActionCount,  
            @TotalErrorsCount As TotalErrorsCount;  
        
        DROP TABLE #AllErrors;         
        COMMIT TRANSACTION;         
    END TRY         
    BEGIN CATCH         
        IF OBJECT_ID('tempdb..#AllErrors') IS NOT NULL         
            DROP TABLE #AllErrors;         
        
        INSERT INTO dbo.DB_Errors         
        VALUES         
        (         
            SUSER_SNAME(),         
            ERROR_NUMBER(),         
            ERROR_STATE(),         
            ERROR_SEVERITY(),         
            ERROR_LINE(),         
            ERROR_PROCEDURE(),         
            ERROR_MESSAGE(),         
            GETDATE()         
        );         
        
        IF (XACT_STATE()) = -1         
            ROLLBACK TRANSACTION;         
        
        IF (XACT_STATE()) = 1         
            COMMIT TRANSACTION;         
        
        THROW;         
    END CATCH         
END
GO

-- sp_GetValidationReport
CREATE OR ALTER PROCEDURE [dbo].[sp_GetValidationReport]
    @FileLogId INT,
    @TableName NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Sql NVARCHAR(MAX);
    DECLARE @Cols NVARCHAR(MAX);
    DECLARE @PKColumn NVARCHAR(100);

    -- 1. Determine the Primary Key Column
    SET @PKColumn = 'Staging' + REPLACE(REPLACE(@TableName, 'Staging_', ''), 's', '') + 'Id';

    -- 2. Get List of Columns
    SELECT @Cols = STRING_AGG(QUOTENAME(c.name), ', ') WITHIN GROUP (ORDER BY c.column_id)
    FROM sys.columns c
    INNER JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = @TableName
      AND c.name NOT IN ('FileLogId', 'IsValid', 'CreatedDate', 'ModifiedDate') 
      AND c.name <> @PKColumn; 

    -- 3. Build Query
    SET @Sql = N'
    SELECT 
        S.RowNumber,
        CASE 
            WHEN SUM(CASE WHEN R.Severity = ''Error'' THEN 1 ELSE 0 END) > 0 THEN ''Failed''
            WHEN SUM(CASE WHEN R.Severity = ''Warning'' THEN 1 ELSE 0 END) > 0 THEN ''Warning''
            ELSE ''Passed''
        END AS [Validation Status],
        ISNULL(STRING_AGG(CAST(R.ErrorMessage AS NVARCHAR(MAX)), '' | ''), '''') AS [Error Messages],
        
        -- NEW: Comma-separated lists of columns that have errors or warnings!
        ISNULL(STRING_AGG(CASE WHEN R.Severity = ''Error'' THEN CAST(R.TargetColumn AS NVARCHAR(MAX)) ELSE NULL END, '',''), '''') AS [ErrorColumns],
        ISNULL(STRING_AGG(CASE WHEN R.Severity = ''Warning'' THEN CAST(R.TargetColumn AS NVARCHAR(MAX)) ELSE NULL END, '',''), '''') AS [WarningColumns],
        
        ' + @Cols + '
    FROM ' + @TableName + ' S
    LEFT JOIN (
        SELECT E.StagingRowId, ISNULL(E.Detail, V.ErrorMessage) AS ErrorMessage, V.Severity, ISNULL(E.ColumnName, V.TargetColumn) AS TargetColumn
        FROM StagingRowErrors E
        JOIN ValidationRules V ON E.RuleId = V.RuleId
        WHERE E.FileLogId = @FileLogId AND E.StagingTableName = ''' + @TableName + '''
    ) R ON S.' + @PKColumn + ' = R.StagingRowId 
    WHERE S.FileLogId = @FileLogId
    GROUP BY S.RowNumber, S.' + @PKColumn + ', ' + @Cols + '
    ORDER BY S.RowNumber;';

    -- 4. Execute
    EXEC sp_executesql @Sql, N'@FileLogId INT', @FileLogId;
END
GO

PRINT '055: validation rules fixed (lookups, rule 19, affordability bands, ZIP by state); auto-corrections recorded.';
GO

/* ############################################################################
   056_triage_fixes.sql
   ############################################################################ */
PRINT '=== 056_triage_fixes.sql';
GO
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
GO

/* ############################################################################
   057_import_what_changed.sql
   ############################################################################ */
PRINT '=== 057_import_what_changed.sql';
GO
/* =============================================================================
   057 - Import Analysis: "What changed"

   dbo.sp_ImportAnalysis_Changes compares a validated file with what the system
   already holds for its employer and filing year, the way fn_ImportAnalysis_Impact
   matches rows (employer by EIN and year, employee by employer and SSN, plan by
   name), and says what an import would change:
     - new employees, leavers and termination dates, full-time / part-time changes,
       hire dates, coverage, names / addresses / other details (each field, before
       and after);
     - employees the system has for that employer and year that are not in the file
       (an import leaves them as they are: a sign of a partial file);
     - new dependents, plan changes, premium changes, employer details.
   Only rows that will be imported are compared (IsValid = 1).

   Result sets: 1) Kind, Label, Changes (every kind, in order, zero included);
                2) one page of changes: Kind, Entity, RowNumber, Name, Field,
                   OldValue, NewValue, Note, TotalCount.
   @PageSize NULL returns every change (the Excel export).
   Read-only. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_ImportAnalysis_Changes
    @FileLogId  INT,
    @Kind       VARCHAR(20)   = NULL,
    @Search     NVARCHAR(200) = NULL,
    @PageIndex  INT           = 1,
    @PageSize   INT           = 25
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageIndex IS NULL OR @PageIndex < 1 SET @PageIndex = 1;
    IF @PageSize IS NOT NULL AND @PageSize NOT BETWEEN 1 AND 500 SET @PageSize = 25;
    SET @Search = NULLIF(LTRIM(RTRIM(@Search)), N'');
    SET @Kind = NULLIF(@Kind, '');

    DECLARE @PlanYear INT, @Parent INT;
    SELECT @PlanYear = PlanYear, @Parent = ParentEmployerId FROM dbo.fn_ImportAnalysis_Context(@FileLogId);

    CREATE TABLE #K (Kind VARCHAR(20) PRIMARY KEY, Label NVARCHAR(100), Seq INT);
    INSERT #K VALUES
        ('NEW_EMPLOYEE', N'New employees', 1),
        ('TERMINATED',   N'Leavers and termination dates', 2),
        ('STATUS',       N'Full-time / part-time changes', 3),
        ('HIRE',         N'Hire date changes', 4),
        ('COVERAGE',     N'Coverage changes', 5),
        ('DETAILS',      N'Names, addresses and other details', 6),
        ('MISSING',      N'In the system but not in this file', 7),
        ('DEPENDENT',    N'New dependents', 8),
        ('PLAN',         N'Plan changes', 9),
        ('PREMIUM',      N'Premium changes', 10),
        ('EMPLOYER',     N'Employer details', 11);

    CREATE TABLE #C (Kind VARCHAR(20) NOT NULL, Entity VARCHAR(20) NOT NULL, RowNumber INT NULL, Name NVARCHAR(300) NULL,
                     Field NVARCHAR(100) NULL, OldValue NVARCHAR(400) NULL, NewValue NVARCHAR(400) NULL, Note NVARCHAR(300) NULL);

    -- Display forms: dates as MM/DD/YYYY, status as words, yes/no flags as Yes/No.
    -- ---------------------------------------------------------------- employees
    ;WITH S AS
    (
        SELECT S.StagingEmployeeId, S.RowNumber, S.EmployeeSSN,
               LTRIM(RTRIM(CONCAT(S.EmployeeLegalFirstName, N' ', S.EmployeeLegalLastName))) AS PersonName,
               S.EmployeeLegalFirstName, S.EmployeeMiddleInitial, S.EmployeeLegalLastName, S.EmployeeSuffix,
               TRY_CONVERT(date, S.EmployeeBirthdate) AS Birth, S.Address1, S.Address2, S.City, S.StateOrProvince, S.Zip, S.EmailAddress,
               TRY_CONVERT(date, S.HireDate) AS Hire, TRY_CONVERT(date, S.TerminationDate) AS Term, TRY_CONVERT(int, S.Status) AS Status,
               TRY_CONVERT(bit, S.CoverageElected) AS Elected, TRY_CONVERT(date, S.CoverageStartDate) AS CovStart,
               TRY_CONVERT(date, S.CoverageEndDate) AS CovEnd, TRY_CONVERT(date, S.DateEligibleForCoverage) AS Eligible,
               S.LowestCostPlanOffered,
               T.id AS EmployeeId, T.firstName AS O_First, T.middleName AS O_Middle, T.lastName AS O_Last, T.suffix AS O_Suffix, T.birthday AS O_Birth, T.address AS O_Address, T.address2 AS O_Address2, T.city AS O_City,
               T.state AS O_State, T.zip AS O_Zip, T.email AS O_Email, T.HireDate AS O_Hire, T.TerminationDate AS O_Term, T.Status AS OldStatus, T.CoverageElected AS O_Elected,
               T.CoverageStartDate AS O_CovStart, T.CoverageEndDate AS O_CovEnd, T.DateEligibleForCoverage AS O_Eligible, T.LowestCostPlanOffered AS OldPlan
        FROM   dbo.Staging_Employees S
        OUTER  APPLY (SELECT TOP (1) E.id FROM dbo.Employer E WHERE E.taxid = S.EINAssociatedWithEE AND E.filingYear = @PlanYear ORDER BY E.id) A
        OUTER  APPLY (SELECT TOP (1) E.* FROM dbo.Employee E WHERE E.EmployerId = COALESCE(A.id, @Parent)
                      AND E.ssn = CONVERT(varchar(50), S.EmployeeSSN) ORDER BY E.id) T
        WHERE  S.FileLogId = @FileLogId AND S.IsValid = 1
    )
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'NEW_EMPLOYEE', 'Employee', S.RowNumber, S.PersonName + N' ' + NCHAR(183) + N' ' + RIGHT(REPLACE(S.EmployeeSSN, '-', ''), 4),
           N'Hire date', NULL, CONVERT(varchar(10), S.Hire, 101), NULL
    FROM   S WHERE S.EmployeeId IS NULL
    UNION ALL
    SELECT V.Kind, 'Employee', S.RowNumber, S.PersonName + N' ' + NCHAR(183) + N' ' + RIGHT(REPLACE(S.EmployeeSSN, '-', ''), 4),
           V.Field, V.OldValue, V.NewValue,
           CASE WHEN V.Kind = 'TERMINATED' AND V.OldValue IS NULL THEN N'Leaving'
                WHEN V.Kind = 'TERMINATED' AND V.NewValue IS NULL THEN N'Termination date removed' END
    FROM   S
    CROSS  APPLY (VALUES
        ('DETAILS',    N'First name',       S.O_First,    S.EmployeeLegalFirstName),
        ('DETAILS',    N'Middle initial',   S.O_Middle,   S.EmployeeMiddleInitial),
        ('DETAILS',    N'Last name',        S.O_Last,     S.EmployeeLegalLastName),
        ('DETAILS',    N'Suffix',           S.O_Suffix,   S.EmployeeSuffix),
        ('DETAILS',    N'Birth date',       CONVERT(varchar(10), S.O_Birth, 101), CONVERT(varchar(10), S.Birth, 101)),
        ('DETAILS',    N'Address',          S.O_Address,  S.Address1),
        ('DETAILS',    N'Address 2',        S.O_Address2, S.Address2),
        ('DETAILS',    N'City',             S.O_City,     S.City),
        ('DETAILS',    N'State',            S.O_State,    S.StateOrProvince),
        ('DETAILS',    N'ZIP',              S.O_Zip,      S.Zip),
        ('DETAILS',    N'Email',            S.O_Email,    S.EmailAddress),
        ('HIRE',       N'Hire date',        CONVERT(varchar(10), S.O_Hire, 101), CONVERT(varchar(10), S.Hire, 101)),
        ('TERMINATED', N'Termination date', CONVERT(varchar(10), S.O_Term, 101), CONVERT(varchar(10), S.Term, 101)),
        ('STATUS',     N'Status',
                       CASE S.OldStatus WHEN 1 THEN N'Full-time' WHEN 2 THEN N'Part-time' ELSE CONVERT(nvarchar(10), S.OldStatus) END,
                       CASE S.Status    WHEN 1 THEN N'Full-time' WHEN 2 THEN N'Part-time' ELSE CONVERT(nvarchar(10), S.Status) END),
        ('COVERAGE',   N'Coverage elected',
                       CASE S.O_Elected WHEN 1 THEN N'Yes' WHEN 0 THEN N'No' END,
                       CASE S.Elected         WHEN 1 THEN N'Yes' WHEN 0 THEN N'No' END),
        ('COVERAGE',   N'Coverage start',   CONVERT(varchar(10), S.O_CovStart, 101), CONVERT(varchar(10), S.CovStart, 101)),
        ('COVERAGE',   N'Coverage end',     CONVERT(varchar(10), S.O_CovEnd, 101), CONVERT(varchar(10), S.CovEnd, 101)),
        ('COVERAGE',   N'Eligible from',    CONVERT(varchar(10), S.O_Eligible, 101), CONVERT(varchar(10), S.Eligible, 101)),
        ('COVERAGE',   N'Lowest cost plan', S.OldPlan,    S.LowestCostPlanOffered)
    ) V (Kind, Field, OldValue, NewValue)
    WHERE  S.EmployeeId IS NOT NULL
      AND  ISNULL(NULLIF(LTRIM(RTRIM(V.OldValue)), N''), N'') <> ISNULL(NULLIF(LTRIM(RTRIM(V.NewValue)), N''), N'');

    -- ------------------------------------------- in the system, not in the file
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'MISSING', 'Employee', NULL,
           LTRIM(RTRIM(CONCAT(E.firstName, N' ', E.lastName))) + N' ' + NCHAR(183) + N' ' + RIGHT(REPLACE(E.ssn, '-', ''), 4),
           N'Employer', G.name, NULL, N'Not in this file. The import leaves this employee as it is.'
    FROM   dbo.Employer G
    JOIN   dbo.Employee E ON E.EmployerId = G.id AND E.IsDeleted = 0
    WHERE  (G.id = @Parent OR G.companyId = @Parent) AND G.filingYear = @PlanYear
      AND  EXISTS (SELECT 1 FROM dbo.EmployeeCode X WHERE X.employeeId = E.id AND X.filingYear = @PlanYear)
      AND  NOT EXISTS (SELECT 1 FROM dbo.Staging_Employees S
                       WHERE S.FileLogId = @FileLogId AND CONVERT(varchar(50), S.EmployeeSSN) = E.ssn);

    -- ------------------------------------------------------------ dependents
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'DEPENDENT', 'Dependent', D.RowNumber,
           LTRIM(RTRIM(CONCAT(D.DependentLegalFirstName, N' ', D.DependentLegalLastName))),
           N'Employee SSN ending', NULL, RIGHT(REPLACE(D.EmployeeSSN, '-', ''), 4), NULL
    FROM   dbo.fn_ImportAnalysis_Impact(@FileLogId, 'Dependent', 'Merge') A
    JOIN   dbo.Staging_Dependents D ON D.StagingDependentId = A.RowId
    WHERE  A.RowSource = 'Upload' AND A.ActionType = 'INSERT' AND D.IsValid = 1;

    -- ----------------------------------------------------------------- plans
    ;WITH P AS
    (
        SELECT S.RowNumber, S.PlanName, T.id AS PlanId,
               T.offeredSpouse, T.offeredDependents, T.minimumValue, T.fundingType, T.bandingType, T.planRenewal, T.waitingDays,
               T.eligibile1stOfMonth, T.planTermTermination,
               TRY_CONVERT(smallint, S.OfferedToSpouse) AS Spouse, TRY_CONVERT(smallint, S.OfferedToDependents) AS Dependents,
               TRY_CONVERT(smallint, S.MinimumValue) AS MV, S.FundingType AS NewFunding, S.BandingType AS NewBanding, TRY_CONVERT(int, S.PlanRenewalMonth) AS Renewal,
               TRY_CONVERT(int, S.WaitingPeriodNumberOfDays) AS Waiting, TRY_CONVERT(smallint, S.EligibleFirstOfTheMonth) AS FirstOfMonth,
               TRY_CONVERT(smallint, S.PlanTerminatesOnDateOfTermination) AS EndsAtTermination
        FROM   dbo.Staging_Plans S
        OUTER  APPLY (SELECT TOP (1) EP.* FROM dbo.Employer E JOIN dbo.EmployerPlan EP ON EP.employerId = E.id
                      WHERE E.taxid = S.PrimaryEIN AND E.filingYear = @PlanYear AND EP.name = S.PlanName ORDER BY EP.id) T
        WHERE  S.FileLogId = @FileLogId AND S.IsValid = 1
    )
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'PLAN', 'Plan', P.RowNumber, P.PlanName, N'Plan', NULL, P.PlanName, N'New plan'
    FROM   P WHERE P.PlanId IS NULL
    UNION ALL
    SELECT 'PLAN', 'Plan', P.RowNumber, P.PlanName, V.Field, V.OldValue, V.NewValue, NULL
    FROM   P
    CROSS  APPLY (VALUES
        (N'Offered to spouse',      CONVERT(nvarchar(20), P.offeredSpouse),       CONVERT(nvarchar(20), P.Spouse)),
        (N'Offered to dependents',  CONVERT(nvarchar(20), P.offeredDependents),   CONVERT(nvarchar(20), P.Dependents)),
        (N'Minimum value',          CONVERT(nvarchar(20), P.minimumValue),        CONVERT(nvarchar(20), P.MV)),
        (N'Funding type',           CONVERT(nvarchar(20), P.fundingType),         CONVERT(nvarchar(20), P.NewFunding)),
        (N'Banding type',           CONVERT(nvarchar(50), P.bandingType),         CONVERT(nvarchar(50), P.NewBanding)),
        (N'Renewal month',          CONVERT(nvarchar(20), P.planRenewal),         CONVERT(nvarchar(20), P.Renewal)),
        (N'Waiting days',           CONVERT(nvarchar(20), P.waitingDays),         CONVERT(nvarchar(20), P.Waiting)),
        (N'Eligible first of month', CONVERT(nvarchar(20), P.eligibile1stOfMonth), CONVERT(nvarchar(20), P.FirstOfMonth)),
        (N'Ends at termination',    CONVERT(nvarchar(20), P.planTermTermination), CONVERT(nvarchar(20), P.EndsAtTermination))
    ) V (Field, OldValue, NewValue)
    WHERE  P.PlanId IS NOT NULL AND ISNULL(V.OldValue, N'') <> ISNULL(V.NewValue, N'');

    -- -------------------------------------------------------------- premiums
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'PREMIUM', 'Premium', S.RowNumber, S.PlanName,
           N'EE monthly contribution from ' + ISNULL(CONVERT(varchar(10), TRY_CONVERT(date, S.StartDate), 101), N'?'),
           (SELECT TOP (1) CONVERT(nvarchar(20), T.amount) FROM dbo.Premium T JOIN dbo.EmployerPlan EP ON EP.id = T.EmployerPlanId
            JOIN dbo.Employer E ON E.id = EP.employerId
            WHERE E.taxid = S.PrimaryEIN AND E.filingYear = @PlanYear AND EP.name = S.PlanName
              AND T.bandingStartDate = TRY_CONVERT(date, S.StartDate) ORDER BY T.id),
           CONVERT(nvarchar(20), TRY_CONVERT(decimal(9,2), S.EEMonthlyContribution)),
           CASE WHEN ISNULL(S.Start, N'') <> N'' AND ISNUMERIC(S.Start) = 1 THEN N'Band ' + S.Start + NCHAR(8211) + ISNULL(S.[End], N'') END
    FROM   dbo.fn_ImportAnalysis_Impact(@FileLogId, 'Premium', 'Merge') A
    JOIN   dbo.Staging_Premiums S ON S.StagingPremiumId = A.RowId
    WHERE  A.RowSource = 'Upload' AND A.ActionType = 'INSERT' AND S.IsValid = 1;

    -- -------------------------------------------------------------- employer
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'EMPLOYER', 'Employer', S.RowNumber, ISNULL(T.name, S.EmployerName), V.Field, V.OldValue, V.NewValue,
           CASE WHEN T.id IS NULL THEN N'New employer' END
    FROM   dbo.Staging_Employers S
    OUTER  APPLY (SELECT TOP (1) E.* FROM dbo.Employer E WHERE E.taxid = COALESCE(NULLIF(S.AffiliatedEIN, ''), S.PrimaryEIN)
                  AND E.filingYear = @PlanYear ORDER BY E.id) T
    CROSS  APPLY (VALUES
        (N'Name',      T.name,        S.EmployerName),
        (N'Address',   T.address,     S.Address),
        (N'Address 2', T.address2,    S.Address2),
        (N'City',      T.city,        S.City),
        (N'State',     T.state,       S.StateOrProvince),
        (N'ZIP',       T.zip,         S.Zip),
        (N'Contact',   T.contactName, S.Contact),
        (N'Phone',     T.phoneNumber, S.Phone),
        (N'Title',     T.signTitle,   S.Title)
    ) V (Field, OldValue, NewValue)
    WHERE  S.FileLogId = @FileLogId AND S.IsValid = 1
      AND  (T.id IS NULL AND V.Field = N'Name'
            OR T.id IS NOT NULL AND V.NewValue IS NOT NULL
               AND ISNULL(LTRIM(RTRIM(V.OldValue)), N'') <> ISNULL(LTRIM(RTRIM(V.NewValue)), N''));

    -- ---------------------------------------------------------------- results
    SELECT K.Kind, K.Label, COUNT(C.Kind) AS Changes
    FROM   #K K LEFT JOIN #C C ON C.Kind = K.Kind
    GROUP  BY K.Kind, K.Label, K.Seq
    ORDER  BY K.Seq;

    SELECT C.Kind, C.Entity, C.RowNumber, C.Name, C.Field, C.OldValue, C.NewValue, C.Note, COUNT(*) OVER () AS TotalCount
    FROM   #C C JOIN #K K ON K.Kind = C.Kind
    WHERE  (@Kind IS NULL OR C.Kind = @Kind)
      AND  (@Search IS NULL OR C.Name LIKE N'%' + @Search + N'%' OR C.Field LIKE N'%' + @Search + N'%'
            OR C.OldValue LIKE N'%' + @Search + N'%' OR C.NewValue LIKE N'%' + @Search + N'%')
    ORDER  BY K.Seq, C.RowNumber, C.Name, C.Field
    OFFSET CASE WHEN @PageSize IS NULL THEN 0 ELSE (@PageIndex - 1) * @PageSize END ROWS
    FETCH  NEXT ISNULL(@PageSize, 2147483647) ROWS ONLY;
END
GO
GO

/* ############################################################################
   058_code_explanations.sql
   ############################################################################ */
PRINT '=== 058_code_explanations.sql';
GO
/* =============================================================================
   058 - Why this code? (1095-C Line 14 / 15 / 16, month by month)

   sp_Generate1095Codes works each month out from the employee's hire spans,
   full-time status, plan offered, its premium, enrolment, union, COBRA, ICHRA
   and pay, and then threw that work away. It now keeps it, per employee code and
   month, in dbo.EmployeeCodeMonthDetail, with the year's limits it compared
   against (FPL, affordability %, full-time hours). The app explains each month's
   codes from these values, and says when the filed codes were changed by hand.

   Employees whose codes are locked or have coding switched off are not
   recalculated, so they keep the explanation of their last calculation (none
   until codes are generated once after this script).

   sp_Generate1095Codes is the database's current one with only the 058 block
   added before its temp tables are dropped. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.EmployeeCodeMonthDetail') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeCodeMonthDetail
    (
        EmployeeCodeId    INT            NOT NULL,
        MonthNum          TINYINT        NOT NULL,
        Employed          BIT            NULL,
        IsFullTime        BIT            NULL,
        PlanId            INT            NULL,
        PlanName          NVARCHAR(200)  NULL,
        MinimumValue      BIT            NULL,
        OfferedSpouse     INT            NULL,
        ConditionalSpouse INT            NULL,
        OfferedDependents INT            NULL,
        FundingType       NVARCHAR(10)   NULL,
        Code1A            BIT            NULL,
        Premium           DECIMAL(18,2)  NULL,
        Enrolled          BIT            NULL,
        UnionMonth        BIT            NULL,
        CobraMonth        BIT            NULL,
        IsIchra           BIT            NULL,
        IchraLcsp         DECIMAL(18,2)  NULL,
        IchraAllowance    DECIMAL(18,2)  NULL,
        IchraNetCost      DECIMAL(18,2)  NULL,
        HourlyRate        DECIMAL(18,4)  NULL,
        AnnualW2          DECIMAL(18,4)  NULL,
        HiredMonths       INT            NULL,
        MonthlyFpl        DECIMAL(10,5)  NULL,
        AffordabilityPct  DECIMAL(10,5)  NULL,
        FullTimeHours     INT            NULL,
        HireDate          DATE           NULL,
        TerminationDate   DATE           NULL,
        EligibleDate      DATE           NULL,
        Line14            VARCHAR(2)     NULL,
        Line15            DECIMAL(18,2)  NULL,
        Line16            VARCHAR(2)     NULL,
        CalculatedAt      DATETIME2(0)   NOT NULL CONSTRAINT DF_EmployeeCodeMonthDetail_At DEFAULT SYSUTCDATETIME(),
        CalculatedBy      NVARCHAR(128)  NULL,
        CONSTRAINT PK_EmployeeCodeMonthDetail PRIMARY KEY (EmployeeCodeId, MonthNum)
    );
END
GO

------------------------------------------------------------
-- One employee's year: the 12 months as calculated, and the codes on file now.
------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_GetCodeExplanation
    @EmployeeId INT,
    @FilingYear INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) EC.id AS EmployeeCodeId, EC.employerId AS EmployerId, EC.filingYear AS FilingYear,
           CAST(ISNULL(EC.IsLocked, 0) AS BIT) AS IsLocked, CAST(ISNULL(EC.disableCoding, 0) AS BIT) AS DisableCoding,
           LTRIM(RTRIM(CONCAT(E.firstName, N' ', E.lastName))) AS EmployeeName, ER.name AS EmployerName,
           EC.JAN_COC, EC.FEB_COC, EC.MAR_COC, EC.APR_COC, EC.MAY_COC, EC.JUN_COC, EC.JUL_COC, EC.AUG_COC, EC.SEP_COC, EC.OCT_COC, EC.NOV_COC, EC.DEC_COC,
           EC.JAN_LCMP, EC.FEB_LCMP, EC.MAR_LCMP, EC.APR_LCMP, EC.MAY_LCMP, EC.JUN_LCMP, EC.JUL_LCMP, EC.AUG_LCMP, EC.SEP_LCMP, EC.OCT_LCMP, EC.NOV_LCMP, EC.DEC_LCMP,
           EC.JAN_SHC, EC.FEB_SHC, EC.MAR_SHC, EC.APR_SHC, EC.MAY_SHC, EC.JUN_SHC, EC.JUL_SHC, EC.AUG_SHC, EC.SEP_SHC, EC.OCT_SHC, EC.NOV_SHC, EC.DEC_SHC
    FROM   dbo.EmployeeCode EC
    JOIN   dbo.Employee E ON E.id = EC.employeeId
    LEFT   JOIN dbo.Employer ER ON ER.id = EC.employerId
    WHERE  EC.employeeId = @EmployeeId AND EC.filingYear = @FilingYear
    ORDER  BY EC.id;

    SELECT D.*
    FROM   dbo.EmployeeCodeMonthDetail D
    WHERE  D.EmployeeCodeId = (SELECT TOP (1) id FROM dbo.EmployeeCode WHERE employeeId = @EmployeeId AND filingYear = @FilingYear ORDER BY id)
    ORDER  BY D.MonthNum;
END
GO

------------------------------------------------------------
-- sp_Generate1095Codes (current, with the 058 block)
------------------------------------------------------------
/* =============================================================================
   PART 2 -- sp_Generate1095Codes: build the premium lookup and the snapshot
             plan fallback from the whole group.

   Patched from the live 265-line definition by string replacement. Four hunks:
   CREATE -> CREATE OR ALTER, the @Group table variable, the #PP filter, and the
   plan fallback filter. Nothing else moves.
   ============================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_Generate1095Codes]
    @EmployerId INT, @FilingYear INT, @CurrentUserId NVARCHAR(128)='SYSTEM'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;   -- any error auto-aborts the transaction so it can never be left open
    DECLARE @TranStarted BIT=0; IF @@TRANCOUNT=0 BEGIN BEGIN TRANSACTION; SET @TranStarted=1; END
    BEGIN TRY
    DECLARE @AffordPct DECIMAL(10,5), @MonthlyFPL DECIMAL(10,5), @FT INT;
    DECLARE @PenA DECIMAL(18,2), @PenB DECIMAL(18,2);

    /* 019-B: the aggregated ALE group this employer belongs to.
       Staging_Plans is keyed on the Primary EIN only, so plans and premiums are
       created once, under the parent. An affiliate employer therefore owns none
       of either, and without widening these lookups every one of its employees
       resolves to a NULL plan and reports 1H -- no offer of coverage -- with a
       blank Line 15. Employer.companyId is the parent pointer the importer
       already sets; the parent's own companyId is NULL.
       Includes @EmployerId itself, so a standalone employer behaves exactly as
       it did before. */
    DECLARE @Group TABLE (id INT PRIMARY KEY);
    INSERT @Group (id)
    SELECT g.id
    FROM   Employer g
    WHERE  g.id = @EmployerId
       OR  g.id = (SELECT p.companyId FROM Employer p WHERE p.id = @EmployerId)
       OR  g.companyId = (SELECT p.companyId FROM Employer p WHERE p.id = @EmployerId)
       OR  g.companyId = @EmployerId;
    SELECT @MonthlyFPL=fpgPremium, @AffordPct=fpgPercent, @FT=ropHours,
           @PenA=PenaltyA_Annual, @PenB=PenaltyB_Annual FROM FilingYear WHERE filingYear=@FilingYear;

    IF @MonthlyFPL IS NULL OR @AffordPct IS NULL OR @FT IS NULL OR @PenA IS NULL OR @PenB IS NULL
    BEGIN
        RAISERROR('FilingYear row for %d is missing or incomplete (fpgPremium/fpgPercent/ropHours/PenaltyA_Annual/PenaltyB_Annual must all be set). Seed FilingYear for this year before generating codes.', 16, 1, @FilingYear);
        IF @TranStarted=1 ROLLBACK TRANSACTION;
        RETURN;
    END

    CREATE TABLE #M (MonthNum INT PRIMARY KEY, MS DATE, ME DATE);
    INSERT #M SELECT n, DATEFROMPARTS(@FilingYear,n,1), EOMONTH(DATEFROMPARTS(@FilingYear,n,1))
    FROM (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) v(n);

    CREATE TABLE #E (EmployeeId INT PRIMARY KEY, CodeId INT, HireDate DATE, TermDate DATE, Status INT,
        Elected BIT, CovStart DATE, CovEnd DATE, PlanName NVARCHAR(200), COBRAElected BIT, Eligible DATE,
        COBRAEffDate DATE, COBRAEndDate DATE, IsUnion BIT,
        HasSpan BIT DEFAULT 0, HasStatus BIT DEFAULT 0, HasEnr BIT DEFAULT 0);
    INSERT #E (EmployeeId,CodeId,HireDate,TermDate,Status,Elected,CovStart,CovEnd,PlanName,COBRAElected,Eligible,
               COBRAEffDate,COBRAEndDate,IsUnion)
    SELECT E.id, EC.id, E.HireDate, E.TerminationDate, E.Status, E.CoverageElected, E.CoverageStartDate, E.CoverageEndDate,
           E.LowestCostPlanOffered, E.COBRABenefitsElected, E.DateEligibleForCoverage,
           E.COBRAEffectiveDate, E.COBRAEndDate, E.IsUnionEmployee
    FROM Employee E JOIN EmployeeCode EC ON EC.employeeId=E.id AND EC.filingYear=@FilingYear
    WHERE E.EmployerId=@EmployerId AND E.IsDeleted=0 AND EC.IsLocked=0 AND EC.disableCoding=0;
    UPDATE e SET HasSpan=1 FROM #E e WHERE EXISTS(SELECT 1 FROM EmployeeHireSpan h WHERE h.employeeCodeId=e.CodeId);
    UPDATE e SET HasStatus=1 FROM #E e WHERE EXISTS(SELECT 1 FROM EmployeeStatus s WHERE s.employeeCodeId=e.CodeId);
    UPDATE e SET HasEnr=1 FROM #E e WHERE EXISTS(SELECT 1 FROM EmployeeEnrollment n WHERE n.employeeCodeId=e.CodeId);

    CREATE TABLE #PP (PlanId INT, MonthNum INT, Prem DECIMAL(18,2), PRIMARY KEY(PlanId,MonthNum));
    INSERT #PP SELECT pr.EmployerPlanId, m.MonthNum, MIN(pr.amount)
    FROM Premium pr JOIN #M m ON pr.bandingStartDate<=m.ME AND (pr.bandingEndDate IS NULL OR pr.bandingEndDate>=m.MS)
    WHERE pr.EmployerId IN (SELECT id FROM @Group) GROUP BY pr.EmployerPlanId, m.MonthNum;

    CREATE TABLE #Pay (EmployeeId INT PRIMARY KEY, Hourly DECIMAL(18,4), AnnualW2 DECIMAL(18,4));
    INSERT #Pay SELECT e.EmployeeId, ISNULL(MAX(pr.payPeriodTotalHours),0),
        ISNULL(MAX(pr.payPeriodSalaryAmount),0)+ISNULL(MAX(pr.payPeriodAdditional),0)
    FROM #E e JOIN EmployeePayroll pr ON pr.employeeCodeId=e.CodeId GROUP BY e.EmployeeId;

    CREATE TABLE #C (EmployeeId INT, MonthNum INT, Employed BIT DEFAULT 0, IsFT BIT DEFAULT 0,
        PlanId INT, mv BIT, sp INT, cond INT, dep INT, funding NVARCHAR(10), code1A BIT, Prem DECIMAL(18,2),
        EnrolledMo BIT DEFAULT 0, UnionMo BIT DEFAULT 0, CobraMo BIT DEFAULT 0,
        -- >>> ICHRA working columns
        IsIchra BIT DEFAULT 0, Basis CHAR(1), Lcsp DECIMAL(18,2), Allow DECIMAL(18,2),
        NetCost DECIMAL(18,2), IchraZip VARCHAR(10),
        Line14 VARCHAR(2) DEFAULT '1H', Line15 DECIMAL(18,2), Line16 VARCHAR(2), PRIMARY KEY(EmployeeId,MonthNum));
    INSERT #C (EmployeeId,MonthNum) SELECT e.EmployeeId, m.MonthNum FROM #E e CROSS JOIN #M m;

    UPDATE c SET Employed=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasSpan=1 JOIN #M m ON m.MonthNum=c.MonthNum
      JOIN EmployeeHireSpan h ON h.employeeCodeId=e.CodeId AND h.startDate<=m.ME AND (h.endDate IS NULL OR h.endDate>=m.MS);
    UPDATE c SET Employed=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasSpan=0 JOIN #M m ON m.MonthNum=c.MonthNum
      WHERE e.HireDate<=m.ME AND (e.TermDate IS NULL OR e.TermDate>=m.MS);
    UPDATE c SET IsFT=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasStatus=1 JOIN #M m ON m.MonthNum=c.MonthNum
      JOIN EmployeeStatus s ON s.employeeCodeId=e.CodeId AND s.status=1 AND s.startDate<=m.ME AND (s.endDate IS NULL OR s.endDate>=m.MS);
    UPDATE c SET IsFT=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasStatus=0 WHERE e.Status=1;

    ;WITH R AS (
      SELECT e.EmployeeId, m.MonthNum, EE.planId,
        ROW_NUMBER() OVER (PARTITION BY e.EmployeeId, m.MonthNum ORDER BY EE.coverageStartDate DESC, EE.coverageOfferDate DESC) rn
      FROM #E e JOIN EmployeeEnrollment EE ON EE.employeeCodeId=e.CodeId AND EE.planId IS NOT NULL AND e.HasEnr=1
      JOIN #M m ON ISNULL(EE.coverageOfferDate,ISNULL(EE.coverageStartDate,'1900-01-01'))<=m.ME AND (EE.coverageEndDate IS NULL OR EE.coverageEndDate>=m.MS))
    UPDATE c SET PlanId=R.planId FROM #C c JOIN R ON R.EmployeeId=c.EmployeeId AND R.MonthNum=c.MonthNum AND R.rn=1;
    UPDATE c SET PlanId=P.id FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasEnr=0
      JOIN EmployerPlan P ON REPLACE(LTRIM(RTRIM(P.name)),CHAR(160),'')=REPLACE(LTRIM(RTRIM(e.PlanName)),CHAR(160),'') AND P.employerId IN (SELECT id FROM @Group)
      WHERE c.PlanId IS NULL;
    UPDATE c SET mv=P.minimumValue, sp=P.offeredSpouse, cond=P.conditionallyOffSpouse, dep=P.offeredDependents,
                 funding=P.fundingType, code1A=P.code1A FROM #C c JOIN EmployerPlan P ON P.id=c.PlanId;
    UPDATE c SET Prem=pp.Prem FROM #C c JOIN #PP pp ON pp.PlanId=c.PlanId AND pp.MonthNum=c.MonthNum;

    /* ---- F-06: banded plans price per employee, not per plan ----------------
       #PP above takes MIN(amount) per plan and month, which is right for an
       unbanded plan (one row per date range) and wrong for a banded one: every
       employee is handed the cheapest tier. On an inverse salary scale that
       understates line 15 for exactly the people it matters most for, and an
       understated contribution passes the affordability test, so 2F/2G/2H get
       claimed where no safe harbour applies.

       Overrides Prem for plans banded on a numeric range. Employee.birthday and
       hire date are read here rather than added to #E, so no existing statement
       changes. Bands whose boundaries are not numeric -- 'Monthly' plans carry
       month names in these columns -- never match and keep the #PP value.
       Ties break to the higher amount: never understate. */
    UPDATE c
    SET    Prem = b.Amount
    FROM   #C c
    JOIN   EmployerPlan P  ON P.id = c.PlanId
    JOIN   #M m            ON m.MonthNum = c.MonthNum
    JOIN   Employee emp    ON emp.id = c.EmployeeId
    LEFT   JOIN #Pay pay   ON pay.EmployeeId = c.EmployeeId
    CROSS APPLY (
        SELECT TOP 1 Amount = pr.amount
        FROM   Premium pr
        WHERE  pr.EmployerPlanId = c.PlanId
          AND  pr.bandingStartDate <= m.ME
          AND  (pr.bandingEndDate IS NULL OR pr.bandingEndDate >= m.MS)
          AND  TRY_CAST(pr.bandingValueStart AS DECIMAL(18,2)) IS NOT NULL
          AND  TRY_CAST(pr.bandingValueEnd   AS DECIMAL(18,2)) IS NOT NULL
          AND  CASE UPPER(LTRIM(RTRIM(ISNULL(P.bandingType, ''))))
                    WHEN 'SALARY'           THEN ISNULL(pay.AnnualW2, 0)
                    WHEN 'HOURLY'           THEN ISNULL(pay.Hourly, 0)
                    WHEN 'AGE'              THEN CASE WHEN emp.birthday IS NULL THEN NULL
                                                      ELSE DATEDIFF(YEAR, emp.birthday, m.MS)
                                                         - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, emp.birthday, m.MS), emp.birthday) > m.MS
                                                                THEN 1 ELSE 0 END END
                    WHEN 'YEARS OF SERVICE' THEN CASE WHEN emp.HireDate IS NULL THEN NULL
                                                      ELSE DATEDIFF(YEAR, emp.HireDate, m.MS)
                                                         - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, emp.HireDate, m.MS), emp.HireDate) > m.MS
                                                                THEN 1 ELSE 0 END END
               END BETWEEN TRY_CAST(pr.bandingValueStart AS DECIMAL(18,2))
                       AND TRY_CAST(pr.bandingValueEnd   AS DECIMAL(18,2))
        ORDER BY pr.amount DESC
    ) b
    WHERE UPPER(LTRIM(RTRIM(ISNULL(P.bandingType, '')))) IN ('SALARY', 'HOURLY', 'AGE', 'YEARS OF SERVICE');

    -- >>> ICHRA (1) populate ICHRA inputs on the grid --------------------------------
    UPDATE c SET IsIchra=1, Basis=P.ichraLocationBasis, Allow=P.ichraSelfOnlyAllow
    FROM #C c JOIN EmployerPlan P ON P.id=c.PlanId WHERE P.isIchra=1;
    UPDATE c SET Allow=ISNULL(x.ichraAllowance,c.Allow), Lcsp=x.lcspSelfOnly,
                 IchraZip=COALESCE(x.applicableZip, CASE WHEN c.Basis='W' THEN emp.workSiteZip ELSE emp.zip END)
    FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN Employee emp ON emp.id=c.EmployeeId
      OUTER APPLY (SELECT TOP 1 ei.ichraAllowance, ei.lcspSelfOnly, ei.applicableZip
                   FROM EmployeeIchra ei WHERE ei.employeeCodeId=e.CodeId AND ei.monthNum IN (0,c.MonthNum)
                   ORDER BY ei.monthNum DESC) x
    WHERE c.IsIchra=1;
    UPDATE #C SET NetCost = CASE WHEN Lcsp IS NULL THEN NULL
                                 WHEN Lcsp-ISNULL(Allow,0) < 0 THEN 0 ELSE Lcsp-ISNULL(Allow,0) END
    WHERE IsIchra=1;
    -- <<< ICHRA (1)

    UPDATE c SET EnrolledMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasEnr=1 JOIN #M m ON m.MonthNum=c.MonthNum
      JOIN EmployeeEnrollment EE ON EE.employeeCodeId=e.CodeId AND EE.isEnrolled=1 AND EE.coverageStartDate<=m.ME AND ISNULL(EE.coverageEndDate,'2099-12-31')>=m.MS;
    UPDATE c SET EnrolledMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasEnr=0 JOIN #M m ON m.MonthNum=c.MonthNum
      WHERE e.Elected=1 AND e.CovStart<=m.ME AND ISNULL(e.CovEnd,'2099-12-31')>=m.MS;
    UPDATE c SET UnionMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN #M m ON m.MonthNum=c.MonthNum
      JOIN EmployeeEnrollment EE ON EE.employeeCodeId=e.CodeId AND EE.unionMember=1 AND ISNULL(EE.contributionStartDate,ISNULL(EE.coverageOfferDate,'1900-01-01'))<=m.ME AND (EE.contributionEndDate IS NULL OR EE.contributionEndDate>=m.MS);
    UPDATE c SET UnionMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN #M m ON m.MonthNum=c.MonthNum
      WHERE e.HasEnr=0 AND e.IsUnion=1;
    UPDATE c SET CobraMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN #M m ON m.MonthNum=c.MonthNum
      JOIN EmployeeEnrollment EE ON EE.employeeCodeId=e.CodeId AND EE.COBRAEnrolled=1 AND ISNULL(EE.COBRAStartDate,'1900-01-01')<=m.ME AND (EE.COBRAEndDate IS NULL OR EE.COBRAEndDate>=m.MS);
    UPDATE c SET CobraMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN #M m ON m.MonthNum=c.MonthNum
      WHERE e.HasEnr=0 AND e.COBRAElected=1
        AND ISNULL(e.COBRAEffDate,'1900-01-01')<=m.ME AND ISNULL(e.COBRAEndDate,'2099-12-31')>=m.MS;

    -- LINE 14 / 15 (group-plan path - unchanged)
    UPDATE #C SET Line14 = CASE
        WHEN PlanId IS NULL THEN '1H' WHEN mv=0 THEN '1F'
        WHEN cond=1 AND dep=1 THEN '1K' WHEN cond=1 AND dep=0 THEN '1J'
        WHEN sp=1 AND dep=1 AND code1A=1 AND Prem IS NOT NULL AND Prem<=@MonthlyFPL THEN '1A'
        WHEN sp=1 AND dep=1 THEN '1E' WHEN sp=1 AND dep=0 THEN '1D'
        WHEN sp=0 AND dep=1 THEN '1C' ELSE '1B' END,
      Line15 = CASE WHEN PlanId IS NULL OR mv=0 THEN NULL
                    WHEN sp=1 AND dep=1 AND code1A=1 AND Prem<=@MonthlyFPL THEN NULL ELSE Prem END;
    UPDATE #C SET Line14='1G', Line15=NULL WHERE IsFT=0 AND EnrolledMo=1 AND funding='2';

    -- LINE 16 (precedence - group-plan path unchanged)
    UPDATE #C SET Line16='2A', Line14='1H', Line15=NULL WHERE Employed=0;
    UPDATE #C SET Line16='2A' WHERE Line16 IS NULL AND CobraMo=1;
    UPDATE #C SET Line16='2E' WHERE Line16 IS NULL AND UnionMo=1;
    UPDATE #C SET Line16='2C' WHERE Line16 IS NULL AND EnrolledMo=1;
    UPDATE c SET Line16='2D' FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN #M m ON m.MonthNum=c.MonthNum
      WHERE c.Line16 IS NULL AND ((MONTH(e.HireDate)=c.MonthNum AND YEAR(e.HireDate)=@FilingYear AND DAY(e.HireDate)>1)
         OR (e.Eligible IS NOT NULL AND m.ME<e.Eligible AND m.ME>=e.HireDate));
    UPDATE #C SET Line16='2B' WHERE Line16 IS NULL AND IsFT=0;
    ;WITH HM AS (SELECT EmployeeId, SUM(CAST(Employed AS INT)) HiredMonths FROM #C GROUP BY EmployeeId)
    UPDATE c SET Line16 = CASE
        WHEN c.Prem<=@MonthlyFPL THEN '2G'
        WHEN p.Hourly>0 AND c.Prem<=(p.Hourly*@FT)*@AffordPct THEN '2H'
        WHEN p.AnnualW2>0 AND hm.HiredMonths>0 AND c.Prem<=(p.AnnualW2/hm.HiredMonths)*@AffordPct THEN '2F' ELSE NULL END
    FROM #C c LEFT JOIN #Pay p ON p.EmployeeId=c.EmployeeId LEFT JOIN HM hm ON hm.EmployeeId=c.EmployeeId
    WHERE c.Line16 IS NULL AND c.IsFT=1 AND c.PlanId IS NOT NULL AND c.mv=1 AND c.EnrolledMo=0;
    UPDATE #C SET Line16=NULL WHERE Line14 IN ('1A','1G');

    -- >>> ICHRA (2) override Line 14/15/16 for ICHRA offer months (1L-1U / 1R / 1S) ----
    ;WITH HM AS (SELECT EmployeeId, SUM(CAST(Employed AS INT)) HiredMonths FROM #C GROUP BY EmployeeId)
    UPDATE c SET
      Line16 = CASE
          WHEN c.IsFT=0 THEN NULL
          WHEN c.EnrolledMo=1 THEN '2C'
          WHEN c.NetCost<=@MonthlyFPL THEN '2G'
          WHEN p.Hourly>0 AND c.NetCost<=(p.Hourly*@FT)*@AffordPct THEN '2H'
          WHEN p.AnnualW2>0 AND hm.HiredMonths>0 AND c.NetCost<=(p.AnnualW2/hm.HiredMonths)*@AffordPct THEN '2F'
          ELSE NULL END,
      Line14 = CASE
          WHEN c.IsFT=0 THEN '1S'
          WHEN c.NetCost IS NULL THEN '1H'
          WHEN NOT (c.NetCost<=@MonthlyFPL
                    OR (p.Hourly>0 AND c.NetCost<=(p.Hourly*@FT)*@AffordPct)
                    OR (p.AnnualW2>0 AND hm.HiredMonths>0 AND c.NetCost<=(p.AnnualW2/hm.HiredMonths)*@AffordPct))
               THEN '1R'
          WHEN c.Basis='W' AND (c.sp=1 OR c.cond=1) AND c.dep=1 THEN '1Q'
          WHEN c.Basis='W' AND (c.sp=1 OR c.cond=1) AND c.dep=0 THEN '1U'
          WHEN c.Basis='W' AND c.dep=1 THEN '1P'
          WHEN c.Basis='W' THEN '1O'
          WHEN (c.sp=1 OR c.cond=1) AND c.dep=1 THEN '1N'
          WHEN (c.sp=1 OR c.cond=1) AND c.dep=0 THEN '1T'
          WHEN c.dep=1 THEN '1M'
          ELSE '1L' END,
      Line15 = CASE WHEN c.IsFT=0 THEN NULL ELSE c.NetCost END
    FROM #C c LEFT JOIN #Pay p ON p.EmployeeId=c.EmployeeId LEFT JOIN HM hm ON hm.EmployeeId=c.EmployeeId
    WHERE c.IsIchra=1 AND c.Employed=1 AND ISNULL(c.Line16,'') NOT IN ('2D','2E');
    -- <<< ICHRA (2)

    DECLARE @DefRenew VARCHAR(2) = (SELECT TOP 1 RIGHT('0'+CAST(ISNULL(planRenewal,1) AS VARCHAR(2)),2)
                                    FROM EmployerPlan WHERE employerId=@EmployerId ORDER BY id);
    SET @DefRenew = ISNULL(@DefRenew,'01');
    CREATE TABLE #PSM (EmployeeId INT PRIMARY KEY, StartMonth VARCHAR(2));
    INSERT #PSM SELECT c.EmployeeId,
        ISNULL(RIGHT('0'+CAST(MAX(P.planRenewal) AS VARCHAR(2)),2), @DefRenew)
    FROM #C c LEFT JOIN EmployerPlan P ON P.id=c.PlanId GROUP BY c.EmployeeId;

    ;WITH MP AS (SELECT EmployeeId,
        MAX(CASE WHEN MonthNum=1 THEN Line14 END) C1,MAX(CASE WHEN MonthNum=2 THEN Line14 END) C2,MAX(CASE WHEN MonthNum=3 THEN Line14 END) C3,MAX(CASE WHEN MonthNum=4 THEN Line14 END) C4,MAX(CASE WHEN MonthNum=5 THEN Line14 END) C5,MAX(CASE WHEN MonthNum=6 THEN Line14 END) C6,MAX(CASE WHEN MonthNum=7 THEN Line14 END) C7,MAX(CASE WHEN MonthNum=8 THEN Line14 END) C8,MAX(CASE WHEN MonthNum=9 THEN Line14 END) C9,MAX(CASE WHEN MonthNum=10 THEN Line14 END) C10,MAX(CASE WHEN MonthNum=11 THEN Line14 END) C11,MAX(CASE WHEN MonthNum=12 THEN Line14 END) C12,
        MAX(CASE WHEN MonthNum=1 THEN Line15 END) L1,MAX(CASE WHEN MonthNum=2 THEN Line15 END) L2,MAX(CASE WHEN MonthNum=3 THEN Line15 END) L3,MAX(CASE WHEN MonthNum=4 THEN Line15 END) L4,MAX(CASE WHEN MonthNum=5 THEN Line15 END) L5,MAX(CASE WHEN MonthNum=6 THEN Line15 END) L6,MAX(CASE WHEN MonthNum=7 THEN Line15 END) L7,MAX(CASE WHEN MonthNum=8 THEN Line15 END) L8,MAX(CASE WHEN MonthNum=9 THEN Line15 END) L9,MAX(CASE WHEN MonthNum=10 THEN Line15 END) L10,MAX(CASE WHEN MonthNum=11 THEN Line15 END) L11,MAX(CASE WHEN MonthNum=12 THEN Line15 END) L12,
        MAX(CASE WHEN MonthNum=1 THEN Line16 END) S1,MAX(CASE WHEN MonthNum=2 THEN Line16 END) S2,MAX(CASE WHEN MonthNum=3 THEN Line16 END) S3,MAX(CASE WHEN MonthNum=4 THEN Line16 END) S4,MAX(CASE WHEN MonthNum=5 THEN Line16 END) S5,MAX(CASE WHEN MonthNum=6 THEN Line16 END) S6,MAX(CASE WHEN MonthNum=7 THEN Line16 END) S7,MAX(CASE WHEN MonthNum=8 THEN Line16 END) S8,MAX(CASE WHEN MonthNum=9 THEN Line16 END) S9,MAX(CASE WHEN MonthNum=10 THEN Line16 END) S10,MAX(CASE WHEN MonthNum=11 THEN Line16 END) S11,MAX(CASE WHEN MonthNum=12 THEN Line16 END) S12,
        -- >>> ICHRA (3) pivot Line 17 ZIP
        MAX(CASE WHEN MonthNum=1 THEN IchraZip END) Z1,MAX(CASE WHEN MonthNum=2 THEN IchraZip END) Z2,MAX(CASE WHEN MonthNum=3 THEN IchraZip END) Z3,MAX(CASE WHEN MonthNum=4 THEN IchraZip END) Z4,MAX(CASE WHEN MonthNum=5 THEN IchraZip END) Z5,MAX(CASE WHEN MonthNum=6 THEN IchraZip END) Z6,MAX(CASE WHEN MonthNum=7 THEN IchraZip END) Z7,MAX(CASE WHEN MonthNum=8 THEN IchraZip END) Z8,MAX(CASE WHEN MonthNum=9 THEN IchraZip END) Z9,MAX(CASE WHEN MonthNum=10 THEN IchraZip END) Z10,MAX(CASE WHEN MonthNum=11 THEN IchraZip END) Z11,MAX(CASE WHEN MonthNum=12 THEN IchraZip END) Z12,
        -- <<< ICHRA (3)
        MAX(CASE WHEN Line14='1H' AND Line16 IS NULL THEN 1 ELSE 0 END) Risk,
        SUM(CASE WHEN Line14='1H' AND Line16 IS NULL THEN 1 ELSE 0 END) PMonths
      FROM #C GROUP BY EmployeeId)
    UPDATE EC SET JAN_COC=P.C1,FEB_COC=P.C2,MAR_COC=P.C3,APR_COC=P.C4,MAY_COC=P.C5,JUN_COC=P.C6,JUL_COC=P.C7,AUG_COC=P.C8,SEP_COC=P.C9,OCT_COC=P.C10,NOV_COC=P.C11,DEC_COC=P.C12,
        JAN_LCMP=P.L1,FEB_LCMP=P.L2,MAR_LCMP=P.L3,APR_LCMP=P.L4,MAY_LCMP=P.L5,JUN_LCMP=P.L6,JUL_LCMP=P.L7,AUG_LCMP=P.L8,SEP_LCMP=P.L9,OCT_LCMP=P.L10,NOV_LCMP=P.L11,DEC_LCMP=P.L12,
        JAN_SHC=P.S1,FEB_SHC=P.S2,MAR_SHC=P.S3,APR_SHC=P.S4,MAY_SHC=P.S5,JUN_SHC=P.S6,JUL_SHC=P.S7,AUG_SHC=P.S8,SEP_SHC=P.S9,OCT_SHC=P.S10,NOV_SHC=P.S11,DEC_SHC=P.S12,
        -- >>> ICHRA (4) write Line 17 ZIP columns
        JAN_ZIP=P.Z1,FEB_ZIP=P.Z2,MAR_ZIP=P.Z3,APR_ZIP=P.Z4,MAY_ZIP=P.Z5,JUN_ZIP=P.Z6,JUL_ZIP=P.Z7,AUG_ZIP=P.Z8,SEP_ZIP=P.Z9,OCT_ZIP=P.Z10,NOV_ZIP=P.Z11,DEC_ZIP=P.Z12,
        ALLM_ZIP=CASE WHEN P.Z1=P.Z2 AND P.Z1=P.Z3 AND P.Z1=P.Z4 AND P.Z1=P.Z5 AND P.Z1=P.Z6 AND P.Z1=P.Z7 AND P.Z1=P.Z8 AND P.Z1=P.Z9 AND P.Z1=P.Z10 AND P.Z1=P.Z11 AND P.Z1=P.Z12 THEN P.Z1 ELSE NULL END,
        -- <<< ICHRA (4)
        ALLM_COC=CASE WHEN P.C1=P.C2 AND P.C1=P.C3 AND P.C1=P.C4 AND P.C1=P.C5 AND P.C1=P.C6 AND P.C1=P.C7 AND P.C1=P.C8 AND P.C1=P.C9 AND P.C1=P.C10 AND P.C1=P.C11 AND P.C1=P.C12 THEN P.C1 ELSE NULL END,
        ALLM_LCMP=CASE WHEN P.L1=P.L2 AND P.L1=P.L3 AND P.L1=P.L4 AND P.L1=P.L5 AND P.L1=P.L6 AND P.L1=P.L7 AND P.L1=P.L8 AND P.L1=P.L9 AND P.L1=P.L10 AND P.L1=P.L11 AND P.L1=P.L12 THEN P.L1 ELSE NULL END,
        ALLM_SHC=CASE WHEN P.S1=P.S2 AND P.S1=P.S3 AND P.S1=P.S4 AND P.S1=P.S5 AND P.S1=P.S6 AND P.S1=P.S7 AND P.S1=P.S8 AND P.S1=P.S9 AND P.S1=P.S10 AND P.S1=P.S11 AND P.S1=P.S12 THEN P.S1 ELSE NULL END,
        PlanStartMonth=PSM.StartMonth,
        IsAtPenaltyRisk=P.Risk, PenaltyMonths=P.PMonths,
        EstimatedPenalty = P.PMonths * (@PenA / 12.0)
    FROM EmployeeCode EC JOIN MP P ON EC.employeeId=P.EmployeeId
    LEFT JOIN #PSM PSM ON PSM.EmployeeId=EC.employeeId
    WHERE EC.employerId=@EmployerId AND EC.filingYear=@FilingYear AND EC.IsLocked=0 AND EC.disableCoding=0;

    INSERT INTO EmployeeCodeAuditTrail
        (EmployeeId, EmployerId, FilingYear, MonthCode, AppliesToAllMonths,
         LineNumber, FieldName, OldValue, NewValue,
         ChangeSource, ChangeReason, SafeHarborApplied,
         ChangedByUserId, ChangedByUserName, IsSystemGenerated, ChangedOn, SourceIP, Notes)
    SELECT EC.employeeId, @EmployerId, @FilingYear, 'ALL', 0,
           0, 'All Codes', 'PREVIOUS_CALC', 'RE-CALCULATED',
           'SYSTEM', 'Automatic Code Generation Triggered', NULL,
           NULL, @CurrentUserId, 1, GETDATE(), 'INTERNAL', 'Codes recalculated by sp_Generate1095Codes (v8+ICHRA)'
    FROM EmployeeCode EC
    WHERE EC.employerId=@EmployerId AND EC.filingYear=@FilingYear AND EC.IsLocked=0;

    -- 058: keep each month's working values, so the codes can be explained ("Why this code?").
    -- Locked employees and those with coding switched off are not recalculated and keep theirs.
    DELETE D FROM dbo.EmployeeCodeMonthDetail D JOIN #E e ON e.CodeId = D.EmployeeCodeId;
    ;WITH HM AS (SELECT EmployeeId, SUM(CAST(Employed AS INT)) HiredMonths FROM #C GROUP BY EmployeeId)
    INSERT dbo.EmployeeCodeMonthDetail
        (EmployeeCodeId, MonthNum, Employed, IsFullTime, PlanId, PlanName, MinimumValue, OfferedSpouse, ConditionalSpouse,
         OfferedDependents, FundingType, Code1A, Premium, Enrolled, UnionMonth, CobraMonth, IsIchra, IchraLcsp, IchraAllowance,
         IchraNetCost, HourlyRate, AnnualW2, HiredMonths, MonthlyFpl, AffordabilityPct, FullTimeHours,
         HireDate, TerminationDate, EligibleDate, Line14, Line15, Line16, CalculatedBy)
    SELECT e.CodeId, c.MonthNum, c.Employed, c.IsFT, c.PlanId, P.name, c.mv, c.sp, c.cond,
           c.dep, c.funding, c.code1A, c.Prem, c.EnrolledMo, c.UnionMo, c.CobraMo, c.IsIchra, c.Lcsp, c.Allow,
           c.NetCost, pay.Hourly, pay.AnnualW2, hm.HiredMonths, @MonthlyFPL, @AffordPct, @FT,
           e.HireDate, e.TermDate, e.Eligible, c.Line14, c.Line15, c.Line16, @CurrentUserId
    FROM   #C c
    JOIN   #E e ON e.EmployeeId = c.EmployeeId
    LEFT   JOIN EmployerPlan P ON P.id = c.PlanId
    LEFT   JOIN #Pay pay ON pay.EmployeeId = c.EmployeeId
    LEFT   JOIN HM hm ON hm.EmployeeId = c.EmployeeId;

    DROP TABLE #M; DROP TABLE #E; DROP TABLE #PP; DROP TABLE #Pay; DROP TABLE #C; DROP TABLE #PSM;
    IF @TranStarted=1 COMMIT TRANSACTION;
    END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
END
GO
GO

/* ############################################################################
   059_global_search.sql
   ############################################################################ */
PRINT '=== 059_global_search.sql';
GO
/* =============================================================================
   059 - Global search (Ctrl+K): employees of the selected employer

   dbo.sp_GlobalSearch_Employees finds employees the way the Employee list scopes
   them: those with codes for the filing year under the selected employer or one of
   its affiliates (Employer.companyId), not deleted. It matches the name (first,
   last or "first last") or, for 4 digits, the last four of the SSN. It returns the
   last four SSN digits only.

   Employers, files and pages are searched in the app with the checks it already
   has (permitted employers, dbo.fn_FileVisibleToUser, the user's menu).
   Read-only. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GlobalSearch_Employees
    @EmployerId INT,
    @FilingYear INT,
    @Query      NVARCHAR(100),
    @Top        INT = 8
AS
BEGIN
    SET NOCOUNT ON;
    SET @Query = NULLIF(LTRIM(RTRIM(@Query)), N'');
    IF @Query IS NULL OR @EmployerId IS NULL OR @EmployerId <= 0 RETURN;
    IF @Top IS NULL OR @Top NOT BETWEEN 1 AND 50 SET @Top = 8;

    DECLARE @Digits VARCHAR(4) = CASE WHEN @Query NOT LIKE '%[^0-9]%' AND LEN(@Query) = 4 THEN @Query END;
    DECLARE @Like NVARCHAR(110) = N'%' + REPLACE(REPLACE(REPLACE(@Query, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%';

    SELECT TOP (@Top)
           E.id AS EmployeeId,
           LTRIM(RTRIM(CONCAT(E.firstName, N' ', E.lastName))) AS Name,
           RIGHT(REPLACE(E.ssn, '-', ''), 4) AS SsnLast4,
           ER.name AS EmployerName,
           E.TerminationDate
    FROM   dbo.EmployeeCode EC
    JOIN   dbo.Employee E   ON E.id = EC.employeeId AND E.IsDeleted = 0
    JOIN   dbo.Employer ER  ON ER.id = EC.employerId
    WHERE  EC.filingYear = @FilingYear
      AND  (EC.employerId = @EmployerId OR ER.companyId = @EmployerId)
      AND  (  (@Digits IS NOT NULL AND RIGHT(REPLACE(E.ssn, '-', ''), 4) = @Digits)
           OR (@Digits IS NULL AND (E.firstName LIKE @Like OR E.lastName LIKE @Like
                                    OR CONCAT(E.firstName, N' ', E.lastName) LIKE @Like)))
    ORDER  BY CASE WHEN E.lastName LIKE @Query + N'%' OR E.firstName LIKE @Query + N'%' THEN 0 ELSE 1 END,
              E.lastName, E.firstName, E.id;
END
GO
GO

/* ############################################################################
   060_employee_page_speed.sql
   ############################################################################ */
PRINT '=== 060_employee_page_speed.sql';
GO
/* =============================================================================
   060 - Employee page speed

   The Employee page runs two procedures on every load: sp_EmployeeList_Filter
   (the list) and sp_Employee_QuickStats (the counts above it). Both test each
   optional filter inside one big query ("no plan filter OR plan matches"), which
   makes SQL Server choose one plan for every combination: on an employer with
   10,000 employees the list took about 670 ms and the counts about 220 ms.

   Now each "is this filter used?" is worked out once into a variable and the big
   queries are compiled for the filters actually in use (OPTION (RECOMPILE)); the
   list reads only the selected employers' codes instead of every code of the year;
   the counts use a temp table for the employer ids instead of a table variable.
   The results are the same (checked on 38 filter combinations).

   Both procedures are the database's current ones with only the changes marked
   060. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_EmployeeList_Filter]  
(  
    @Employer_ID        BIGINT = NULL,  
    @EmployerIDs        NVARCHAR(MAX) = NULL,  
    @FillingYear        INT,  
    @PageIndex          INT = 1,  
    @PageSize           INT = 20,  
    @Search             NVARCHAR(100) = NULL,  
    @SortColumn         INT = 0,  
    @SortOrder          VARCHAR(5) = 'asc',  
    @PlanIDs            NVARCHAR(MAX) = NULL,  
    @Statuses            NVARCHAR(MAX) = NULL,  
    @EnrollmentIDs      NVARCHAR(MAX) = NULL,  
    @Flags              NVARCHAR(MAX) = NULL,  
    @IsReceivingForms   NVARCHAR(MAX) = NULL,  
    @User_ID            INT = NULL,  
    @QuickFilter        VARCHAR(10) = NULL  
)  
AS  
BEGIN  
    SET NOCOUNT ON;  
  
  
  
    BEGIN TRY  
  
  
  
        ------------------------------------------------------------  
        -- Safety  
        ------------------------------------------------------------  
        IF @PageIndex < 1  
            SET @PageIndex = 1;  
  
  
  
        IF @PageSize < 1  
            SET @PageSize = 20;  
  
  
  
        SET @Search = NULLIF(LTRIM(RTRIM(@Search)), '');  
  
  
  
        ------------------------------------------------------------  
        -- 1. Parse JSON filters ONCE  
        ------------------------------------------------------------  
  
  
  
        CREATE TABLE #EmployerFilter  
        (  
            ID BIGINT NOT NULL PRIMARY KEY  
        );  
  
  
  
        CREATE TABLE #StatusFilter  
(  
    StatusValue NVARCHAR(100) NOT NULL PRIMARY KEY  
);  
  
  
  
        CREATE TABLE #PlanFilter  
        (  
            ID BIGINT NOT NULL PRIMARY KEY  
        );  
  
  
  
        CREATE TABLE #EnrollmentFilter  
        (  
            ID INT NOT NULL PRIMARY KEY  
        );  
  
  
  
        CREATE TABLE #FlagFilter  
        (  
            ID INT NOT NULL PRIMARY KEY  
        );  
  
  
  
        CREATE TABLE #ReceivingFormFilter  
        (  
            ID INT NOT NULL PRIMARY KEY  
        );  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Employer IDs  
        ------------------------------------------------------------  
        IF NULLIF(@EmployerIDs, '') IS NOT NULL  
           AND @EmployerIDs <> '[]'  
        BEGIN  
            INSERT INTO #EmployerFilter(ID)  
            SELECT DISTINCT TRY_CONVERT(BIGINT, value)  
            FROM OPENJSON(@EmployerIDs)  
            WHERE TRY_CONVERT(BIGINT, value) IS NOT NULL;  
        END  
        ELSE IF @Employer_ID IS NOT NULL  
        BEGIN  
            INSERT INTO #EmployerFilter(ID)  
            VALUES (@Employer_ID);  
  
  
  
            INSERT INTO #EmployerFilter(ID)  
            SELECT E.id  
            FROM Employer E  
            WHERE E.companyId = @Employer_ID  
              AND NOT EXISTS  
              (  
                  SELECT 1  
                  FROM #EmployerFilter X  
                  WHERE X.ID = E.id  
              );  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Status  
        ------------------------------------------------------------  
        IF NULLIF(@Statuses, '') IS NOT NULL  
           AND @Statuses <> '[]'  
        BEGIN  
           INSERT INTO #StatusFilter(StatusValue)  
SELECT DISTINCT CONVERT(NVARCHAR(100), value)  
FROM OPENJSON(@Statuses)  
WHERE value IS NOT NULL;  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Plan  
        ------------------------------------------------------------  
        IF NULLIF(@PlanIDs, '') IS NOT NULL  
           AND @PlanIDs <> '[]'  
        BEGIN  
            INSERT INTO #PlanFilter(ID)  
            SELECT DISTINCT TRY_CONVERT(BIGINT, value)  
            FROM OPENJSON(@PlanIDs)  
            WHERE TRY_CONVERT(BIGINT, value) IS NOT NULL;  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Enrollment  
        ------------------------------------------------------------  
        IF NULLIF(@EnrollmentIDs, '') IS NOT NULL  
           AND @EnrollmentIDs <> '[]'  
        BEGIN  
            INSERT INTO #EnrollmentFilter(ID)  
            SELECT DISTINCT TRY_CONVERT(INT, value)  
            FROM OPENJSON(@EnrollmentIDs)  
            WHERE TRY_CONVERT(INT, value) IS NOT NULL;  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Flags  
        ------------------------------------------------------------  
        IF NULLIF(@Flags, '') IS NOT NULL  
           AND @Flags <> '[]'  
        BEGIN  
            INSERT INTO #FlagFilter(ID)  
            SELECT DISTINCT TRY_CONVERT(INT, value)  
            FROM OPENJSON(@Flags)  
            WHERE TRY_CONVERT(INT, value) IS NOT NULL;  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Receiving forms  
        ------------------------------------------------------------  
        IF NULLIF(@IsReceivingForms, '') IS NOT NULL  
           AND @IsReceivingForms <> '[]'  
        BEGIN  
            INSERT INTO #ReceivingFormFilter(ID)  
            SELECT DISTINCT TRY_CONVERT(INT, value)  
            FROM OPENJSON(@IsReceivingForms)  
            WHERE TRY_CONVERT(INT, value) IS NOT NULL;  
        END;

        -- 060: which filters are in use.
        DECLARE @HasEmployerFilter BIT = CASE WHEN EXISTS (SELECT 1 FROM #EmployerFilter) THEN 1 ELSE 0 END,
                @HasStatusFilter   BIT = CASE WHEN EXISTS (SELECT 1 FROM #StatusFilter) THEN 1 ELSE 0 END,
                @HasPlanFilter     BIT = CASE WHEN EXISTS (SELECT 1 FROM #PlanFilter) THEN 1 ELSE 0 END,
                @HasFormFilter     BIT = CASE WHEN EXISTS (SELECT 1 FROM #ReceivingFormFilter) THEN 1 ELSE 0 END,
                @HasEnrollFilter   BIT = CASE WHEN EXISTS (SELECT 1 FROM #EnrollmentFilter) THEN 1 ELSE 0 END,
                @HasFlagFilter     BIT = CASE WHEN EXISTS (SELECT 1 FROM #FlagFilter) THEN 1 ELSE 0 END;
  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 2. Filter EmployeeCode FIRST  
        --  
        -- This is extremely important for millions of records.  
        ------------------------------------------------------------  
        CREATE TABLE #EmployeeCodes  
        (  
            EmpCodeID       BIGINT NOT NULL PRIMARY KEY,  
            EmployeeID      BIGINT NOT NULL,  
            EmployerID      BIGINT NULL,  
            GetForm         INT NULL,  
            FilingYear      INT NOT NULL  
        );  
  
  
  
  
  
        -- 060: the selected employers' codes through a join, or every code of the year.
        IF @HasEmployerFilter = 1
            INSERT INTO #EmployeeCodes (EmpCodeID, EmployeeID, EmployerID, GetForm, FilingYear)
            SELECT EC.id, EC.employeeid, EC.employerId, EC.getForm, EC.filingYear
            FROM   EmployeeCode EC
            JOIN   (SELECT DISTINCT ID FROM #EmployerFilter) EF ON EF.ID = EC.employerId
            WHERE  EC.filingYear = @FillingYear;
        ELSE
            INSERT INTO #EmployeeCodes (EmpCodeID, EmployeeID, EmployerID, GetForm, FilingYear)
            SELECT EC.id, EC.employeeid, EC.employerId, EC.getForm, EC.filingYear
            FROM   EmployeeCode EC
            WHERE  EC.filingYear = @FillingYear;

        CREATE INDEX IX_EmployeeCodes_EmployeeID  
            ON #EmployeeCodes(EmployeeID);  
  
  
  
        CREATE INDEX IX_EmployeeCodes_EmployerID  
            ON #EmployeeCodes(EmployerID);  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 3. Build Status information ONCE  
        ------------------------------------------------------------  
        CREATE TABLE #Status  
        (  
            EmployeeCodeID BIGINT NOT NULL PRIMARY KEY,  
            EmployeeStatus NVARCHAR(100) NULL  
        );  
  
  
  
  
  
        INSERT INTO #Status  
        (  
            EmployeeCodeID,  
            EmployeeStatus  
        )  
        SELECT  
            ES.employeeCodeId,  
            MAX(ES.status)  
        FROM EmployeeStatus ES  
        INNER JOIN #EmployeeCodes EC  
            ON EC.EmpCodeID = ES.employeeCodeId  
        GROUP BY  
            ES.employeeCodeId;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 4. Build Enrollment information ONCE  
        ------------------------------------------------------------  
        CREATE TABLE #Enrollment  
        (  
            EmployeeCodeID       BIGINT NOT NULL PRIMARY KEY,  
            PlanID               BIGINT NULL,  
            CoverageOfferDate    DATETIME NULL,  
            IsEnrolled            BIT NULL,  
            UnionMember           BIT NULL,  
            COBRAEnrolled         BIT NULL,  
            RetireeEnrolled       BIT NULL  
        );  
  
  
  
  
  
        INSERT INTO #Enrollment  
        (  
            EmployeeCodeID,  
            PlanID,  
            CoverageOfferDate,  
            IsEnrolled,  
            UnionMember,  
            COBRAEnrolled,  
            RetireeEnrolled  
        )  
        SELECT  
            EE.employeeCodeId,  
            MAX(EE.planId),  
            MAX(EE.coverageOfferDate),  
            MAX(CONVERT(INT, ISNULL(EE.isEnrolled, 0))),  
            MAX(CONVERT(INT, ISNULL(EE.unionMember, 0))),  
            MAX(CONVERT(INT, ISNULL(EE.COBRAEnrolled, 0))),  
            MAX(CONVERT(INT, ISNULL(EE.RetireeEnrolled, 0)))  
        FROM EmployeeEnrollment EE  
        INNER JOIN #EmployeeCodes EC  
            ON EC.EmpCodeID = EE.employeeCodeId  
        GROUP BY  
            EE.employeeCodeId;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 5. Missing-data calculation  
        --  
        -- Instead of four correlated NOT EXISTS per employee,  
        -- calculate availability once.  
        ------------------------------------------------------------  
        CREATE TABLE #DataAvailability  
        (  
            EmployeeCodeID BIGINT NOT NULL PRIMARY KEY,  
            HasHire        BIT NOT NULL,  
            HasStatus      BIT NOT NULL,  
            HasEnrollment  BIT NOT NULL,  
            HasPayroll     BIT NOT NULL  
        );  
  
  
  
  
  
        INSERT INTO #DataAvailability  
        (  
            EmployeeCodeID,  
            HasHire,  
            HasStatus,  
            HasEnrollment,  
            HasPayroll  
        )  
        SELECT  
            EC.EmpCodeID,  
  
  
  
            CASE  
                WHEN EXISTS  
                (  
                    SELECT 1  
                    FROM EmployeeHireSpan H  
                    WHERE H.employeeCodeId = EC.EmpCodeID  
                )  
                THEN 1 ELSE 0  
            END,  
  
  
  
            CASE  
                WHEN EXISTS  
                (  
                    SELECT 1  
                    FROM EmployeeStatus S  
                    WHERE S.employeeCodeId = EC.EmpCodeID  
                )  
                THEN 1 ELSE 0  
            END,  
  
  
  
            CASE  
                WHEN EXISTS  
                (  
                    SELECT 1  
                    FROM EmployeeEnrollment E  
                    WHERE E.employeeCodeId = EC.EmpCodeID  
                )  
                THEN 1 ELSE 0  
            END,  
  
  
  
            CASE  
                WHEN EXISTS  
                (  
                    SELECT 1  
                    FROM EmployeePayroll P  
                    WHERE P.employeeCodeId = EC.EmpCodeID  
                )  
                THEN 1 ELSE 0  
            END  
  
  
  
        FROM #EmployeeCodes EC;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 6. Employee Flags  
        --  
        -- Only process flags belonging to relevant employees/year.  
        ------------------------------------------------------------  
        CREATE TABLE #EmployeeFlags  
        (  
            EmployeeSSN  NVARCHAR(100) NOT NULL,  
            EmployerID   BIGINT NOT NULL,  
            FlagID       INT NOT NULL,  
  
  
  
            PRIMARY KEY  
            (  
                EmployeeSSN,  
                EmployerID,  
                FlagID  
            )  
        );  
  
  
  
  
  
        INSERT INTO #EmployeeFlags  
        (  
            EmployeeSSN,  
            EmployerID,  
            FlagID  
        )  
        SELECT DISTINCT  
            EF.EmployeeSSN,  
            EF.EmployerId,  
            EF.FlagId  
        FROM EmployeeFlags EF  
        INNER JOIN #EmployeeCodes EC  
            ON EC.EmployerID = EF.EmployerId  
        INNER JOIN Employee EMP  
            ON EMP.id = EC.EmployeeID  
           AND EMP.ssn = EF.EmployeeSSN  
        WHERE EF.TaxYear = @FillingYear;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 7. Main result  
        ------------------------------------------------------------  
        CREATE TABLE #TempResults  
        (  
            Id              BIGINT NOT NULL,  
            FirstName       NVARCHAR(200) NULL,  
            LastName        NVARCHAR(200) NULL,  
            FullName        NVARCHAR(401) NULL,  
            SSN             NVARCHAR(100) NULL,  
            Suffix          NVARCHAR(100) NULL,  
            EmployerName    NVARCHAR(500) NULL,  
            EmployeeStatus  NVARCHAR(100) NULL,  
            GetForm         INT NULL,  
            FlagId          INT NULL,  
            FlagDescription NVARCHAR(MAX) NULL,  
            FlagSeverity    VARCHAR(20) NULL,  
            IsMissingData   BIT NOT NULL,  
            EmpCodeID       BIGINT NOT NULL  
        );  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Employee base query  
        ------------------------------------------------------------  
        INSERT INTO #TempResults  
        (  
            Id,  
            FirstName,  
            LastName,  
            FullName,  
            SSN,  
            Suffix,  
            EmployerName,  
            EmployeeStatus,  
            GetForm,  
            FlagId,  
            FlagDescription,  
            FlagSeverity,  
            IsMissingData,  
            EmpCodeID  
        )  
        SELECT  
            EMP.id,  
  
  
  
            EMP.firstName,  
  
  
  
            EMP.lastName,  
  
  
  
            ISNULL(EMP.firstName, '') +  
                CASE  
                    WHEN EMP.firstName IS NOT NULL  
                     AND EMP.lastName IS NOT NULL  
                    THEN ' '  
                    ELSE ''  
                END +  
                ISNULL(EMP.lastName, ''),  
  
  
  
            EMP.ssn,  
  
  
  
            EMP.suffix,  
  
  
  
            ER.name,  
  
  
  
            S.EmployeeStatus,  
  
  
  
            EC.GetForm,  
  
  
  
            EMP.FlagId,  
  
  
  
            EMP.FlagDescription,  
  
  
  
            FD.Severity,  
  
  
  
            CASE  
                WHEN DA.HasHire = 0  
                  OR DA.HasStatus = 0  
                  OR DA.HasEnrollment = 0  
                  OR DA.HasPayroll = 0  
                THEN 1  
                ELSE 0  
            END,  
  
  
  
            EC.EmpCodeID  
  
  
  
        FROM #EmployeeCodes EC  
  
  
  
        INNER JOIN Employee EMP  
            ON EMP.id = EC.EmployeeID  
  
  
  
        LEFT JOIN Employer ER  
            ON ER.id = EC.EmployerID  
  
  
  
        LEFT JOIN #Status S  
            ON S.EmployeeCodeID = EC.EmpCodeID  
  
  
  
        LEFT JOIN #Enrollment EN  
            ON EN.EmployeeCodeID = EC.EmpCodeID  
  
  
  
        LEFT JOIN #DataAvailability DA  
            ON DA.EmployeeCodeID = EC.EmpCodeID  
  
  
  
        LEFT JOIN FlagDefinitions FD  
            ON FD.FlagId = EMP.FlagId  
  
  
  
        WHERE EMP.IsDeleted = 0  
          AND EMP.Flag = 0  
  
  
  
          ----------------------------------------------------------  
          -- Search  
          ----------------------------------------------------------  
          AND  
          (  
              @Search IS NULL  
              OR EMP.firstName LIKE '%' + @Search + '%'  
              OR EMP.lastName LIKE '%' + @Search + '%'  
              OR EMP.fullName LIKE '%' + @Search + '%'  
              OR EMP.ssn LIKE '%' + @Search + '%'  
          )  
  
  
  
          ----------------------------------------------------------  
          -- Status filter  
          ----------------------------------------------------------  
         AND  
(  
    @HasStatusFilter = 0  
  
  
  
    OR  
    (  
        EXISTS  
        (  
            SELECT 1  
            FROM #StatusFilter SF  
            WHERE SF.StatusValue = '-2'  
        )  
        AND S.EmployeeStatus IS NULL  
    )  
  
  
  
    OR  
    EXISTS  
    (  
        SELECT 1  
        FROM #StatusFilter SF  
        WHERE SF.StatusValue = S.EmployeeStatus  
    )  
)  
  
  
  
          ----------------------------------------------------------  
          -- Plan filter  
          ----------------------------------------------------------  
          AND  
          (  
              @HasPlanFilter = 0  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #PlanFilter PF  
                  WHERE PF.ID = -2  
                    AND EN.PlanID IS NULL  
              )  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #PlanFilter PF  
                  WHERE PF.ID = EN.PlanID  
              )  
          )  
  
  
  
          ----------------------------------------------------------  
          -- Receiving forms  
          ----------------------------------------------------------  
          AND  
          (  
              @HasFormFilter = 0  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #ReceivingFormFilter RF  
                  WHERE RF.ID = 0  
                    AND ISNULL(EC.GetForm, 0) = 0  
              )  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #ReceivingFormFilter RF  
                  WHERE RF.ID = EC.GetForm  
              )  
          )  
  
  
  
          ----------------------------------------------------------  
          -- Enrollment filter  
          ----------------------------------------------------------  
          AND  
          (  
              @HasEnrollFilter = 0  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #EnrollmentFilter EF  
                  WHERE  
                      (  
                          EF.ID = 1  
                          AND  
                          (  
                              EN.EmployeeCodeID IS NULL  
                              OR  
                              (  
                                  EN.CoverageOfferDate IS NULL  
                                  AND ISNULL(EN.IsEnrolled, 0) = 0  
                                  AND ISNULL(EN.UnionMember, 0) = 0  
                                  AND ISNULL(EN.COBRAEnrolled, 0) = 0  
                                  AND ISNULL(EN.RetireeEnrolled, 0) = 0  
                              )  
                          )  
                      )  
  
  
  
                      OR  
                      (  
                          EF.ID = 2  
                          AND EN.CoverageOfferDate IS NOT NULL  
                          AND ISNULL(EN.IsEnrolled, 0) = 0  
                      )  
  
  
  
                      OR  
                      (  
                          EF.ID = 3  
                          AND EN.IsEnrolled = 1  
                      )  
  
  
  
                      OR  
                      (  
                          EF.ID = 4  
                          AND EN.UnionMember = 1  
                      )  
  
  
  
                      OR  
                      (  
                          EF.ID = 5  
                          AND EN.COBRAEnrolled = 1  
                      )  
  
  
  
                      OR  
                      (  
                          EF.ID = 6  
                          AND EN.RetireeEnrolled = 1  
                      )  
              )  
          )  
  
  
  
          ----------------------------------------------------------  
          -- Flag filter  
          ----------------------------------------------------------  
          AND  
          (  
              @HasFlagFilter = 0  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #FlagFilter FF  
                  WHERE FF.ID = -2  
                    AND EMP.FlagId IS NULL  
              )  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #FlagFilter FF  
                  WHERE FF.ID = EMP.FlagId  
              )  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #EmployeeFlags EF  
                  INNER JOIN #FlagFilter FF  
                      ON FF.ID = EF.FlagID  
                  WHERE EF.EmployeeSSN = EMP.ssn  
                    AND EF.EmployerID = EC.EmployerID  
              )  
          )
        OPTION (RECOMPILE);   -- 060: plan for the filters actually used  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 8. Remove duplicate Employee IDs  
        --  
        -- Employee can have multiple EmployeeCode records.  
        -- Keep newest EmpCodeID.  
        ------------------------------------------------------------  
        ;WITH D AS  
        (  
            SELECT  
                EmpCodeID,  
                ROW_NUMBER() OVER  
                (  
                    PARTITION BY Id  
                    ORDER BY EmpCodeID DESC  
                ) AS RN  
            FROM #TempResults  
        )  
        DELETE T  
        FROM #TempResults T  
        INNER JOIN D  
            ON D.EmpCodeID = T.EmpCodeID  
        WHERE D.RN > 1;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 9. Quick Filter  
        ------------------------------------------------------------  
        IF @QuickFilter IS NOT NULL  
           AND @QuickFilter <> ''  
           AND @QuickFilter <> 'all'  
        BEGIN  
  
  
  
            IF @QuickFilter = 'missing'  
            BEGIN  
                DELETE  
                FROM #TempResults  
                WHERE IsMissingData = 0;  
            END  
  
  
  
            ELSE IF @QuickFilter = 'flagged'  
            BEGIN  
                DELETE  
                FROM #TempResults  
                WHERE FlagId IS NULL  
                   OR IsMissingData = 1;  
            END  
  
  
  
            ELSE IF @QuickFilter = 'valid'  
            BEGIN  
                DELETE  
                FROM #TempResults  
                WHERE FlagId IS NOT NULL  
                   OR IsMissingData = 1;  
            END  
  
  
  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 10. Index temporary result  
        ------------------------------------------------------------  
        CREATE CLUSTERED INDEX IX_TempResults_Id  
            ON #TempResults(Id);  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 11. Count  
        ------------------------------------------------------------  
        DECLARE @RecordCount INT;  
  
  
  
        SELECT @RecordCount = COUNT(*)  
        FROM #TempResults;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 12. Final paging  
        ------------------------------------------------------------  
        SELECT  
            Id,  
            FirstName,  
            LastName,  
            FullName,  
            SSN,  
            Suffix,  
            EmployerName,  
            EmployeeStatus,  
            GetForm,  
            FlagId,  
            FlagDescription,  
            FlagSeverity,  
            IsMissingData,  
            EmpCodeID,  
  
  
  
            ROW_NUMBER() OVER  
            (  
                ORDER BY  
  
  
  
                CASE  
                    WHEN @SortColumn = 0  
                     AND @SortOrder = 'asc'  
                    THEN FullName  
                END ASC,  
  
  
  
                CASE  
                    WHEN @SortColumn = 0  
                     AND @SortOrder = 'desc'  
                    THEN FullName  
                END DESC,  
  
  
  
                CASE  
                    WHEN @SortColumn = 1  
                     AND @SortOrder = 'asc'  
                    THEN LastName  
                END ASC,  
  
  
  
                CASE  
                    WHEN @SortColumn = 1  
                     AND @SortOrder = 'desc'  
                    THEN LastName  
                END DESC,  
  
  
  
                Id ASC  
            ) AS RowNUM,  
  
  
  
            @RecordCount AS TotalCount  
  
  
  
        FROM #TempResults  
  
  
  
        ORDER BY  
  
  
  
            CASE  
                WHEN @SortColumn = 0  
                 AND @SortOrder = 'asc'  
                THEN FullName  
            END ASC,  
  
  
  
            CASE  
                WHEN @SortColumn = 0  
                 AND @SortOrder = 'desc'  
                THEN FullName  
            END DESC,  
  
  
  
            CASE  
                WHEN @SortColumn = 1  
                 AND @SortOrder = 'asc'  
                THEN LastName  
            END ASC,  
  
  
  
            CASE  
                WHEN @SortColumn = 1  
                 AND @SortOrder = 'desc'  
                THEN LastName  
            END DESC,  
  
  
  
            Id ASC  
  
  
  
        OFFSET @PageSize * (@PageIndex - 1) ROWS  
        FETCH NEXT @PageSize ROWS ONLY  
  
  
  
        OPTION (RECOMPILE);  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Metadata  
        ------------------------------------------------------------  
        SELECT  
            @RecordCount AS [RowCount],  
    @RecordCount AS [TotalCount];  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Cleanup  
        ------------------------------------------------------------  
        DROP TABLE #TempResults;  
        DROP TABLE #EmployeeFlags;  
        DROP TABLE #DataAvailability;  
        DROP TABLE #Enrollment;  
        DROP TABLE #Status;  
        DROP TABLE #EmployeeCodes;  
        DROP TABLE #EmployerFilter;  
        DROP TABLE #StatusFilter;  
        DROP TABLE #PlanFilter;  
        DROP TABLE #EnrollmentFilter;  
        DROP TABLE #FlagFilter;  
        DROP TABLE #ReceivingFormFilter;  
  
  
  
    END TRY  
  
  
  
    BEGIN CATCH  
  
  
  
        IF OBJECT_ID('tempdb..#TempResults') IS NOT NULL  
            DROP TABLE #TempResults;  
  
  
  
        IF OBJECT_ID('tempdb..#EmployeeFlags') IS NOT NULL  
            DROP TABLE #EmployeeFlags;  
  
  
  
        IF OBJECT_ID('tempdb..#DataAvailability') IS NOT NULL  
            DROP TABLE #DataAvailability;  
  
  
  
        IF OBJECT_ID('tempdb..#Enrollment') IS NOT NULL  
            DROP TABLE #Enrollment;  
  
  
  
        IF OBJECT_ID('tempdb..#Status') IS NOT NULL  
            DROP TABLE #Status;  
  
  
  
        IF OBJECT_ID('tempdb..#EmployeeCodes') IS NOT NULL  
            DROP TABLE #EmployeeCodes;  
  
  
  
        IF OBJECT_ID('tempdb..#EmployerFilter') IS NOT NULL  
            DROP TABLE #EmployerFilter;  
  
  
  
        IF OBJECT_ID('tempdb..#StatusFilter') IS NOT NULL  
            DROP TABLE #StatusFilter;  
  
  
  
        IF OBJECT_ID('tempdb..#PlanFilter') IS NOT NULL  
            DROP TABLE #PlanFilter;  
  
  
  
        IF OBJECT_ID('tempdb..#EnrollmentFilter') IS NOT NULL  
            DROP TABLE #EnrollmentFilter;  
  
  
  
        IF OBJECT_ID('tempdb..#FlagFilter') IS NOT NULL  
            DROP TABLE #FlagFilter;  
  
  
  
        IF OBJECT_ID('tempdb..#ReceivingFormFilter') IS NOT NULL  
            DROP TABLE #ReceivingFormFilter;  
  
  
  
        THROW;  
  
  
  
    END CATCH  
END
GO

-- =====================================================================
-- 2. Counts for the quick-filter badge bar. Applies the SAME filters as
--    the list (employer, plan, status, enrollment, flag, receiving forms,
--    search) so the counts reflect the filtered subset   only the quick
--    filter itself is excluded (each category is counted independently).
-- =====================================================================
CREATE OR ALTER PROCEDURE [dbo].[sp_Employee_QuickStats]
(
    @Employer_ID        BIGINT = NULL,
    @EmployerIDs        NVARCHAR(MAX) = NULL,
    @FillingYear        INT,
    @Search             NVARCHAR(100) = NULL,
    @PlanIDs            NVARCHAR(MAX) = NULL,
    @Statuses           NVARCHAR(MAX) = NULL,
    @EnrollmentIDs      NVARCHAR(MAX) = NULL,
    @Flags              NVARCHAR(MAX) = NULL,
    @IsReceivingForms   NVARCHAR(MAX) = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    -- 060: a temp table (with statistics) instead of a table variable, and whether it is used.
    CREATE TABLE #SelectedIDs (ID BIGINT NOT NULL PRIMARY KEY);

    IF (@EmployerIDs IS NOT NULL AND @EmployerIDs <> '[]' AND @EmployerIDs <> '')
        INSERT INTO #SelectedIDs SELECT DISTINCT value FROM OPENJSON(@EmployerIDs);
    ELSE IF (@Employer_ID IS NOT NULL)
    BEGIN
        INSERT INTO #SelectedIDs (ID) VALUES (@Employer_ID);
        INSERT INTO #SelectedIDs (ID) SELECT id FROM Employer WHERE companyId = @Employer_ID AND id <> @Employer_ID;
    END
    DECLARE @HasSelected BIT = CASE WHEN EXISTS (SELECT 1 FROM #SelectedIDs) THEN 1 ELSE 0 END;

    ;WITH FilteredEmployees AS (
        SELECT
            emp.id,
            emp.ssn,
            emp.FlagId,
            CASE WHEN NOT EXISTS (SELECT 1 FROM EmployeeHireSpan HS2 WHERE HS2.employeeCodeId = empCode.id)
                   OR NOT EXISTS (SELECT 1 FROM EmployeeStatus  ST2 WHERE ST2.employeeCodeId = empCode.id)
                   OR NOT EXISTS (SELECT 1 FROM EmployeeEnrollment EN2 WHERE EN2.employeeCodeId = empCode.id)
                   OR NOT EXISTS (SELECT 1 FROM EmployeePayroll  PR2 WHERE PR2.employeeCodeId = empCode.id)
                 THEN 1 ELSE 0 END AS IsMissingData,
            empCode.employerId,
            ROW_NUMBER() OVER(PARTITION BY emp.id ORDER BY empCode.id DESC) AS DuplicateRank
        FROM Employee emp
        INNER JOIN EmployeeCode empCode ON emp.id = empCode.employeeid
        LEFT JOIN EmployeeStatus ES ON ES.employeeCodeId = empCode.id
        LEFT JOIN EmployeeEnrollment EE ON EE.employeeCodeId = empCode.id
        WHERE emp.IsDeleted = 0
          AND emp.Flag = 0
          AND empCode.filingYear = @FillingYear
          AND (@HasSelected = 0 OR empCode.employerId IN (SELECT ID FROM #SelectedIDs))

          AND (@Statuses IS NULL OR @Statuses = '[]'
               OR ES.status IN (SELECT value FROM OPENJSON(@Statuses))
               -- '-2' = the "No status" option: employees with no status history
               OR (EXISTS (SELECT 1 FROM OPENJSON(@Statuses) WHERE value = '-2') AND ES.status IS NULL))
          AND (@PlanIDs IS NULL OR @PlanIDs = '[]'
                   OR EE.planId IN (SELECT value FROM OPENJSON(@PlanIDs))
                   -- '-2' = the "No plan" option: employees with no enrollment/plan
                   OR (EXISTS (SELECT 1 FROM OPENJSON(@PlanIDs) WHERE value = '-2') AND EE.planId IS NULL))
          AND (@IsReceivingForms IS NULL OR @IsReceivingForms = '[]'
               OR empCode.getForm IN (SELECT value FROM OPENJSON(@IsReceivingForms))
               -- NULL getForm counts as "Not Receiving Forms" (0)
               OR (EXISTS (SELECT 1 FROM OPENJSON(@IsReceivingForms) WHERE value = '0') AND empCode.getForm IS NULL))

          AND (@EnrollmentIDs IS NULL OR @EnrollmentIDs = '[]'
               OR (
                    (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '1')
                        AND (EE.id IS NULL OR (EE.coverageOfferDate IS NULL
                                               AND ISNULL(EE.isEnrolled, 0) = 0
                                               AND ISNULL(EE.unionMember, 0) = 0
                                               AND ISNULL(EE.COBRAEnrolled, 0) = 0
                                               AND ISNULL(EE.RetireeEnrolled, 0) = 0)))
                 OR (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '2')
                        AND EE.coverageOfferDate IS NOT NULL AND ISNULL(EE.isEnrolled, 0) = 0)
                 OR (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '3') AND EE.isEnrolled = 1)
                 OR (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '4') AND EE.unionMember = 1)
                 OR (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '5') AND EE.COBRAEnrolled = 1)
                 OR (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '6') AND EE.RetireeEnrolled = 1)
               ))

          AND (@Flags IS NULL OR @Flags = '[]'
               -- '-2' = the "No flags" option
               OR (EXISTS (SELECT 1 FROM OPENJSON(@Flags) WHERE value = '-2') AND emp.FlagId IS NULL)
               OR emp.FlagId IN (SELECT value FROM OPENJSON(@Flags))
               OR EXISTS (SELECT 1 FROM EmployeeFlags EF
                          WHERE EF.EmployeeSSN = emp.ssn
                            AND EF.EmployerId = empCode.employerId
                            AND EF.TaxYear = @FillingYear
                            AND EF.FlagId IN (SELECT value FROM OPENJSON(@Flags))))

          AND (ISNULL(@Search, '') = '' OR (emp.firstName LIKE '%' + @Search + '%' OR emp.lastName LIKE '%' + @Search + '%' OR emp.ssn LIKE '%' + @Search + '%'))
    )
    -- Mutually exclusive categories (missing data takes precedence over flags):
    -- Missing + Flagged + Valid = All
    SELECT
        COUNT(*)                                                            AS AllCount,
        ISNULL(SUM(CASE WHEN IsMissingData = 0 AND FlagId IS NOT NULL
                        THEN 1 ELSE 0 END), 0)                              AS FlaggedCount,
        ISNULL(SUM(CASE WHEN IsMissingData = 1 THEN 1 ELSE 0 END), 0)       AS MissingCount,
        ISNULL(SUM(CASE WHEN IsMissingData = 0 AND FlagId IS NULL
                        THEN 1 ELSE 0 END), 0)                              AS ValidCount
    FROM FilteredEmployees
    WHERE DuplicateRank = 1
    OPTION (RECOMPILE);   -- 060: plan for the filters actually used
END
GO
GO

/* ############################################################################
   061_acknowledged_warnings.sql
   ############################################################################ */
PRINT '=== 061_acknowledged_warnings.sql';
GO
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
GO

/* ############################################################################
   062_system_health.sql
   ############################################################################ */
PRINT '=== 062_system_health.sql';
GO
/* =============================================================================
   062 - Administration > System Health

   One page for SuperAdmin and Admin that says whether the app is well:
   database size and last backup, errors in the last day and week, slow pages
   (from the slow-request log), e-mail sending, files stuck part-way through
   processing, and the background jobs (read by the app from Hangfire).

   - dbo.sp_SystemHealth: the database figures, in five result sets.
   - Menu item Administration > System Health (SystemHealthController) with the
     Index and View permissions for SuperAdmin and Admin.
   - tbl_ErrorLogs: lines written as information (the slow-request log) were
     saved with Level 'Error' (LoggerService.LogInfo never set the level), so
     they counted as errors; they are now 'Info'.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

UPDATE dbo.tbl_ErrorLogs SET Level = 'Info' WHERE Type = 'Info' AND Level = 'Error';
PRINT CONCAT('  ', @@ROWCOUNT, ' information lines no longer counted as errors');
GO

CREATE OR ALTER PROCEDURE dbo.sp_SystemHealth
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Now DATETIME = GETDATE(), @UtcNow DATETIME = GETUTCDATE();

    -- 1. Database: size and last backup (msdb when readable, else the app's own backup list).
    DECLARE @LastBackup DATETIME = NULL, @BackupSource NVARCHAR(50) = NULL;
    BEGIN TRY
        SELECT @LastBackup = MAX(backup_finish_date) FROM msdb.dbo.backupset WHERE database_name = DB_NAME() AND type = 'D';
        IF @LastBackup IS NOT NULL SET @BackupSource = N'SQL Server';
    END TRY
    BEGIN CATCH
        SET @LastBackup = NULL;
    END CATCH
    IF @LastBackup IS NULL
        SELECT @LastBackup = MAX(CreatedOn), @BackupSource = CASE WHEN MAX(CreatedOn) IS NULL THEN NULL ELSE N'App backups' END
        FROM dbo.tbl_DBBackupHistory;

    SELECT DB_NAME() AS DatabaseName,
           CAST(SUM(CASE WHEN type = 0 THEN size END) * 8 / 1024.0 AS DECIMAL(12,1)) AS DataMb,
           CAST(SUM(CASE WHEN type = 1 THEN size END) * 8 / 1024.0 AS DECIMAL(12,1)) AS LogMb,
           @LastBackup AS LastBackup, @BackupSource AS BackupSource
    FROM   sys.database_files;

    -- 2. Errors: counts, and the most frequent in the last week (the slow-request log is not an error).
    SELECT SUM(CASE WHEN LoggedOnDate >= DATEADD(DAY, -1, @Now) THEN 1 ELSE 0 END) AS LastDay,
           COUNT(*) AS LastWeek
    FROM   dbo.tbl_ErrorLogs
    WHERE  Level = 'Error' AND LoggedOnDate >= DATEADD(DAY, -7, @Now);

    SELECT TOP (8) CallSite, LEFT(Message, 300) AS Message, COUNT(*) AS Times, MAX(LoggedOnDate) AS Last
    FROM   dbo.tbl_ErrorLogs
    WHERE  Level = 'Error' AND LoggedOnDate >= DATEADD(DAY, -7, @Now)
    GROUP  BY CallSite, LEFT(Message, 300)
    ORDER  BY COUNT(*) DESC, MAX(LoggedOnDate) DESC;

    -- 3. Slow requests in the last week (grouped by page in the app).
    SELECT TOP (5000) Message, AdditionalInfo, LoggedOnDate
    FROM   dbo.tbl_ErrorLogs
    WHERE  CallSite = 'SlowRequestLogMiddleware' AND LoggedOnDate >= DATEADD(DAY, -7, @Now)
    ORDER  BY LoggedOnDate DESC;

    -- 4. E-mail in the last week, by status.
    SELECT ISNULL(NULLIF(LTRIM(RTRIM(Status)), ''), 'Unknown') AS Status, COUNT(*) AS Emails, MAX(SentDate) AS Last
    FROM   dbo.CommunicationLog
    WHERE  SentDate >= DATEADD(DAY, -7, @Now)
    GROUP  BY ISNULL(NULLIF(LTRIM(RTRIM(Status)), ''), 'Unknown')
    ORDER  BY COUNT(*) DESC;

    -- 5. Files stuck part-way: still pre-processing, validating or importing an hour after their last step.
    SELECT TOP (20) F.FileLogId, F.OriginalFileName, F.EmployerName,
           CAST(F.ValidationStatusId AS INT) AS ValidationStatusId, CAST(F.WorkflowStatusId AS INT) AS WorkflowStatusId,
           COALESCE(H.LastStep, F.UploadedAt) AS Since
    FROM   dbo.UploadedFileLog F
    OUTER  APPLY (SELECT MAX(Timestamp) AS LastStep FROM dbo.FileLifecycleHistory X WHERE X.FileLogId = F.FileLogId) H
    WHERE  ISNULL(F.IsDeleted, 0) = 0
      AND  (F.WorkflowStatusId IN (200, 251) OR F.ValidationStatusId IN (20, 30))
      AND  COALESCE(H.LastStep, F.UploadedAt) < DATEADD(HOUR, -1, @Now)
    ORDER  BY COALESCE(H.LastStep, F.UploadedAt);
END
GO

------------------------------------------------------------
-- Menu: Administration > System Health, for SuperAdmin and Admin.
------------------------------------------------------------
DECLARE @Admin INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'Administration' AND ParentMenuId IS NULL AND Flag = 0 ORDER BY Id);
IF @Admin IS NULL THROW 50062, '062: the Administration menu container was not found.', 1;

DECLARE @Menu INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'SystemHealth' ORDER BY Id);
IF @Menu IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (@Admin, 'System Health', 'SystemHealth', 'bx bx-pulse',
            ISNULL((SELECT MAX(DisplayOrder) FROM dbo.tbl_Menus WHERE ParentMenuId = @Admin AND Flag = 0), 0) + 1,
            1, GETDATE(), 0, 1);
    SET @Menu = SCOPE_IDENTITY();
    PRINT '  menu item added';
END
ELSE
    UPDATE dbo.tbl_Menus SET ParentMenuId = @Admin, IsActive = 1, Flag = 0, IsVisible = 1, DateModified = GETDATE()
    WHERE Id = @Menu AND (ParentMenuId <> @Admin OR IsActive <> 1 OR Flag <> 0 OR IsVisible <> 1);

INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT r.ID, @Menu, a.Id, 1, GETDATE(), 'script 062'
FROM   dbo.tbl_Roles r
CROSS  JOIN dbo.tbl_ActionTypes a
WHERE  r.RoleName IN ('SuperAdmin', 'Admin') AND a.ActionName IN ('Index', 'View')
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions p WHERE p.RoleId = r.ID AND p.MenuId = @Menu AND p.ActionID = a.Id);

UPDATE p SET IsEnabled = 1, Date_Modified = GETDATE(), Modified_by = 'script 062'
FROM   dbo.tbl_AccessPermissions p
JOIN   dbo.tbl_ActionTypes a ON a.Id = p.ActionID
JOIN   dbo.tbl_Roles r ON r.ID = p.RoleId
WHERE  p.MenuId = @Menu AND r.RoleName IN ('SuperAdmin', 'Admin') AND a.ActionName IN ('Index', 'View') AND ISNULL(p.IsEnabled, 0) = 0;

PRINT '062: Administration > System Health (SuperAdmin, Admin).';
GO
GO

/* ############################################################################
   063_filing_deadlines.sql
   ############################################################################ */
PRINT '=== 063_filing_deadlines.sql';
GO
/* =============================================================================
   063 - Filing deadlines

   dbo.FilingDeadlines holds the dates that matter each filing (tax) year: the IRS
   dates and the state individual-mandate dates (CA, NJ, RI, DC, MA). They show as
   countdowns on every dashboard and as read-only entries in the Calendar, and the
   Employer dashboard's mailing / e-filing countdown reads the IRS ones (it assumed
   1 and 31 March of the next year).

   Seeded for tax years 2025 and 2026 (filed in 2026 and 2027). A date that falls
   on a weekend or federal holiday moves to the next business day. The state dates
   are marked NeedsCheck = 1: confirm them on Administration > Filing Deadlines
   (SuperAdmin, Admin), where dates can be added, changed or removed.
   Electronic filing is required at 10 or more returns, so the paper date rarely applies.

   Safe to re-run: seeded rows are added only when missing; edited rows are kept.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.FilingDeadlines') IS NULL
BEGIN
    CREATE TABLE dbo.FilingDeadlines
    (
        FilingDeadlineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FilingDeadlines PRIMARY KEY,
        FilingYear   INT            NOT NULL,   -- the tax year the forms are for
        Jurisdiction NVARCHAR(10)   NOT NULL,   -- IRS, CA, NJ, RI, DC, MA, ...
        Kind         NVARCHAR(20)   NOT NULL,   -- Furnish, E-file, Paper, Other
        Title        NVARCHAR(200)  NOT NULL,
        DueDate      DATE           NOT NULL,
        Notes        NVARCHAR(500)  NULL,
        NeedsCheck   BIT            NOT NULL CONSTRAINT DF_FilingDeadlines_NeedsCheck DEFAULT 0,
        IsActive     BIT            NOT NULL CONSTRAINT DF_FilingDeadlines_IsActive DEFAULT 1,
        ModifiedBy   NVARCHAR(256)  NULL,
        ModifiedAt   DATETIME2(0)   NOT NULL CONSTRAINT DF_FilingDeadlines_ModifiedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_FilingDeadlines_Due ON dbo.FilingDeadlines (DueDate) INCLUDE (FilingYear, Jurisdiction, Kind, IsActive);
END
GO

DECLARE @Seed TABLE (FilingYear INT, Jurisdiction NVARCHAR(10), Kind NVARCHAR(20), Title NVARCHAR(200), DueDate DATE, Notes NVARCHAR(500), NeedsCheck BIT);
INSERT @Seed VALUES
 -- Tax year 2025, filed in 2026
 (2025, 'IRS', 'Furnish', 'Furnish 1095-C / 1095-B to employees',            '2026-03-02', 'Due 31 January plus the automatic 30 days.', 0),
 (2025, 'IRS', 'Paper',   'Paper 1094-C / 1095-C to the IRS',                '2026-03-02', '28 February falls on a Saturday. Only under 10 returns.', 0),
 (2025, 'IRS', 'E-file',  'E-file 1094-C / 1095-C with the IRS (AIR)',       '2026-03-31', NULL, 0),
 (2025, 'CA',  'Furnish', 'California: statement to residents',             '2026-02-02', '31 January falls on a Saturday.', 1),
 (2025, 'CA',  'E-file',  'California: file with the Franchise Tax Board',  '2026-03-31', NULL, 1),
 (2025, 'NJ',  'Furnish', 'New Jersey: statement to residents',             '2026-03-02', NULL, 1),
 (2025, 'NJ',  'E-file',  'New Jersey: file with the Division of Taxation', '2026-03-31', NULL, 1),
 (2025, 'RI',  'Furnish', 'Rhode Island: statement to residents',           '2026-02-02', '31 January falls on a Saturday.', 1),
 (2025, 'RI',  'E-file',  'Rhode Island: file with the Division of Taxation', '2026-03-31', NULL, 1),
 (2025, 'DC',  'E-file',  'District of Columbia: file with the OTR',        '2026-04-30', '30 days after the IRS deadline.', 1),
 (2025, 'MA',  'Furnish', 'Massachusetts: Form MA 1099-HC to residents',    '2026-02-02', 'Issued by the insurer or plan.', 1),
 -- Tax year 2026, filed in 2027
 (2026, 'IRS', 'Furnish', 'Furnish 1095-C / 1095-B to employees',            '2027-03-02', 'Due 31 January plus the automatic 30 days.', 0),
 (2026, 'IRS', 'Paper',   'Paper 1094-C / 1095-C to the IRS',                '2027-03-01', '28 February falls on a Sunday. Only under 10 returns.', 0),
 (2026, 'IRS', 'E-file',  'E-file 1094-C / 1095-C with the IRS (AIR)',       '2027-03-31', NULL, 0),
 (2026, 'CA',  'Furnish', 'California: statement to residents',             '2027-02-01', '31 January falls on a Sunday.', 1),
 (2026, 'CA',  'E-file',  'California: file with the Franchise Tax Board',  '2027-03-31', NULL, 1),
 (2026, 'NJ',  'Furnish', 'New Jersey: statement to residents',             '2027-03-02', NULL, 1),
 (2026, 'NJ',  'E-file',  'New Jersey: file with the Division of Taxation', '2027-03-31', NULL, 1),
 (2026, 'RI',  'Furnish', 'Rhode Island: statement to residents',           '2027-02-01', '31 January falls on a Sunday.', 1),
 (2026, 'RI',  'E-file',  'Rhode Island: file with the Division of Taxation', '2027-03-31', NULL, 1),
 (2026, 'DC',  'E-file',  'District of Columbia: file with the OTR',        '2027-04-30', '30 days after the IRS deadline.', 1),
 (2026, 'MA',  'Furnish', 'Massachusetts: Form MA 1099-HC to residents',    '2027-02-01', 'Issued by the insurer or plan.', 1);

INSERT dbo.FilingDeadlines (FilingYear, Jurisdiction, Kind, Title, DueDate, Notes, NeedsCheck, ModifiedBy)
SELECT S.FilingYear, S.Jurisdiction, S.Kind, S.Title, S.DueDate, S.Notes, S.NeedsCheck, 'script 063'
FROM   @Seed S
WHERE  NOT EXISTS (SELECT 1 FROM dbo.FilingDeadlines D
                   WHERE D.FilingYear = S.FilingYear AND D.Jurisdiction = S.Jurisdiction AND D.Kind = S.Kind);
PRINT CONCAT('  ', @@ROWCOUNT, ' deadlines added');
GO

CREATE OR ALTER PROCEDURE dbo.sp_FilingDeadlines_List
    @FilingYear INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT FilingDeadlineId, FilingYear, Jurisdiction, Kind, Title, DueDate, Notes, NeedsCheck, ModifiedBy, ModifiedAt
    FROM   dbo.FilingDeadlines
    WHERE  IsActive = 1 AND (@FilingYear IS NULL OR FilingYear = @FilingYear)
    ORDER  BY FilingYear DESC, DueDate, CASE Jurisdiction WHEN 'IRS' THEN 0 ELSE 1 END, Jurisdiction;
END
GO

-- The next deadlines from a day on (today unless given).
CREATE OR ALTER PROCEDURE dbo.sp_FilingDeadlines_Upcoming
    @From DATE = NULL,
    @Top  INT  = 5
AS
BEGIN
    SET NOCOUNT ON;
    SET @From = ISNULL(@From, CAST(GETDATE() AS DATE));
    SELECT TOP (@Top) FilingDeadlineId, FilingYear, Jurisdiction, Kind, Title, DueDate, Notes, NeedsCheck, ModifiedBy, ModifiedAt
    FROM   dbo.FilingDeadlines
    WHERE  IsActive = 1 AND DueDate >= @From
    ORDER  BY DueDate, CASE Jurisdiction WHEN 'IRS' THEN 0 ELSE 1 END, Jurisdiction;
END
GO

-- Add (Id 0) or change one deadline. Saving marks it as checked.
CREATE OR ALTER PROCEDURE dbo.sp_FilingDeadlines_Save
    @FilingDeadlineId INT,
    @FilingYear   INT,
    @Jurisdiction NVARCHAR(10),
    @Kind         NVARCHAR(20),
    @Title        NVARCHAR(200),
    @DueDate      DATE,
    @Notes        NVARCHAR(500) = NULL,
    @ModifiedBy   NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(@FilingDeadlineId, 0) = 0
    BEGIN
        INSERT dbo.FilingDeadlines (FilingYear, Jurisdiction, Kind, Title, DueDate, Notes, NeedsCheck, ModifiedBy)
        VALUES (@FilingYear, UPPER(LTRIM(RTRIM(@Jurisdiction))), @Kind, LTRIM(RTRIM(@Title)), @DueDate, NULLIF(LTRIM(RTRIM(@Notes)), N''), 0, @ModifiedBy);
        SELECT CAST(SCOPE_IDENTITY() AS INT) AS FilingDeadlineId;
    END
    ELSE
    BEGIN
        UPDATE dbo.FilingDeadlines
        SET    FilingYear = @FilingYear, Jurisdiction = UPPER(LTRIM(RTRIM(@Jurisdiction))), Kind = @Kind, Title = LTRIM(RTRIM(@Title)),
               DueDate = @DueDate, Notes = NULLIF(LTRIM(RTRIM(@Notes)), N''), NeedsCheck = 0, ModifiedBy = @ModifiedBy, ModifiedAt = SYSUTCDATETIME()
        WHERE  FilingDeadlineId = @FilingDeadlineId AND IsActive = 1;
        SELECT CASE WHEN @@ROWCOUNT = 1 THEN @FilingDeadlineId ELSE 0 END AS FilingDeadlineId;
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_FilingDeadlines_Delete
    @FilingDeadlineId INT,
    @ModifiedBy NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.FilingDeadlines SET IsActive = 0, ModifiedBy = @ModifiedBy, ModifiedAt = SYSUTCDATETIME()
    WHERE  FilingDeadlineId = @FilingDeadlineId AND IsActive = 1;
    SELECT @@ROWCOUNT AS Removed;
END
GO

------------------------------------------------------------
-- Menu: Administration > Filing Deadlines, for SuperAdmin and Admin.
------------------------------------------------------------
DECLARE @Admin INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'Administration' AND ParentMenuId IS NULL AND Flag = 0 ORDER BY Id);
IF @Admin IS NULL THROW 50063, '063: the Administration menu container was not found.', 1;

DECLARE @Menu INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'FilingDeadlines' ORDER BY Id);
IF @Menu IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (@Admin, 'Filing Deadlines', 'FilingDeadlines', 'bx bx-calendar-exclamation',
            ISNULL((SELECT MAX(DisplayOrder) FROM dbo.tbl_Menus WHERE ParentMenuId = @Admin AND Flag = 0), 0) + 1,
            1, GETDATE(), 0, 1);
    SET @Menu = SCOPE_IDENTITY();
    PRINT '  menu item added';
END
ELSE
    UPDATE dbo.tbl_Menus SET ParentMenuId = @Admin, IsActive = 1, Flag = 0, IsVisible = 1, DateModified = GETDATE()
    WHERE Id = @Menu AND (ParentMenuId <> @Admin OR IsActive <> 1 OR Flag <> 0 OR IsVisible <> 1);

INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT r.ID, @Menu, a.Id, 1, GETDATE(), 'script 063'
FROM   dbo.tbl_Roles r
CROSS  JOIN dbo.tbl_ActionTypes a
WHERE  r.RoleName IN ('SuperAdmin', 'Admin') AND a.ActionName IN ('Index', 'View')
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions p WHERE p.RoleId = r.ID AND p.MenuId = @Menu AND p.ActionID = a.Id);

UPDATE p SET IsEnabled = 1, Date_Modified = GETDATE(), Modified_by = 'script 063'
FROM   dbo.tbl_AccessPermissions p
JOIN   dbo.tbl_ActionTypes a ON a.Id = p.ActionID
JOIN   dbo.tbl_Roles r ON r.ID = p.RoleId
WHERE  p.MenuId = @Menu AND r.RoleName IN ('SuperAdmin', 'Admin') AND a.ActionName IN ('Index', 'View') AND ISNULL(p.IsEnabled, 0) = 0;

PRINT '063: filing deadlines; Administration > Filing Deadlines (SuperAdmin, Admin).';
GO
GO

/* ############################################################################
   064_year_over_year.sql
   ############################################################################ */
PRINT '=== 064_year_over_year.sql';
GO
/* =============================================================================
   064 - Year over year

   The Penalty Risk page compares an employer's filing year with the year before
   (the employer row of the previous year with the same EIN), and the dashboards
   list the employers whose estimated penalty grew the most.

   Figures come from the 1095-C codes (dbo.EmployeeCode), so a year counts once its
   codes are generated: employees, full-time employees, months offered coverage,
   months enrolled (2C), months not offered while full-time and employed (1H with no
   Line 16: the 4980H(a) risk), offered months with no affordability safe harbor
   (Line 16 blank: the 4980H(b) risk), employees at risk, and the estimated penalty.

   - dbo.fn_EmployerYearFigures(@EmployerId, @Year): one row of figures.
   - dbo.sp_PenaltyRisk_YearOverYear(@EmployerId, @Year): this year and last year.
   - dbo.sp_PenaltyExposureGrowth(@Year, @EmployerIds, @Top): the employers among
     @EmployerIds (JSON array) whose estimated penalty grew most since last year.
   Read-only. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER FUNCTION dbo.fn_EmployerYearFigures (@EmployerId INT, @Year INT)
RETURNS TABLE
AS RETURN
(
    WITH C AS
    (
        SELECT EC.id, EC.employeeId, ISNULL(EC.IsAtPenaltyRisk, 0) AS AtRisk, ISNULL(EC.EstimatedPenalty, 0) AS Penalty,
               EC.ALLM_COC, EC.ALLM_SHC,
               EC.JAN_COC, EC.FEB_COC, EC.MAR_COC, EC.APR_COC, EC.MAY_COC, EC.JUN_COC, EC.JUL_COC, EC.AUG_COC, EC.SEP_COC, EC.OCT_COC, EC.NOV_COC, EC.DEC_COC,
               EC.JAN_SHC, EC.FEB_SHC, EC.MAR_SHC, EC.APR_SHC, EC.MAY_SHC, EC.JUN_SHC, EC.JUL_SHC, EC.AUG_SHC, EC.SEP_SHC, EC.OCT_SHC, EC.NOV_SHC, EC.DEC_SHC
        FROM   dbo.EmployeeCode EC
        JOIN   dbo.Employee E ON E.id = EC.employeeId AND ISNULL(E.IsDeleted, 0) = 0
        WHERE  EC.employerId = @EmployerId AND EC.filingYear = @Year
    ),
    M AS
    (
        SELECT C.id,
               NULLIF(LTRIM(RTRIM(COALESCE(V.Coc, C.ALLM_COC))), '') AS Coc,
               NULLIF(LTRIM(RTRIM(COALESCE(V.Shc, C.ALLM_SHC))), '') AS Shc
        FROM   C
        CROSS  APPLY (VALUES (C.JAN_COC, C.JAN_SHC), (C.FEB_COC, C.FEB_SHC), (C.MAR_COC, C.MAR_SHC), (C.APR_COC, C.APR_SHC),
                             (C.MAY_COC, C.MAY_SHC), (C.JUN_COC, C.JUN_SHC), (C.JUL_COC, C.JUL_SHC), (C.AUG_COC, C.AUG_SHC),
                             (C.SEP_COC, C.SEP_SHC), (C.OCT_COC, C.OCT_SHC), (C.NOV_COC, C.NOV_SHC), (C.DEC_COC, C.DEC_SHC)) V (Coc, Shc)
    )
    SELECT @EmployerId AS EmployerId, @Year AS FilingYear,
           (SELECT COUNT(DISTINCT employeeId) FROM C) AS Employees,
           (SELECT COUNT(DISTINCT C.employeeId) FROM C JOIN dbo.Employee E ON E.id = C.employeeId WHERE E.Status = 1) AS FullTimeEmployees,
           (SELECT COUNT(*) FROM M WHERE Coc IS NOT NULL AND Coc <> '1H') AS OfferedMonths,
           (SELECT COUNT(*) FROM M WHERE Shc = '2C') AS EnrolledMonths,
           (SELECT COUNT(*) FROM M WHERE Coc = '1H' AND Shc IS NULL) AS NotOfferedMonths,
           (SELECT COUNT(*) FROM M WHERE Coc IS NOT NULL AND Coc NOT IN ('1H', '1A', '1G') AND Shc IS NULL) AS UnaffordableMonths,
           (SELECT COUNT(*) FROM C WHERE AtRisk = 1) AS AtRiskEmployees,
           (SELECT CAST(ISNULL(SUM(Penalty), 0) AS DECIMAL(18,2)) FROM C) AS EstimatedPenalty,
           (SELECT COUNT(*) FROM C) AS CodeRows
);
GO

CREATE OR ALTER PROCEDURE dbo.sp_PenaltyRisk_YearOverYear
    @EmployerId INT,
    @Year       INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ein NVARCHAR(20) = (SELECT taxid FROM dbo.Employer WHERE id = @EmployerId);
    DECLARE @Prior INT = (SELECT TOP (1) id FROM dbo.Employer
                          WHERE taxid = @Ein AND filingYear = @Year - 1 ORDER BY CASE WHEN companyId IS NULL THEN 0 ELSE 1 END, id);

    SELECT * FROM dbo.fn_EmployerYearFigures(@EmployerId, @Year)
    UNION ALL
    SELECT * FROM dbo.fn_EmployerYearFigures(@Prior, @Year - 1) WHERE @Prior IS NOT NULL;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_PenaltyExposureGrowth
    @Year        INT,
    @EmployerIds NVARCHAR(MAX),
    @Top         INT = 5
AS
BEGIN
    SET NOCOUNT ON;
    IF @Top IS NULL OR @Top NOT BETWEEN 1 AND 50 SET @Top = 5;

    CREATE TABLE #E (Id INT PRIMARY KEY);
    INSERT #E SELECT DISTINCT TRY_CONVERT(INT, value) FROM OPENJSON(@EmployerIds) WHERE TRY_CONVERT(INT, value) IS NOT NULL;

    ;WITH P AS
    (
        SELECT ER.id AS EmployerId, ER.name AS EmployerName, ER.taxid AS Ein, PR.id AS PriorEmployerId
        FROM   #E
        JOIN   dbo.Employer ER ON ER.id = #E.Id AND ER.filingYear = @Year
        CROSS  APPLY (SELECT TOP (1) X.id FROM dbo.Employer X WHERE X.taxid = ER.taxid AND X.filingYear = @Year - 1
                      ORDER BY CASE WHEN X.companyId IS NULL THEN 0 ELSE 1 END, X.id) PR
    ),
    T AS
    (
        SELECT P.EmployerId, P.EmployerName, P.Ein,
               (SELECT ISNULL(SUM(EstimatedPenalty), 0) FROM dbo.EmployeeCode WHERE employerId = P.EmployerId AND filingYear = @Year) AS ThisYear,
               (SELECT ISNULL(SUM(EstimatedPenalty), 0) FROM dbo.EmployeeCode WHERE employerId = P.PriorEmployerId AND filingYear = @Year - 1) AS LastYear,
               (SELECT COUNT(*) FROM dbo.EmployeeCode WHERE employerId = P.PriorEmployerId AND filingYear = @Year - 1) AS LastYearCodes
        FROM   P
    )
    SELECT TOP (@Top) EmployerId, EmployerName, Ein,
           CAST(ThisYear AS DECIMAL(18,2)) AS ThisYear, CAST(LastYear AS DECIMAL(18,2)) AS LastYear,
           CAST(ThisYear - LastYear AS DECIMAL(18,2)) AS Growth
    FROM   T
    WHERE  LastYearCodes > 0 AND ThisYear > LastYear
    ORDER  BY ThisYear - LastYear DESC, EmployerName;
END
GO
GO

/* ############################################################################
   065_validation_speed.sql
   ############################################################################ */
PRINT '=== 065_validation_speed.sql';
GO
/* =============================================================================
   065 - Faster validation (and so faster saves in the Error Triage editor)

   sp_Validation_UpdateStatus updated the statistics of StagingRowErrors and of
   every staging table (a 20% sample of all files' rows) each time a file was
   validated. Every save in the Correction Editor re-validates the file, so on a
   5,000-employee file this was 4.3 of the 9.5 seconds a save took, and it grows
   with every file ever uploaded. SQL Server keeps statistics current itself.

   The procedure is the database's current one (script 053) without those steps.
   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_Validation_UpdateStatus]
(
    @FileLogId INT
)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TableName NVARCHAR(255), @PKColumn NVARCHAR(255), @SQL NVARCHAR(MAX);

    -- 065: no statistics update here. It ran on every validation, over every file's rows
    -- (StagingRowErrors and each staging table, 20% sample), and was most of the time a
    -- save in the Correction Editor took. SQL Server keeps the statistics up to date itself.

    DECLARE TableCursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT TableName, PrimaryKeyColumn FROM StagingTableMetadata;

    OPEN TableCursor;
    FETCH NEXT FROM TableCursor INTO @TableName, @PKColumn;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- 3. Invalidate rows with an error-severity issue in this table (053: warnings and
        --    information notes do not keep a row out of the import).
        SET @SQL = '
            UPDATE T
            SET T.IsValid = 0
            FROM ' + QUOTENAME(@TableName) + ' AS T
            INNER JOIN StagingRowErrors AS E
                ON E.StagingRowId = T.' + QUOTENAME(@PKColumn) + '
            LEFT JOIN ValidationRules AS R ON R.RuleId = E.RuleId
            WHERE T.FileLogId = @FileLogId
              AND E.FileLogId = @FileLogId
              -- Row IDs only invalidate their own table
              AND E.StagingTableName = ''' + @TableName + '''
              AND ISNULL(R.Severity, ''Error'') NOT IN (''Warning'', ''Information'', ''Info'')
              AND T.IsValid <> 0
            OPTION (MAXDOP 1);';
        EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId = @FileLogId;

        FETCH NEXT FROM TableCursor INTO @TableName, @PKColumn;
    END
    CLOSE TableCursor;
    DEALLOCATE TableCursor;
END
GO
GO

/* ############################################################################
   066_duplicate_ssns.sql
   ############################################################################ */
PRINT '=== 066_duplicate_ssns.sql';
GO
/* =============================================================================
   066 - Duplicate SSNs across employers and files (idea #8)

   1. An index on Employee.ssn, so the new check and the report do not scan
      every employee.
   2. A new validation rule, EMP-160 "SSN Under Another Employer" (a warning,
      never an error): the uploaded SSN already belongs to an employee of a
      different employer for the same plan year. Employers named in the file
      itself (primary or affiliated EIN) do not count.
   3. sp_Validation_ExecuteRules: the handler for that rule (keyword
      SSN_UnderOtherEmployer). Everything else is the database's current
      procedure (script 055), unchanged.
   4. sp_DuplicateSsn_Report: SSNs that appear under more than one employer,
      more than once under one employer, or with different names or birth
      dates, among the employers the user may see.
   5. Reports > Duplicate SSNs for the staff roles.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Employee') AND name = 'IX_Employee_Ssn')
    CREATE NONCLUSTERED INDEX IX_Employee_Ssn ON dbo.Employee (ssn) INCLUDE (EmployerId, IsDeleted);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ValidationRules WHERE RuleCode = 'EMP-160')
    INSERT INTO dbo.ValidationRules (RuleName, TargetTable, TargetColumn, ValidationType, RuleParameter1, RuleParameter2,
                                     ErrorMessage, IsActive, Severity, IsCorrectable, HelpText, Topic, AuditTabNote,
                                     EmailSummaryNote, RuleCode, IsAutoCorrect)
    VALUES ('SSN Under Another Employer', 'Staging_Employees', 'EmployeeSSN', 'CUSTOM_LOGIC', 'SSN_UnderOtherEmployer', NULL,
            'This SSN already belongs to an employee of another employer for this plan year.', 1, 'Warning', 0,
            'Check that the SSN is right. The same person can work for two unrelated employers; a typing mistake is more likely when the names differ.',
            'Duplicate SSNs',
            'This SSN is already on file for another employer this year.',
            '# employees have an SSN already on file for another employer.',
            'EMP-160', 0);
GO

-- 3. Procedures --------------------------------------------------------------
-- sp_Validation_ExecuteRules
/* ---------------------------------------------------------------- PART 1 ----
   sp_Validation_ExecuteRules - handlers for the eight missing types, repairs for
   the two malformed blocks, and the missing CUSTOM_LOGIC keyword.
   ---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE [dbo].[sp_Validation_ExecuteRules] @FileLogId INT AS
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

CREATE OR ALTER PROCEDURE dbo.sp_DuplicateSsn_Report
    @UserId INT,
    @Year   INT
AS
BEGIN
    SET NOCOUNT ON;

    /* One row per employee record whose SSN is in a group worth a look. The SSN
       itself is never returned: the group number ties the rows together and the
       last four digits are enough to recognise it. */
    ;WITH People AS (
        SELECT E.id AS EmployeeId, E.ssn,
               UPPER(LTRIM(RTRIM(ISNULL(E.firstName, '')))) AS FirstKey,
               UPPER(LTRIM(RTRIM(ISNULL(E.lastName, ''))))  AS LastKey,
               LTRIM(RTRIM(CONCAT(E.firstName, ' ', E.lastName))) AS EmployeeName,
               E.birthday, E.FileLogId,
               R.id AS EmployerId, R.name AS EmployerName, R.taxid AS EIN
        FROM   dbo.Employee E
        JOIN   dbo.Employer R ON R.id = E.EmployerId
        JOIN   dbo.fn_DashboardEmployerScope(@UserId, @Year) S ON S.EmployerId = R.id
        WHERE  ISNULL(E.IsDeleted, 0) = 0
          AND  R.filingYear = @Year
          AND  ISNULL(LTRIM(E.ssn), '') <> ''
    ), Groups AS (
        SELECT ssn,
               COUNT(*)                                AS Records,
               COUNT(DISTINCT EmployerId)              AS Employers,
               COUNT(DISTINCT FirstKey + '|' + LastKey) AS Names,
               COUNT(DISTINCT birthday)                AS Birthdates
        FROM   People
        GROUP  BY ssn
        HAVING COUNT(DISTINCT EmployerId) > 1
            OR COUNT(*) > COUNT(DISTINCT EmployerId)
            OR COUNT(DISTINCT FirstKey + '|' + LastKey) > 1
            OR COUNT(DISTINCT birthday) > 1
    )
    SELECT DENSE_RANK() OVER (ORDER BY G.Employers DESC, G.Names DESC, G.ssn) AS GroupNo,
           'XXX-XX-' + RIGHT(G.ssn, 4) AS SsnMasked,
           G.Records, G.Employers, G.Names, G.Birthdates,
           CAST(CASE WHEN G.Employers > 1 THEN 1 ELSE 0 END AS BIT)            AS SeveralEmployers,
           CAST(CASE WHEN G.Records > G.Employers THEN 1 ELSE 0 END AS BIT)    AS RepeatedInEmployer,
           CAST(CASE WHEN G.Names > 1 THEN 1 ELSE 0 END AS BIT)                AS DifferentNames,
           CAST(CASE WHEN G.Birthdates > 1 THEN 1 ELSE 0 END AS BIT)           AS DifferentBirthdates,
           P.EmployeeId, P.EmployeeName, P.birthday AS Birthdate,
           P.EmployerId, P.EmployerName, P.EIN, CAST(P.FileLogId AS INT) AS FileLogId,
           F.OriginalFileName AS FileName
    FROM   Groups G
    JOIN   People P ON P.ssn = G.ssn
    LEFT   JOIN dbo.UploadedFileLog F ON F.FileLogId = P.FileLogId
    ORDER  BY GroupNo, P.EmployerName, P.EmployeeName;
END
GO

DECLARE @Reports INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'Report' AND ParentMenuId IS NULL AND Flag = 0 ORDER BY Id);
IF @Reports IS NULL THROW 50066, '066: the Reports menu container was not found.', 1;

DECLARE @Menu INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'DuplicateSsn' ORDER BY Id);
IF @Menu IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (@Reports, 'Duplicate SSNs', 'DuplicateSsn', 'bx bx-copy-alt',
            ISNULL((SELECT MAX(DisplayOrder) FROM dbo.tbl_Menus WHERE ParentMenuId = @Reports AND Flag = 0), 0) + 1,
            1, GETDATE(), 0, 1);
    SET @Menu = SCOPE_IDENTITY();
    PRINT '  menu item added';
END
ELSE
    UPDATE dbo.tbl_Menus SET ParentMenuId = @Reports, IsActive = 1, Flag = 0, IsVisible = 1, DateModified = GETDATE()
    WHERE Id = @Menu AND (ParentMenuId <> @Reports OR IsActive <> 1 OR Flag <> 0 OR IsVisible <> 1);

INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT r.ID, @Menu, a.Id, 1, GETDATE(), 'script 066'
FROM   dbo.tbl_Roles r
CROSS  JOIN dbo.tbl_ActionTypes a
WHERE  r.RoleName IN ('SuperAdmin', 'Admin', 'ACA Director', 'DA Supervisor', 'Data Analyst',
                      'Account Manager Supervisor', 'Account Manager')
  AND  a.ActionName IN ('Index', 'View')
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions p WHERE p.RoleId = r.ID AND p.MenuId = @Menu AND p.ActionID = a.Id);

PRINT '066: duplicate SSN warning (EMP-160), sp_DuplicateSsn_Report, Reports > Duplicate SSNs.';
GO
GO

/* ############################################################################
   067_rule_what_if.sql
   ############################################################################ */
PRINT '=== 067_rule_what_if.sql';
GO
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
GO

/* ############################################################################
   068_employee_change_history.sql
   ############################################################################ */
PRINT '=== 068_employee_change_history.sql';
GO
/* =============================================================================
   068 - Employee change history (idea #19)

   Hand edits were already audited (DataAuditlog) and code changes too
   (EmployeeCodeAuditTrail); what an import changed on an employee was not
   kept anywhere.

   1. EmployeeChangeHistory: one row per employee the import added or
      restored, and per field it changed (old -> new), with the file.
   2. sp_ImportData_FromStaging: the employee MERGE now writes those rows.
      Everything else is the database's current procedure (script 048),
      unchanged.
   3. sp_EmployeeChangeHistory: the employee's history from all three
      sources, newest first. SSNs are shown by their last four digits.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.EmployeeChangeHistory') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeChangeHistory (
        ChangeId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeChangeHistory PRIMARY KEY,
        EmployeeId BIGINT        NOT NULL,
        ChangedAt  DATETIME2(0)  NOT NULL CONSTRAINT DF_EmployeeChangeHistory_ChangedAt DEFAULT SYSDATETIME(),
        Source     VARCHAR(20)   NOT NULL,   -- Import
        FileLogId  INT           NULL,
        ChangeKind VARCHAR(20)   NOT NULL,   -- Added, Restored, Changed
        FieldName  NVARCHAR(64)  NULL,
        OldValue   NVARCHAR(400) NULL,
        NewValue   NVARCHAR(400) NULL
    );
    CREATE INDEX IX_EmployeeChangeHistory_Employee ON dbo.EmployeeChangeHistory (EmployeeId, ChangedAt DESC);
    CREATE INDEX IX_EmployeeChangeHistory_File ON dbo.EmployeeChangeHistory (FileLogId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.DataAuditlog') AND name = 'IX_DataAuditlog_Record')
    CREATE INDEX IX_DataAuditlog_Record ON dbo.DataAuditlog (Record_Id, Table_Name) INCLUDE (Created_at);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.EmployeeCodeAuditTrail') AND name = 'IX_EmployeeCodeAuditTrail_Employee')
    CREATE INDEX IX_EmployeeCodeAuditTrail_Employee ON dbo.EmployeeCodeAuditTrail (EmployeeId, ChangedOn DESC);
GO

-- 1. Import: rows without an Affiliated EIN ------------------------------------
/* =============================================================================
   PART 1 -- sp_ImportData_FromStaging: resolve an employee's plan anywhere in
             the group, not only under their own employer record.
   ============================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_ImportData_FromStaging]  
    @FileLogId INT,  
    @ImportMode VARCHAR(20) = 'Merge'   
AS  
BEGIN  
    SET NOCOUNT ON;  
    DECLARE @TranStarted BIT = 0;  
  
    IF @@TRANCOUNT = 0  
    BEGIN  
        BEGIN TRANSACTION;  
        SET @TranStarted = 1;  
    END  
  
    BEGIN TRY  
        DECLARE      
            @PlanYear INT,      
            @PrimaryEIN VARCHAR(15),  
            @ParentEmployerId INT,  
            @ParentNameFromLog NVARCHAR(255);      
  
        SELECT      
            @PlanYear   = PlanYear,      
            @PrimaryEIN = LTRIM(RTRIM(TaxID)),  
            @ParentNameFromLog = EmployerName  
        FROM UploadedFileLog      
        WHERE FileLogId = @FileLogId;      
  
        IF @PrimaryEIN IS NULL OR @PrimaryEIN = ''  
        BEGIN  
            SELECT TOP 1 @PrimaryEIN = LTRIM(RTRIM(PrimaryEIN))  
            FROM Staging_Employers   
            WHERE FileLogId = @FileLogId AND PrimaryEIN IS NOT NULL AND LTRIM(RTRIM(PrimaryEIN)) <> '';  
              
            IF @PrimaryEIN IS NULL OR @PrimaryEIN = ''  
            BEGIN  
                SELECT TOP 1 @PrimaryEIN = LTRIM(RTRIM(AffiliatedEIN))  
                FROM Staging_Employers   
                WHERE FileLogId = @FileLogId AND AffiliatedEIN IS NOT NULL AND LTRIM(RTRIM(AffiliatedEIN)) <> '';  
            END  
        END  
  
        IF @PrimaryEIN IS NULL OR @PrimaryEIN = ''  
        BEGIN  
            THROW 50011, 'Critical Error: Primary EIN is missing. Import aborted.', 1;  
        END  
  
        IF @PlanYear IS NULL OR @PlanYear = 0  
        BEGIN  
            SELECT TOP 1 @PlanYear = YEAR(StartDate) FROM Staging_Premiums WHERE FileLogId = @FileLogId AND StartDate IS NOT NULL;  
            IF @PlanYear IS NULL  
                SELECT TOP 1 @PlanYear = YEAR(StatusStartDate) FROM Staging_Employees WHERE FileLogId = @FileLogId AND StatusStartDate IS NOT NULL;  
        END  
        SET @PlanYear = ISNULL(@PlanYear, YEAR(GETDATE()));  
  
        UPDATE UploadedFileLog SET TaxID = @PrimaryEIN, PlanYear = @PlanYear WHERE FileLogId = @FileLogId;  
  
        /* 1. ESTABLISH PARENT EMPLOYER */   
        MERGE Employer AS T  
        USING (  
            SELECT TOP 1   
                PrimaryEIN as taxid, EmployerName, Address, Address2, City, StateOrProvince, Zip, Contact, Phone, Title  
            FROM Staging_Employers   
            WHERE FileLogId = @FileLogId AND PrimaryEIN = @PrimaryEIN  
        ) AS S  
        ON T.taxid = S.taxid AND T.filingYear = @PlanYear  
        WHEN MATCHED THEN   
            UPDATE SET   
                T.name = ISNULL(S.EmployerName, T.name),  
                T.address = ISNULL(S.Address, T.address),  
                T.address2 = ISNULL(S.Address2, T.address2),  
                T.city = ISNULL(S.City, T.city),  
                T.state = ISNULL(S.StateOrProvince, T.state),  
                T.zip = ISNULL(S.Zip, T.zip),  
                T.contactName = ISNULL(S.Contact, T.contactName),  
                T.phoneNumber = ISNULL(S.Phone, T.phoneNumber),  
                T.signTitle = ISNULL(S.Title, T.signTitle),  
                T.FileLogId = @FileLogId,  
                T.companyId = NULL  
        WHEN NOT MATCHED THEN  
            INSERT (FileLogId, taxid, name, filingYear, address, address2, city, state, zip, contactName, phoneNumber, signTitle, companyId, isAuthoritative)  
            VALUES (@FileLogId, @PrimaryEIN, S.EmployerName, @PlanYear, S.Address, S.Address2, S.City, S.StateOrProvince, S.Zip, S.Contact, S.Phone, S.Title, NULL, 1);  
  
        SELECT @ParentEmployerId = id FROM Employer WHERE taxid = @PrimaryEIN AND filingYear = @PlanYear;  
  
        IF @ParentEmployerId IS NULL  
            THROW 50010, 'Critical Error: Could not establish ParentEmployerId. Import aborted.', 1;  
  
        /* 0.5. OVERWRITE CLEANUP */   
        IF @ImportMode = 'Overwrite'  
        BEGIN  
            DECLARE @TargetEmployeeCodes TABLE (Id INT);  
            INSERT INTO @TargetEmployeeCodes (Id) SELECT id FROM EmployeeCode WHERE employerId = @ParentEmployerId AND filingYear = @PlanYear;  
  
            DELETE FROM EmployeeEnrollment WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM EmployeePayroll WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM CoveredIndividual WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM EmployeeHireSpan WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM Premium WHERE EmployerId = @ParentEmployerId;  
            DELETE FROM Employer_AggregatedGroupMembers WHERE EmployerId = @ParentEmployerId;  
        END  
  
        /* 2. AFFILIATE EMPLOYERS (FIXED DUPLICATE CRASH) */   
        MERGE Employer AS T      
        USING (      
            SELECT AffiliatedEIN, EmployerName, Address, Address2, City, StateOrProvince, Zip, Contact, Phone, Title
            FROM (
                SELECT AffiliatedEIN, EmployerName, Address, Address2, City, StateOrProvince, Zip, Contact, Phone, Title,
                       ROW_NUMBER() OVER(PARTITION BY AffiliatedEIN ORDER BY (SELECT NULL)) as rn
                FROM Staging_Employers
                WHERE FileLogId = @FileLogId AND IsValid = 1 AND AffiliatedEIN <> @PrimaryEIN
                  AND NULLIF(LTRIM(RTRIM(AffiliatedEIN)), '') IS NOT NULL   -- 048: a row without an affiliate EIN is not an affiliate
            ) Sub WHERE rn = 1
        ) AS S      
        ON T.taxid = S.AffiliatedEIN AND T.filingYear = @PlanYear      
        WHEN MATCHED THEN      
            UPDATE SET   
                name = S.EmployerName, address = S.Address, address2 = S.Address2, city = S.City, state = S.StateOrProvince, zip = S.Zip,  
                companyId = @ParentEmployerId, FileLogId = @FileLogId      
        WHEN NOT MATCHED THEN      
            INSERT (FileLogId, taxid, name, filingYear, address, address2, city, state, zip, contactName, phoneNumber, signTitle, companyId, isAuthoritative)      
            VALUES (@FileLogId, S.AffiliatedEIN, S.EmployerName, @PlanYear, S.Address, S.Address2, S.City, S.StateOrProvince, S.Zip, S.Contact, S.Phone, S.Title, @ParentEmployerId, 0);      
        
       /* 2.5. TRACKER EXTENSION HEALING (FIXED) */   
        INSERT INTO [dbo].[Tracker_EmployerExtension] (EmployerId)
        SELECT DISTINCT E.id
        FROM [dbo].[Employer] E
        LEFT JOIN [dbo].[Tracker_EmployerExtension] EE ON E.id = EE.EmployerId
        WHERE (E.FileLogId = @FileLogId OR E.id = @ParentEmployerId)
          AND EE.EmployerId IS NULL;
  
        /* 3. AGGREGATED GROUP MEMBERS */      
        INSERT INTO Employer_AggregatedGroupMembers (EmployerId, MemberName, MemberEIN)      
        SELECT DISTINCT @ParentEmployerId, S.EmployerName, S.AffiliatedEIN      
        FROM Staging_Employers S      
        WHERE S.FileLogId = @FileLogId      
          AND NULLIF(LTRIM(RTRIM(S.AffiliatedEIN)), '') IS NOT NULL   -- 048: MemberEIN is NOT NULL; a blank one stopped the import
          AND NOT EXISTS (SELECT 1 FROM Employer_AggregatedGroupMembers AG WHERE AG.MemberEIN = S.AffiliatedEIN AND AG.EmployerId = @ParentEmployerId);      
  
        /* 068: what this import changes on each employee, kept as the employee's history */
        IF OBJECT_ID('tempdb..#EmpImportChanges') IS NOT NULL DROP TABLE #EmpImportChanges;
        CREATE TABLE #EmpImportChanges (
            MergeAction NVARCHAR(10) NOT NULL, EmployeeId BIGINT NOT NULL, WasDeleted BIT NULL,
            Old0 NVARCHAR(400) NULL, New0 NVARCHAR(400) NULL,
            Old1 NVARCHAR(400) NULL, New1 NVARCHAR(400) NULL,
            Old2 NVARCHAR(400) NULL, New2 NVARCHAR(400) NULL,
            Old3 NVARCHAR(400) NULL, New3 NVARCHAR(400) NULL,
            Old4 NVARCHAR(400) NULL, New4 NVARCHAR(400) NULL,
            Old5 NVARCHAR(400) NULL, New5 NVARCHAR(400) NULL,
            Old6 NVARCHAR(400) NULL, New6 NVARCHAR(400) NULL,
            Old7 NVARCHAR(400) NULL, New7 NVARCHAR(400) NULL,
            Old8 NVARCHAR(400) NULL, New8 NVARCHAR(400) NULL,
            Old9 NVARCHAR(400) NULL, New9 NVARCHAR(400) NULL,
            Old10 NVARCHAR(400) NULL, New10 NVARCHAR(400) NULL,
            Old11 NVARCHAR(400) NULL, New11 NVARCHAR(400) NULL,
            Old12 NVARCHAR(400) NULL, New12 NVARCHAR(400) NULL,
            Old13 NVARCHAR(400) NULL, New13 NVARCHAR(400) NULL,
            Old14 NVARCHAR(400) NULL, New14 NVARCHAR(400) NULL,
            Old15 NVARCHAR(400) NULL, New15 NVARCHAR(400) NULL,
            Old16 NVARCHAR(400) NULL, New16 NVARCHAR(400) NULL,
            Old17 NVARCHAR(400) NULL, New17 NVARCHAR(400) NULL,
            Old18 NVARCHAR(400) NULL, New18 NVARCHAR(400) NULL
        );

        /* 4. EMPLOYEES (FIXED DUPLICATE CRASH) */      
        MERGE Employee AS T      
        USING (      
            SELECT * FROM (
                SELECT      
                    S.EmployeeSSN, S.EmployeeLegalFirstName, S.EmployeeLegalLastName, S.EmployeeMiddleInitial, S.EmployeeSuffix,  
                    S.Address1, S.Address2, S.City, S.StateOrProvince, S.Zip, S.Country,  
                    S.EmailAddress,  
                    S.CoverageElected,   
                    S.LowestCostPlanOffered,  
                    TRY_CAST(S.CoverageStartDate AS DATE) AS CoverageStartDate,  
                    TRY_CAST(S.CoverageEndDate AS DATE) AS CoverageEndDate,  
                    TRY_CAST(S.DateEligibleForCoverage AS DATE) AS DateEligibleForCoverage,  
                    TRY_CAST(S.EmployeeBirthdate AS DATE) AS BirthDate,      
                    TRY_CAST(S.HireDate AS DATE) AS HireDate,      
                    TRY_CAST(S.TerminationDate AS DATE) AS TerminationDate,      
                    TRY_CAST(S.Status AS INT) AS Status,      
                    COALESCE(E.id, @ParentEmployerId) AS FinalEmployerId,
                    ROW_NUMBER() OVER(PARTITION BY S.EmployeeSSN, COALESCE(E.id, @ParentEmployerId) ORDER BY ISNULL(TRY_CAST(S.StatusStartDate AS DATE), TRY_CAST(S.HireDate AS DATE)) DESC) as rn
                FROM Staging_Employees S      
                LEFT JOIN Employer E ON S.EINAssociatedWithEE = E.taxid AND E.filingYear = @PlanYear      
                WHERE S.FileLogId = @FileLogId AND S.IsValid = 1  
            ) Sub WHERE rn = 1
        ) AS S      
        ON T.ssn = S.EmployeeSSN AND T.EmployerId = S.FinalEmployerId      
        WHEN MATCHED THEN      
            UPDATE SET   
                firstName = S.EmployeeLegalFirstName,   
                lastName = S.EmployeeLegalLastName,   
                middleName = S.EmployeeMiddleInitial,   
                suffix = S.EmployeeSuffix,  
                address = S.Address1,   
                address2 = S.Address2,   
                city = S.City,   
                state = S.StateOrProvince,   
                zip = S.Zip,  
                birthday = S.BirthDate,   
                HireDate = S.HireDate,   
                TerminationDate = S.TerminationDate,   
                Status = S.Status,  
                CoverageElected = S.CoverageElected,  
                CoverageStartDate = S.CoverageStartDate,  
                CoverageEndDate = S.CoverageEndDate,  
                LowestCostPlanOffered = S.LowestCostPlanOffered,  
                DateEligibleForCoverage = S.DateEligibleForCoverage,  
                email = S.EmailAddress,  
                IsDeleted = 0      
        WHEN NOT MATCHED THEN      
            INSERT (  
                FileLogId, EmployerId, fullName, firstName, lastName, middleName, suffix,   
                ssn, address, address2, city, state, zip, birthday, HireDate, TerminationDate,   
                Status, CoverageElected, CoverageStartDate, CoverageEndDate,   
                LowestCostPlanOffered, DateEligibleForCoverage, email, IsDeleted  
            )      
            VALUES (  
                @FileLogId, S.FinalEmployerId, S.EmployeeLegalFirstName + ' ' + S.EmployeeLegalLastName,   
                S.EmployeeLegalFirstName, S.EmployeeLegalLastName, S.EmployeeMiddleInitial, S.EmployeeSuffix,   
                S.EmployeeSSN, S.Address1, S.Address2, S.City, S.StateOrProvince, S.Zip,   
                S.BirthDate, S.HireDate, S.TerminationDate, S.Status,   
                S.CoverageElected, S.CoverageStartDate, S.CoverageEndDate,   
                S.LowestCostPlanOffered, S.DateEligibleForCoverage, S.EmailAddress, 0  
            )
        OUTPUT $action, inserted.id, deleted.IsDeleted,
               CAST(deleted.firstName AS NVARCHAR(400)), CAST(inserted.firstName AS NVARCHAR(400)),
               CAST(deleted.middleName AS NVARCHAR(400)), CAST(inserted.middleName AS NVARCHAR(400)),
               CAST(deleted.lastName AS NVARCHAR(400)), CAST(inserted.lastName AS NVARCHAR(400)),
               CAST(deleted.suffix AS NVARCHAR(400)), CAST(inserted.suffix AS NVARCHAR(400)),
               CAST(deleted.address AS NVARCHAR(400)), CAST(inserted.address AS NVARCHAR(400)),
               CAST(deleted.address2 AS NVARCHAR(400)), CAST(inserted.address2 AS NVARCHAR(400)),
               CAST(deleted.city AS NVARCHAR(400)), CAST(inserted.city AS NVARCHAR(400)),
               CAST(deleted.state AS NVARCHAR(400)), CAST(inserted.state AS NVARCHAR(400)),
               CAST(deleted.zip AS NVARCHAR(400)), CAST(inserted.zip AS NVARCHAR(400)),
               CONVERT(NVARCHAR(10), deleted.birthday, 23), CONVERT(NVARCHAR(10), inserted.birthday, 23),
               CONVERT(NVARCHAR(10), deleted.HireDate, 23), CONVERT(NVARCHAR(10), inserted.HireDate, 23),
               CONVERT(NVARCHAR(10), deleted.TerminationDate, 23), CONVERT(NVARCHAR(10), inserted.TerminationDate, 23),
               CAST(deleted.Status AS NVARCHAR(20)), CAST(inserted.Status AS NVARCHAR(20)),
               CAST(deleted.CoverageElected AS NVARCHAR(20)), CAST(inserted.CoverageElected AS NVARCHAR(20)),
               CONVERT(NVARCHAR(10), deleted.CoverageStartDate, 23), CONVERT(NVARCHAR(10), inserted.CoverageStartDate, 23),
               CONVERT(NVARCHAR(10), deleted.CoverageEndDate, 23), CONVERT(NVARCHAR(10), inserted.CoverageEndDate, 23),
               CAST(deleted.LowestCostPlanOffered AS NVARCHAR(400)), CAST(inserted.LowestCostPlanOffered AS NVARCHAR(400)),
               CONVERT(NVARCHAR(10), deleted.DateEligibleForCoverage, 23), CONVERT(NVARCHAR(10), inserted.DateEligibleForCoverage, 23),
               CAST(deleted.email AS NVARCHAR(400)), CAST(inserted.email AS NVARCHAR(400))
        INTO #EmpImportChanges (MergeAction, EmployeeId, WasDeleted, Old0, New0, Old1, New1, Old2, New2, Old3, New3, Old4, New4, Old5, New5, Old6, New6, Old7, New7, Old8, New8, Old9, New9, Old10, New10, Old11, New11, Old12, New12, Old13, New13, Old14, New14, Old15, New15, Old16, New16, Old17, New17, Old18, New18);

        -- 068: the employee's history: added, restored, and each field the file changed
        INSERT INTO dbo.EmployeeChangeHistory (EmployeeId, Source, FileLogId, ChangeKind, FieldName, OldValue, NewValue)
        SELECT C.EmployeeId, 'Import', @FileLogId, 'Added', NULL, NULL, NULL
        FROM   #EmpImportChanges C WHERE C.MergeAction = 'INSERT'
        UNION ALL
        SELECT C.EmployeeId, 'Import', @FileLogId, 'Restored', NULL, NULL, NULL
        FROM   #EmpImportChanges C WHERE C.MergeAction = 'UPDATE' AND C.WasDeleted = 1
        UNION ALL
        SELECT C.EmployeeId, 'Import', @FileLogId, 'Changed', V.FieldName, V.OldValue, V.NewValue
        FROM   #EmpImportChanges C
        CROSS  APPLY (VALUES
                   (N'First name', C.Old0, C.New0),
                   (N'Middle initial', C.Old1, C.New1),
                   (N'Last name', C.Old2, C.New2),
                   (N'Suffix', C.Old3, C.New3),
                   (N'Address', C.Old4, C.New4),
                   (N'Address 2', C.Old5, C.New5),
                   (N'City', C.Old6, C.New6),
                   (N'State', C.Old7, C.New7),
                   (N'ZIP', C.Old8, C.New8),
                   (N'Birth date', C.Old9, C.New9),
                   (N'Hire date', C.Old10, C.New10),
                   (N'Termination date', C.Old11, C.New11),
                   (N'Status', C.Old12, C.New12),
                   (N'Coverage elected', C.Old13, C.New13),
                   (N'Coverage start', C.Old14, C.New14),
                   (N'Coverage end', C.Old15, C.New15),
                   (N'Lowest cost plan', C.Old16, C.New16),
                   (N'Eligible for coverage', C.Old17, C.New17),
                   (N'E-mail', C.Old18, C.New18)
               ) V (FieldName, OldValue, NewValue)
        WHERE  C.MergeAction = 'UPDATE'
          AND  ISNULL(LTRIM(RTRIM(V.OldValue)), N'') <> ISNULL(LTRIM(RTRIM(V.NewValue)), N'');  

        /* 5. EMPLOYEE CODES & HIRE SPANS (FIXED MULTI-SPAN ISSUE) */      
        INSERT INTO EmployeeCode (employeeId, employerId, filingYear, getForm, isCorrected, isVoid, disableCoding)      
        SELECT E.id, E.EmployerId, @PlanYear, 1, 0, 0, 0      
        FROM Employee E WHERE E.FileLogId = @FileLogId      
        AND NOT EXISTS (SELECT 1 FROM EmployeeCode EC WHERE EC.employeeId = E.id AND EC.filingYear = @PlanYear);      
  
               INSERT INTO EmployeeHireSpan (employeeCodeId, startDate, endDate)
        SELECT DISTINCT EC.id, TRY_CAST(S.HireDate AS DATE), TRY_CAST(S.TerminationDate AS DATE)
        FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON S.EmployeeSSN = E.ssn AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1 AND S.HireDate IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM EmployeeHireSpan HS WHERE HS.employeeCodeId = EC.id AND HS.startDate = TRY_CAST(S.HireDate AS DATE));
  
        /* 5b. EMPLOYEE STATUS PERIODS (one row per distinct status span) -- FIX 1 (NEW) */
               INSERT INTO EmployeeStatus (employeeCodeId, status, startDate, endDate)
        SELECT DISTINCT EC.id, TRY_CAST(S.Status AS INT), TRY_CAST(S.StatusStartDate AS DATE), TRY_CAST(S.StatusEndDate AS DATE)
        FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON E.ssn = S.EmployeeSSN AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1
          AND TRY_CAST(S.StatusStartDate AS DATE) IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM EmployeeStatus ST WHERE ST.employeeCodeId = EC.id
                AND ST.startDate = TRY_CAST(S.StatusStartDate AS DATE)
                AND ISNULL(ST.status,-1) = ISNULL(TRY_CAST(S.Status AS INT),-1));
  
  
        /* 6. PLANS & PREMIUMS */      
        MERGE EmployerPlan AS T      
        USING (  
            SELECT DISTINCT   
                SP.PlanName,   
                E.id AS EmpId,   
                CASE WHEN LTRIM(RTRIM(SP.PlanType)) = 'Medical' THEN 1 ELSE 0 END AS PlanTypeInt,  
                TRY_CAST(SP.OfferedToSpouse AS SMALLINT) AS OfferedToSpouseInt,   
                TRY_CAST(SP.OfferedToDependents AS SMALLINT) AS OfferedToDependentsInt,  
                SP.MinimumValue,   
                SP.FundingType,   
                SP.BandingType,   
                SP.PlanRenewalMonth,   
                SP.WaitingPeriodNumberOfDays,  
                SP.EligibleFirstOfTheMonth,  
                SP.PlanTerminatesOnDateOfTermination  
            FROM Staging_Plans SP   
            JOIN Employer E ON SP.PrimaryEIN = E.taxid AND E.filingYear = @PlanYear    
            WHERE SP.FileLogId = @FileLogId AND SP.IsValid = 1  
        ) AS S      
        ON T.employerId = S.EmpId AND T.name = S.PlanName      
        WHEN MATCHED THEN   
            UPDATE SET FileLogId = @FileLogId,    
                medicalPlan = S.PlanTypeInt,  
                offeredSpouse = S.OfferedToSpouseInt,   
                offeredDependents = S.OfferedToDependentsInt,  
                minimumValue = S.MinimumValue,   
                fundingType = S.FundingType,   
                bandingType = S.BandingType,   
                planRenewal = S.PlanRenewalMonth,   
                waitingDays = S.WaitingPeriodNumberOfDays,  
                eligibile1stOfMonth = S.EligibleFirstOfTheMonth,  
                planTermTermination = S.PlanTerminatesOnDateOfTermination  
        WHEN NOT MATCHED THEN   
            INSERT (  
                FileLogId, employerId, name, medicalPlan,   
                offeredSpouse, offeredDependents,   
                minimumValue, fundingType, bandingType, planRenewal, waitingDays,   
                eligibile1stOfMonth, planTermTermination,   
                code1A, code2F, code2G, code2H   
            )   
            VALUES (  
                @FileLogId, S.EmpId, S.PlanName, S.PlanTypeInt,  
                S.OfferedToSpouseInt, S.OfferedToDependentsInt,  
                S.MinimumValue, S.FundingType, S.BandingType, S.PlanRenewalMonth, S.WaitingPeriodNumberOfDays,   
                S.EligibleFirstOfTheMonth, S.PlanTerminatesOnDateOfTermination,   
                0, 1, 1, 1  
            );  
  
        /* 7. PREMIUMS */  
        INSERT INTO Premium (EmployerPlanId, bandingValueStart, bandingValueEnd, bandingStartDate, bandingEndDate, amount, EmployerId, FileLogId)      
        SELECT   
            EP.id,   
            ISNULL(NULLIF(LTRIM(RTRIM(SP.[Start])), ''), SP.BandingType),   
            ISNULL(NULLIF(LTRIM(RTRIM(SP.[End])),   ''), SP.BandingType),   
            SP.StartDate,   
            SP.EndDate,   
            SP.EEMonthlyContribution,   
            EP.employerId,   
            @FileLogId   
        FROM Staging_Premiums SP   
        JOIN EmployerPlan EP ON   
            REPLACE(LTRIM(RTRIM(EP.name)), CHAR(160), '') = REPLACE(LTRIM(RTRIM(SP.PlanName)), CHAR(160), '')  
            AND EP.FileLogId = @FileLogId   
        WHERE SP.FileLogId = @FileLogId   
            AND SP.IsValid = 1   
            AND NOT EXISTS (  
                SELECT 1 FROM Premium P   
                WHERE P.EmployerPlanId = EP.id   
                AND P.bandingStartDate = SP.StartDate   
                AND P.amount = SP.EEMonthlyContribution  
            );  
  
        /* 8. ENROLLMENT & PAYROLL & DEPENDENTS */      
        INSERT INTO EmployeeEnrollment
            (employeeCodeId, planId, isEnrolled, coverageOfferDate, coverageStartDate, coverageEndDate,
             unionMember, contributionStartDate, contributionEndDate,
             COBRAEnrolled, COBRAStartDate, COBRAEndDate, RetireeEnrolled, RetireeStartDate, RetireeEndDate)   -- FIX 2
        SELECT DISTINCT
            EC.id, EP.id, ISNULL(TRY_CAST(S.CoverageElected AS SMALLINT),0),
            TRY_CAST(S.DateEligibleForCoverage AS DATE), TRY_CAST(S.CoverageStartDate AS DATE), TRY_CAST(S.CoverageEndDate AS DATE),
            ISNULL(TRY_CAST(S.UnionEmployee AS SMALLINT),0), TRY_CAST(S.DateEmployerFirstPaysToUnionBenefits AS DATE), TRY_CAST(S.DateEmployerLastPaysToUnionBenefits AS DATE),
            ISNULL(TRY_CAST(S.COBRABenefitsElected AS SMALLINT),0), TRY_CAST(S.COBRACoverageEffectiveDate AS DATE), TRY_CAST(S.COBRACoverageEndDate AS DATE),
            ISNULL(TRY_CAST(S.RetireeBenefitsElected AS SMALLINT),0), TRY_CAST(S.RetireePlanStartDate AS DATE), TRY_CAST(S.RetireePlanEndDate AS DATE)
                FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON E.ssn = S.EmployeeSSN AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        LEFT JOIN EmployerPlan EP ON REPLACE(LTRIM(RTRIM(EP.name)),CHAR(160),'') = REPLACE(LTRIM(RTRIM(S.LowestCostPlanOffered)),CHAR(160),'') AND EP.employerId IN (SELECT g.id FROM Employer g WHERE g.id = EC.employerId OR g.id = (SELECT p2.companyId FROM Employer p2 WHERE p2.id = EC.employerId))
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1
            AND ( S.LowestCostPlanOffered IS NOT NULL
                 OR ISNULL(TRY_CAST(S.UnionEmployee AS SMALLINT),0)=1
                 OR ISNULL(TRY_CAST(S.COBRABenefitsElected AS SMALLINT),0)=1
                 OR ISNULL(TRY_CAST(S.RetireeBenefitsElected AS SMALLINT),0)=1 )
            AND NOT EXISTS (SELECT 1 FROM EmployeeEnrollment EE WHERE EE.employeeCodeId = EC.id
                    AND ISNULL(EE.planId,-1) = ISNULL(EP.id,-1)
                    AND ISNULL(EE.coverageStartDate,'1900-01-01') = ISNULL(TRY_CAST(S.CoverageStartDate AS DATE),'1900-01-01')
                    AND ISNULL(EE.coverageOfferDate,'1900-01-01')  = ISNULL(TRY_CAST(S.DateEligibleForCoverage AS DATE),'1900-01-01'));      
  
        INSERT INTO EmployeePayroll
            (employeeCodeId, payPeriodStartDate, payPeriodEndDate, payPeriodTotalHours, payPeriodSalaryAmount, payPeriodAdditional)   -- FIX 3
        SELECT
            EC.id, ISNULL(TRY_CAST(S.StatusStartDate AS DATE), E.HireDate), ISNULL(TRY_CAST(S.StatusEndDate AS DATE), '2099-12-31'),
            TRY_CAST(S.HourlyRate AS DECIMAL(18,2)),                 -- convention: hourly RATE stored here
            TRY_CAST(S.W2IncomeOrAnnualSalary AS DECIMAL(18,2)),     -- convention: W-2 / annual salary
            TRY_CAST(S.AdditionalIncome AS DECIMAL(18,2))            -- NEW: additional income (was dropped)
                FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON E.ssn = S.EmployeeSSN AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId
            AND (S.W2IncomeOrAnnualSalary IS NOT NULL OR S.HourlyRate IS NOT NULL OR S.AdditionalIncome IS NOT NULL)
            AND NOT EXISTS (SELECT 1 FROM EmployeePayroll PR WHERE PR.employeeCodeId = EC.id AND PR.payPeriodStartDate = ISNULL(TRY_CAST(S.StatusStartDate AS DATE), E.HireDate));      
  
        /* F-07: carry the dependent's own coverage dates, relationship, middle
           name and suffix across. Staging_Dependents supplies all five and the
           import previously took none of them, so every dependent inherited the
           employee's coverage span via the fallback in ACALogicService.
           CalculateMonths -- correct only while the two spans happen to agree. */
        INSERT INTO CoveredIndividual (employeeCodeId, firstName, middleName, lastName, suffix,
                                       ssn, birthday, coverageStartDate, coverageEndDate,
                                       Relationship, EmployerId)
        SELECT
            EC.id,
            S.DependentLegalFirstName,
            NULLIF(LTRIM(RTRIM(S.DependentMiddleInitial)), ''),
            S.DependentLegalLastName,
            NULLIF(LTRIM(RTRIM(S.DependentSuffix)), ''),
            S.DependentSSN,
            TRY_CAST(S.DependentBirthdate AS DATE),
            TRY_CAST(S.DependentCoverageStartDate AS DATE),
            TRY_CAST(S.DependentCoverageEndDate   AS DATE),
            NULLIF(LTRIM(RTRIM(S.Relationship)), ''),
            EC.employerId
                FROM Staging_Dependents S
        JOIN Employee E ON S.EmployeeSSN = E.ssn
             AND (E.EmployerId = @ParentEmployerId
                  OR E.EmployerId IN (SELECT id FROM Employer WHERE companyId = @ParentEmployerId))
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1
            /* The guard used to be CI.ssn = S.DependentSSN alone. A dependent with
               no SSN -- routine for young children -- compares NULL to NULL, which
               is never true, so NOT EXISTS always passed and the row was inserted
               again on every re-import. Fall back to name plus date of birth when
               there is no SSN to match on. */
            AND NOT EXISTS (
                    SELECT 1
                    FROM   CoveredIndividual CI
                    WHERE  CI.employeeCodeId = EC.id
                      AND  (
                             (   NULLIF(LTRIM(RTRIM(S.DependentSSN)), '') IS NOT NULL
                             AND CI.ssn = S.DependentSSN )
                          OR (   NULLIF(LTRIM(RTRIM(S.DependentSSN)), '') IS NULL
                             AND ISNULL(LTRIM(RTRIM(CI.ssn)), '') = ''
                             AND ISNULL(LTRIM(RTRIM(CI.firstName)), '') = ISNULL(LTRIM(RTRIM(S.DependentLegalFirstName)), '')
                             AND ISNULL(LTRIM(RTRIM(CI.lastName)),  '') = ISNULL(LTRIM(RTRIM(S.DependentLegalLastName)),  '')
                             AND ISNULL(CI.birthday, '1900-01-01') = ISNULL(TRY_CAST(S.DependentBirthdate AS DATE), '1900-01-01') )
                           )
                );
  
        /* 9. FINALIZE & SUMMARY */      
        DELETE FROM ImportValidationSummary WHERE FileLogId = @FileLogId;  
        INSERT INTO ImportValidationSummary (FileLogId, TotalEmployees, EmployeesWithHireDate, EmployeesWithStatus, PayrollRows, EnrollmentRows, PlanCount, IsImportComplete, ValidationMessage, DependentRows, TotalPayrollImported)      
        SELECT @FileLogId, (SELECT COUNT(*) FROM Employee WHERE FileLogId = @FileLogId), (SELECT COUNT(*) FROM Employee WHERE FileLogId = @FileLogId AND HireDate IS NOT NULL), (SELECT COUNT(*) FROM Employee WHERE FileLogId = @FileLogId AND Status IS NOT 
        NULL), (SELECT COUNT(*) FROM EmployeePayroll EP JOIN EmployeeCode EC ON EP.employeeCodeId = EC.id JOIN Employee EMP ON EC.employeeId = EMP.id WHERE EMP.FileLogId = @FileLogId), (SELECT COUNT(*) FROM EmployeeEnrollment EE JOIN EmployeeCode EC ON EE.employeeCodeId = EC.id JOIN Employee EMP ON EC.employeeId = EMP.id WHERE EMP.FileLogId = @FileLogId), (SELECT COUNT(*) FROM EmployerPlan WHERE FileLogId = @FileLogId), 1, 'Import complete', (SELECT COUNT(*) FROM CoveredIndividual CI JOIN EmployeeCode EC ON CI.employeeCodeId = EC.id JOIN Employee E ON EC.employeeId = E.id WHERE E.FileLogId = @FileLogId), (SELECT ISNULL(SUM(payPeriodSalaryAmount), 0) FROM EmployeePayroll EP JOIN EmployeeCode EC ON EP.employeeCodeId = EC.id JOIN Employee EMP ON EC.employeeId = EMP.id WHERE EMP.FileLogId = @FileLogId) FROM UploadedFileLog WHERE FileLogId = @FileLogId;      
  
        UPDATE UploadedFileLog SET WorkflowStatusId = 40, ValidationStatusId = 5, ValidationStatus = 'Imported', ProcessingEndTime = SYSUTCDATETIME() WHERE FileLogId = @FileLogId;      
  
        IF @TranStarted = 1  
            COMMIT TRANSACTION;  
    END TRY  
    BEGIN CATCH  
        IF @@TRANCOUNT > 0   
            ROLLBACK TRANSACTION;  
  
        THROW;  
    END CATCH  
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_EmployeeChangeHistory
    @EmployeeId BIGINT,
    @Top        INT = 500
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Rec VARCHAR(50) = CAST(@EmployeeId AS VARCHAR(50));

    ;WITH Imports AS (
        SELECT H.ChangedAt, 'Import' AS Source,
               CASE H.ChangeKind WHEN 'Added' THEN N'Added by an import' WHEN 'Restored' THEN N'Restored by an import'
                                 ELSE N'Changed by an import' END AS Summary,
               COALESCE(NULLIF(U1.User_Name, ''), NULLIF(U2.User_Name, '')) AS ChangedBy,
               H.FileLogId, F.OriginalFileName AS FileName,
               H.FieldName, H.OldValue, H.NewValue,
               'I' + CAST(H.FileLogId AS VARCHAR(20)) + '-' + CONVERT(VARCHAR(19), H.ChangedAt, 126) AS GroupKey
        FROM   dbo.EmployeeChangeHistory H
        LEFT   JOIN dbo.UploadedFileLog F ON F.FileLogId = H.FileLogId
        LEFT   JOIN dbo.tbl_User U1 ON CAST(U1.User_ID AS VARCHAR(20)) = F.AssignedToUserId
        LEFT   JOIN dbo.tbl_User U2 ON CAST(U2.User_ID AS VARCHAR(20)) = F.UploadedByUserId
        WHERE  H.EmployeeId = @EmployeeId
    ), Edits AS (
        -- Hand edits: field deltas for single saves, one line for a grid save.
        SELECT A.Created_at AS ChangedAt, 'Edit' AS Source,
               CASE WHEN A.Action_Type = 'INSERT' THEN N'Added by hand'
                    WHEN A.Action_Type = 'BULK_UPSERT' THEN N'Saved ' + REPLACE(REPLACE(A.Table_Name, 'Employee', ''), 'Details', '') + N' details'
                    ELSE N'Edited by hand' END AS Summary,
               A.User_Name AS ChangedBy, CAST(NULL AS INT) AS FileLogId, CAST(NULL AS NVARCHAR(260)) AS FileName,
               CAST(N.[key] AS NVARCHAR(64)) COLLATE DATABASE_DEFAULT AS FieldName,
               CASE WHEN N.[key] LIKE '%SSN%' AND LEN(O.value) >= 4 THEN N'XXX-XX-' + RIGHT(O.value, 4) ELSE CAST(O.value AS NVARCHAR(400)) END COLLATE DATABASE_DEFAULT AS OldValue,
               CASE WHEN N.[key] LIKE '%SSN%' AND LEN(N.value) >= 4 THEN N'XXX-XX-' + RIGHT(N.value, 4) ELSE CAST(N.value AS NVARCHAR(400)) END COLLATE DATABASE_DEFAULT AS NewValue,
               'E' + CAST(A.Id AS VARCHAR(40)) AS GroupKey
        FROM   dbo.DataAuditlog A
        OUTER  APPLY (SELECT [key], value FROM OPENJSON(CASE WHEN A.Action_Type = 'UPDATE' AND ISJSON(A.New_Values) = 1
                                                             AND LEFT(LTRIM(A.New_Values), 1) = '{' THEN A.New_Values END)) N
        OUTER  APPLY (SELECT TOP 1 value FROM OPENJSON(CASE WHEN ISJSON(A.Old_Values) = 1 AND LEFT(LTRIM(A.Old_Values), 1) = '{'
                                                          THEN A.Old_Values END) X WHERE X.[key] = N.[key]) O
        WHERE  A.Record_Id = @Rec AND A.Table_Name LIKE 'Employee%'
    ), Codes AS (
        SELECT T.ChangedOn AS ChangedAt, 'Codes' AS Source,
               CASE WHEN T.FieldName = 'IsLocked' THEN CASE WHEN T.NewValue = '1' THEN N'Codes locked' ELSE N'Codes unlocked' END
                    WHEN T.IsSystemGenerated = 1 THEN N'Codes recalculated' ELSE N'Code changed by hand' END AS Summary,
               COALESCE(T.ChangedByUserName, CASE WHEN T.IsSystemGenerated = 1 THEN 'System' END) AS ChangedBy,
               CAST(NULL AS INT) AS FileLogId, CAST(NULL AS NVARCHAR(260)) AS FileName,
               CASE WHEN T.LineNumber > 0 THEN CAST(CONCAT('Line ', T.LineNumber, ' ', CASE WHEN T.AppliesToAllMonths = 1 THEN 'All months' ELSE T.MonthCode END) AS NVARCHAR(64)) END AS FieldName,
               CASE WHEN T.LineNumber > 0 THEN CAST(T.OldValue AS NVARCHAR(400)) END AS OldValue,
               CASE WHEN T.LineNumber > 0 THEN CAST(T.NewValue AS NVARCHAR(400)) END AS NewValue,
               'C' + CONVERT(VARCHAR(16), T.ChangedOn, 126) + CAST(T.IsSystemGenerated AS CHAR(1)) AS GroupKey
        FROM   dbo.EmployeeCodeAuditTrail T
        WHERE  T.EmployeeId = @EmployeeId
          AND  ISNULL(T.OldValue, '') <> ISNULL(T.NewValue, '')
          AND  NOT (TRY_CAST(T.OldValue AS DECIMAL(18,4)) IS NOT NULL AND TRY_CAST(T.OldValue AS DECIMAL(18,4)) = TRY_CAST(T.NewValue AS DECIMAL(18,4)))
    ), AllRows AS (
        SELECT * FROM Imports UNION ALL SELECT * FROM Edits UNION ALL SELECT * FROM Codes
    )
    SELECT TOP (@Top) ChangedAt, Source, Summary, ChangedBy, FileLogId, FileName, FieldName, OldValue, NewValue, GroupKey
    FROM   AllRows
    WHERE  NOT (Source = 'Edit' AND FieldName IN ('EmployeeID', 'EmployerId', 'FilingYear', 'RowId', 'EmployeeCodeId'))
    ORDER  BY ChangedAt DESC, GroupKey, FieldName;
END
GO

PRINT '068: employee change history (imports recorded; sp_EmployeeChangeHistory).';
GO
GO

/* ############################################################################
   069_employer_timeline.sql
   ############################################################################ */
PRINT '=== 069_employer_timeline.sql';
GO
/* =============================================================================
   069 - Employer timeline (idea #18)

   sp_EmployerTimeline: one list, newest first, of what happened to an
   employer for its tax year: files (uploaded, validated, assigned, imported,
   rejected...), code recalculations, forms generated, filing locked or
   unlocked, notes, edits to the employer, account manager and data analyst
   assignments, and the filing deadlines still ahead (state ones only where the employer
   has employees living in that state).

   Reads only. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.EmployeeCodeAuditTrail') AND name = 'IX_EmployeeCodeAuditTrail_Employer')
    CREATE INDEX IX_EmployeeCodeAuditTrail_Employer ON dbo.EmployeeCodeAuditTrail (EmployerId, FilingYear, LineNumber)
        INCLUDE (ChangedOn, EmployeeId, ChangedByUserName, IsSystemGenerated);
GO

CREATE OR ALTER PROCEDURE dbo.sp_EmployerTimeline
    @EmployerId BIGINT,
    @Top        INT = 300
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Year INT, @Ein VARCHAR(20), @Rec VARCHAR(50) = CAST(@EmployerId AS VARCHAR(50));
    -- File times are kept in UTC; everything else here is server local time.
    DECLARE @UtcOffset INT = DATEDIFF(MINUTE, GETUTCDATE(), GETDATE());
    SELECT @Year = filingYear, @Ein = REPLACE(ISNULL(taxid, ''), '-', '') FROM dbo.Employer WHERE id = @EmployerId;
    IF @Year IS NULL RETURN;

    DECLARE @Files TABLE (FileLogId INT PRIMARY KEY, FileName NVARCHAR(260), UploadedAt DATETIME2, UploadedBy VARCHAR(50));
    INSERT INTO @Files
    SELECT L.FileLogId, L.OriginalFileName, L.UploadedAt, L.UploadedByUserId
    FROM   dbo.UploadedFileLog L
    WHERE  REPLACE(ISNULL(L.TaxID, ''), '-', '') = @Ein AND L.PlanYear = @Year AND ISNULL(L.IsDeleted, 0) = 0;

    ;WITH Events AS (
        -- Files: the upload, then each step that means something to a person
        SELECT DATEADD(MINUTE, @UtcOffset, F.UploadedAt) AS EventAt, 'File' AS Kind, N'File uploaded' AS Title,
               F.FileName AS Detail, U.User_Name AS Who, F.FileLogId
        FROM   @Files F
        LEFT   JOIN dbo.tbl_User U ON CAST(U.User_ID AS VARCHAR(50)) = F.UploadedBy
        UNION ALL
        SELECT DATEADD(MINUTE, @UtcOffset, H.Timestamp), 'File', CAST(H.ActionName AS NVARCHAR(200)),
               F.FileName + CASE WHEN NULLIF(H.Message, '') IS NOT NULL THEN N' - ' + H.Message ELSE N'' END,
               H.PerformedByUserName, F.FileLogId
        FROM   dbo.FileLifecycleHistory H
        JOIN   @Files F ON F.FileLogId = H.FileLogId
        WHERE  H.ActionName NOT IN ('Pre-Processing Started', 'Structure Check Started', 'Processing Started',
                                    'Import Started', 'Import Queued')
        UNION ALL
        -- Codes: one line per recalculation (each employee gets a marker row)
        SELECT MIN(T.ChangedOn), 'Codes',
               CASE WHEN MAX(CAST(T.IsSystemGenerated AS INT)) = 1 THEN N'Codes recalculated' ELSE N'Codes changed by hand' END,
               CAST(COUNT(DISTINCT T.EmployeeId) AS NVARCHAR(20)) + N' employee' + CASE WHEN COUNT(DISTINCT T.EmployeeId) = 1 THEN N'' ELSE N's' END,
               MAX(T.ChangedByUserName), NULL
        FROM   dbo.EmployeeCodeAuditTrail T
        WHERE  T.EmployerId = @EmployerId AND T.FilingYear = @Year AND T.LineNumber = 0 AND T.FieldName = 'All Codes'
        GROUP  BY CONVERT(VARCHAR(16), T.ChangedOn, 126)
        UNION ALL
        -- Codes locked or unlocked (script 074), one line per action
        SELECT MIN(T.ChangedOn), 'Codes',
               CASE WHEN MAX(T.NewValue) = '1' THEN N'Codes locked' ELSE N'Codes unlocked' END,
               CAST(COUNT(DISTINCT T.EmployeeId) AS NVARCHAR(20)) + N' employee' + CASE WHEN COUNT(DISTINCT T.EmployeeId) = 1 THEN N'' ELSE N's' END,
               MAX(T.ChangedByUserName), NULL
        FROM   dbo.EmployeeCodeAuditTrail T
        WHERE  T.EmployerId = @EmployerId AND T.FilingYear = @Year AND T.FieldName = 'IsLocked'
        GROUP  BY CONVERT(VARCHAR(16), T.ChangedOn, 126), T.NewValue
        UNION ALL
        SELECT B.GeneratedDate, 'Forms', N'Forms generated (' + ISNULL(B.FormType, '') + N')',
               B.FileName, COALESCE(GU.User_Name, B.GeneratedBy), NULL
        FROM   dbo.GenerateMultiEmployerBatchHistoryEmployers BE
        JOIN   dbo.GenerateMultiEmployerBatchHistory B ON B.Id = BE.BatchHistoryId
        LEFT   JOIN dbo.tbl_User GU ON CAST(GU.User_ID AS VARCHAR(50)) = B.GeneratedBy
        WHERE  BE.EmployerId = @EmployerId AND BE.TaxYear = @Year
        UNION ALL
        SELECT A.EventDate, 'Filing', N'Filing ' + LOWER(A.EventType), A.EventDescription,
               COALESCE(A.PerformedByUserName, U.User_Name), NULL
        FROM   dbo.FilingAuditLog A
        LEFT   JOIN dbo.tbl_User U ON CAST(U.User_ID AS VARCHAR(50)) = A.PerformedByUserId
        WHERE  A.EmployerId = @EmployerId AND A.TaxYear = @Year
        UNION ALL
        SELECT N.CreatedOn, 'Note', CASE WHEN N.IsInternal = 1 THEN N'Internal note' ELSE N'Note' END,
               LEFT(N.NoteMessage, 300), U.User_Name, NULL
        FROM   dbo.tbl_SystemNotes N
        LEFT   JOIN dbo.tbl_User U ON U.User_ID = N.UserID
        WHERE  N.LinkedEntityType = 'Employer' AND N.LinkedEntityID = @EmployerId AND ISNULL(N.IsDeleted, 0) = 0
        UNION ALL
        SELECT D.Created_at, 'Edit',
               CASE WHEN D.Action_Type = 'INSERT' THEN N'Employer added' ELSE N'Employer details changed' END,
               CASE WHEN D.Table_Name = 'Tracker_EmployerExtension' THEN N'Service details' ELSE N'Employer details' END,
               D.User_Name, NULL
        FROM   dbo.DataAuditlog D
        WHERE  D.Record_Id = @Rec AND D.Table_Name IN ('Employer', 'Tracker_EmployerExtension')
        UNION ALL
        SELECT A.StartDate, 'Assignment', N'Account manager assigned', U.User_Name, NULL, NULL
        FROM   dbo.tbl_AM_Assignment_History A
        LEFT   JOIN dbo.tbl_User U ON U.User_ID = A.AM_UserID
        WHERE  A.EmployerID = @EmployerId
        UNION ALL
        SELECT A.AssignedDate, 'Assignment', N'Data analyst assigned', U.User_Name, NULL, NULL
        FROM   dbo.tbl_DA_Assignment_History A
        LEFT   JOIN dbo.tbl_User U ON U.User_ID = A.DA_UserID
        WHERE  A.EmployerID = @EmployerId
        UNION ALL
        SELECT CAST(D.DueDate AS DATETIME2), 'Deadline', CAST(D.Title AS NVARCHAR(200)),
               D.Jurisdiction + CASE WHEN D.NeedsCheck = 1 THEN N' (date to be confirmed)' ELSE N'' END, NULL, NULL
        FROM   dbo.FilingDeadlines D
        WHERE  D.FilingYear = @Year AND D.DueDate >= CAST(GETDATE() AS DATE)
          AND  ISNULL(D.IsActive, 1) = 1
          -- a state date only matters when the employer has employees living there
          AND (D.Jurisdiction = 'IRS' OR EXISTS (SELECT 1 FROM dbo.Employee E WHERE E.EmployerId = @EmployerId
                                                  AND ISNULL(E.IsDeleted, 0) = 0 AND UPPER(LTRIM(RTRIM(E.state))) = D.Jurisdiction))
    )
    SELECT TOP (@Top) EventAt, Kind, Title, Detail, Who, FileLogId,
           CAST(CASE WHEN EventAt > SYSDATETIME() THEN 1 ELSE 0 END AS BIT) AS IsUpcoming
    FROM   Events
    WHERE  EventAt IS NOT NULL
    ORDER  BY EventAt DESC;
END
GO

PRINT '069: employer timeline (sp_EmployerTimeline).';
GO
GO

/* ############################################################################
   070_notifications.sql
   ############################################################################ */
PRINT '=== 070_notifications.sql';
GO
/* =============================================================================
   070 - Notification centre (idea #21)

   In-app notifications only (no e-mail). Each user chooses which events
   they get, on the Notifications page.

   1. tbl_UserNotifications.EventKey: lets a reminder be sent once only.
   2. NotificationPreferences: a user's choice per event (no row = on).
   3. sp_Notify_FileEvent: the uploader and the assigned analyst hear when
      their file is ready to import, failed, or was imported; the analyst
      hears when a file is assigned to them. Never the person who did it.
   4. sp_UpdateFileLogStatus calls it after writing the file's history.
      Everything else is the database's current procedure, unchanged. A
      notification that fails never stops the status change.
   5. sp_Notify_Daily (a daily background job): filing deadlines 14 and 7
      days ahead for staff; new errors on System Health for admins.
   6. sp_Notifications_Recent, the preference procedures, and
      sp_MarkNotificationAsRead / sp_MarkNotificationAsUnread taking the
      user, so nobody can mark someone else's notification.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.tbl_UserNotifications', 'EventKey') IS NULL
    ALTER TABLE dbo.tbl_UserNotifications ADD EventKey VARCHAR(120) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.tbl_UserNotifications') AND name = 'IX_UserNotifications_User')
    CREATE INDEX IX_UserNotifications_User ON dbo.tbl_UserNotifications (UserId, IsRead, CreatedDate DESC);
GO
-- Not a unique filtered index: those need QUOTED_IDENTIFIER ON in every procedure that writes
-- here, and older ones were created without it. The reminder procedure checks before it inserts.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.tbl_UserNotifications') AND name = 'IX_UserNotifications_EventKey')
    CREATE INDEX IX_UserNotifications_EventKey ON dbo.tbl_UserNotifications (UserId, EventKey);
GO

IF OBJECT_ID('dbo.NotificationPreferences') IS NULL
    CREATE TABLE dbo.NotificationPreferences (
        UserId     INT          NOT NULL,
        EventCode  VARCHAR(40)  NOT NULL,   -- FileReady, FileFailed, FileAssigned, ImportDone, Deadline, HealthErrors
        IsEnabled  BIT          NOT NULL,
        ModifiedAt DATETIME2(0) NOT NULL CONSTRAINT DF_NotificationPreferences_ModifiedAt DEFAULT SYSDATETIME(),
        CONSTRAINT PK_NotificationPreferences PRIMARY KEY (UserId, EventCode)
    );
GO

/* Who may get an event: an active user, of a role the event is for, who has not turned it off. */
CREATE OR ALTER FUNCTION dbo.fn_NotificationWanted (@UserId INT, @EventCode VARCHAR(40))
RETURNS TABLE
AS
RETURN
    SELECT CAST(1 AS BIT) AS Wanted
    FROM   dbo.tbl_User U
    JOIN   dbo.tbl_Roles R ON R.ID = U.Role_ID
    WHERE  U.User_ID = @UserId
      AND  ISNULL(U.Flag, 1) = 1
      AND  (@EventCode <> 'HealthErrors' OR R.RoleName IN ('SuperAdmin', 'Admin'))
      AND  (@EventCode <> 'Deadline' OR R.RoleName NOT IN ('Employer', 'Broker'))
      AND  NOT EXISTS (SELECT 1 FROM dbo.NotificationPreferences P
                       WHERE P.UserId = @UserId AND P.EventCode = @EventCode AND P.IsEnabled = 0);
GO

CREATE OR ALTER PROCEDURE dbo.sp_Notify_FileEvent
    @FileLogId   INT,
    @Action      NVARCHAR(100),
    @PerformedBy NVARCHAR(450) = NULL,
    @Message     NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Event VARCHAR(40) =
        CASE WHEN @Action IN ('Validation Passed', 'Validation Completed with Warnings') THEN 'FileReady'
             WHEN @Action IN ('Validation Failed', 'System Error', 'DA Rejected', 'Import Abandoned') THEN 'FileFailed'
             WHEN @Action = 'File Assigned' THEN 'FileAssigned'
             WHEN @Action = 'Import Complete' THEN 'ImportDone' END;
    IF @Event IS NULL RETURN;

    DECLARE @File NVARCHAR(260), @Employer NVARCHAR(255), @Uploader VARCHAR(50), @Assignee VARCHAR(50);
    SELECT @File = OriginalFileName, @Employer = EmployerName,
           @Uploader = NULLIF(UploadedByUserId, ''), @Assignee = NULLIF(AssignedToUserId, '')
    FROM   dbo.UploadedFileLog WHERE FileLogId = @FileLogId;
    IF @File IS NULL RETURN;

    DECLARE @Title VARCHAR(255) =
        CASE @Event WHEN 'FileReady'    THEN CASE WHEN @Action = 'Validation Passed' THEN 'File ready to import' ELSE 'File ready, with warnings' END
                    WHEN 'FileFailed'   THEN CASE WHEN @Action = 'DA Rejected' THEN 'File rejected'
                                                  WHEN @Action = 'Import Abandoned' THEN 'Import did not finish'
                                                  ELSE 'File needs attention' END
                    WHEN 'FileAssigned' THEN 'File assigned to you'
                    ELSE 'Import complete' END;
    DECLARE @Text NVARCHAR(MAX) = @File + ISNULL(N' (' + @Employer + N')', N'')
                                + CASE WHEN NULLIF(@Message, N'') IS NOT NULL AND @Event <> 'FileAssigned' THEN N': ' + @Message ELSE N'.' END;
    DECLARE @Type VARCHAR(50) = CASE @Event WHEN 'FileFailed' THEN 'danger'
                                            WHEN 'FileReady' THEN CASE WHEN @Action = 'Validation Passed' THEN 'success' ELSE 'warning' END
                                            WHEN 'ImportDone' THEN 'success' ELSE 'info' END;
    DECLARE @Link VARCHAR(255) = CASE WHEN @Event IN ('FileReady', 'FileFailed') AND @Action <> 'Import Abandoned'
                                      THEN '/ErrorTriage/ErrorTriageDashboard/' + CAST(@FileLogId AS VARCHAR(20))
                                      ELSE '/FileUpload?searchTerm=' + LEFT(REPLACE(REPLACE(REPLACE(@File, '%', '%25'), '&', '%26'), ' ', '%20'), 200) END;

    INSERT INTO dbo.tbl_UserNotifications (UserId, Title, Message, Type, LinkUrl, CreatedDate, IsRead)
    SELECT DISTINCT TRY_CAST(R.UserId AS INT), @Title, @Text, @Type, @Link, GETUTCDATE(), 0
    FROM   (VALUES (CASE WHEN @Event <> 'FileAssigned' THEN @Uploader END), (@Assignee)) R (UserId)
    CROSS  APPLY dbo.fn_NotificationWanted(TRY_CAST(R.UserId AS INT), @Event) W
    WHERE  TRY_CAST(R.UserId AS INT) > 0
      AND  ISNULL(R.UserId, '') <> ISNULL(@PerformedBy, '');
END
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_UpdateFileLogStatus]  
    @FileLogId INT,  
    @ValidationStatusId TINYINT = NULL,  
    @WorkflowStatusId INT = NULL,
    @UserId NVARCHAR(450) = NULL,      -- NEW: Who did it?
    @Action NVARCHAR(100) = 'System Update', -- NEW: What did they do?
    @Message NVARCHAR(500) = NULL      -- NEW: Why? (e.g. "Found 20 errors")
AS  
BEGIN  
    SET NOCOUNT ON;  
    
    BEGIN TRANSACTION;

    BEGIN TRY
        -- 1. CAPTURE OLD STATUS (For History)
        DECLARE @OldStatus INT;
        SELECT @OldStatus = WorkflowStatusId FROM dbo.UploadedFileLog WHERE FileLogId = @FileLogId;

        -- 2. UPDATE THE FILE (Current State)
        UPDATE dbo.UploadedFileLog  
        SET  
            ValidationStatusId = COALESCE(@ValidationStatusId, ValidationStatusId),  
            WorkflowStatusId = COALESCE(@WorkflowStatusId, WorkflowStatusId),  
            AnalysisDate = CASE  
                                WHEN @ValidationStatusId IS NOT NULL THEN GETUTCDATE()  
                                ELSE AnalysisDate  
                           END,
            ModifiedOn = GETUTCDATE()
        WHERE  
            FileLogId = @FileLogId;  

        -- 3. INSERT HISTORY (Flight Tracker)
        INSERT INTO dbo.FileLifecycleHistory
        (
            FileLogId, 
            PreviousStatusId, 
            NewStatusId, 
            ActionName, 
            PerformedByUserId, 
            PerformedByUserName, 
            Timestamp, 
            Message
        )
        SELECT 
            @FileLogId,
            @OldStatus,
            COALESCE(@WorkflowStatusId, @OldStatus), -- New Status
            @Action,
            @UserId,
            u.User_Name, -- Fetch UserName automatically if UserID exists
            GETUTCDATE(),
            @Message
        FROM (SELECT 1 AS Dummy) d
        LEFT JOIN tbl_User u ON u.User_ID = @UserId; -- Join to Identity table if exists

        -- 070: tell the uploader / assigned analyst. A notification never stops the status change.
        BEGIN TRY
            EXEC dbo.sp_Notify_FileEvent @FileLogId = @FileLogId, @Action = @Action, @PerformedBy = @UserId, @Message = @Message;
        END TRY
        BEGIN CATCH
            IF XACT_STATE() = -1 THROW;
        END CATCH

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_Notify_Daily
    @Today DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Today = ISNULL(@Today, CAST(GETDATE() AS DATE));

    /* Filing deadlines: once when 14 days or fewer are left, once more at 7. A missed day still
       sends the reminder late rather than never; the EventKey keeps each one to a single send. */
    ;WITH Due AS (
        SELECT D.FilingDeadlineId, D.Title, D.Jurisdiction, D.DueDate, D.NeedsCheck,
               DATEDIFF(DAY, @Today, D.DueDate) AS DaysLeft,
               CASE WHEN DATEDIFF(DAY, @Today, D.DueDate) <= 7 THEN 7 ELSE 14 END AS Stage
        FROM   dbo.FilingDeadlines D
        WHERE  ISNULL(D.IsActive, 1) = 1
          AND  DATEDIFF(DAY, @Today, D.DueDate) BETWEEN 0 AND 14
    )
    INSERT INTO dbo.tbl_UserNotifications (UserId, Title, Message, Type, LinkUrl, CreatedDate, IsRead, EventKey)
    SELECT U.User_ID,
           CASE WHEN D.DaysLeft = 0 THEN 'Deadline today' ELSE 'Deadline in ' + CAST(D.DaysLeft AS VARCHAR(5)) + ' day' + CASE WHEN D.DaysLeft = 1 THEN '' ELSE 's' END END,
           D.Title + N' (' + D.Jurisdiction + N') is due ' + FORMAT(D.DueDate, 'MMMM d, yyyy')
               + CASE WHEN D.NeedsCheck = 1 THEN N'. The date is still to be confirmed.' ELSE N'.' END,
           CASE WHEN D.Stage = 7 THEN 'danger' ELSE 'warning' END,
           '/Calendar', GETUTCDATE(), 0,
           'Deadline:' + CAST(D.FilingDeadlineId AS VARCHAR(20)) + ':' + CAST(D.Stage AS VARCHAR(5))
    FROM   Due D
    CROSS  JOIN dbo.tbl_User U
    CROSS  APPLY dbo.fn_NotificationWanted(U.User_ID, 'Deadline') W
    WHERE  NOT EXISTS (SELECT 1 FROM dbo.tbl_UserNotifications N
                       WHERE N.UserId = U.User_ID
                         AND N.EventKey = 'Deadline:' + CAST(D.FilingDeadlineId AS VARCHAR(20)) + ':' + CAST(D.Stage AS VARCHAR(5)));
    DECLARE @Deadlines INT = @@ROWCOUNT;

    /* System Health: admins hear about new errors once a day. */
    DECLARE @Errors INT = (SELECT COUNT(*) FROM dbo.tbl_ErrorLogs
                           WHERE Level = 'Error' AND LoggedOnDate >= DATEADD(DAY, -1, CAST(@Today AS DATETIME))
                             AND LoggedOnDate < DATEADD(DAY, 1, CAST(@Today AS DATETIME)));
    DECLARE @Key VARCHAR(120) = 'Health:' + CONVERT(VARCHAR(10), @Today, 23);
    IF @Errors > 0
        INSERT INTO dbo.tbl_UserNotifications (UserId, Title, Message, Type, LinkUrl, CreatedDate, IsRead, EventKey)
        SELECT U.User_ID, 'Errors on System Health',
               CAST(@Errors AS VARCHAR(10)) + ' error' + CASE WHEN @Errors = 1 THEN ' was' ELSE 's were' END + ' logged in the last day.',
               'warning', '/SystemHealth', GETUTCDATE(), 0, @Key
        FROM   dbo.tbl_User U
        CROSS  APPLY dbo.fn_NotificationWanted(U.User_ID, 'HealthErrors') W
        WHERE  NOT EXISTS (SELECT 1 FROM dbo.tbl_UserNotifications N WHERE N.UserId = U.User_ID AND N.EventKey = @Key);

    SELECT @Deadlines AS DeadlineNotifications, @@ROWCOUNT AS HealthNotifications;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_Notifications_Recent
    @UserId INT,
    @Top    INT = 8
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) AS Unread FROM dbo.tbl_UserNotifications WHERE UserId = @UserId AND IsRead = 0;
    SELECT TOP (@Top) NotificationId, UserId, Title, Message, Type, IsRead, CreatedDate, LinkUrl
    FROM   dbo.tbl_UserNotifications
    WHERE  UserId = @UserId
    ORDER  BY IsRead, CreatedDate DESC, NotificationId DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_NotificationPreferences_Get
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT E.EventCode,
           ISNULL(P.IsEnabled, CAST(1 AS BIT)) AS IsEnabled,
           CAST(CASE WHEN E.EventCode = 'HealthErrors'
                      AND NOT EXISTS (SELECT 1 FROM dbo.tbl_User U JOIN dbo.tbl_Roles R ON R.ID = U.Role_ID
                                      WHERE U.User_ID = @UserId AND R.RoleName IN ('SuperAdmin', 'Admin')) THEN 0
                     WHEN E.EventCode = 'Deadline'
                      AND EXISTS (SELECT 1 FROM dbo.tbl_User U JOIN dbo.tbl_Roles R ON R.ID = U.Role_ID
                                  WHERE U.User_ID = @UserId AND R.RoleName IN ('Employer', 'Broker')) THEN 0
                     ELSE 1 END AS BIT) AS IsAvailable
    FROM   (VALUES ('FileReady', 1), ('FileFailed', 2), ('FileAssigned', 3), ('ImportDone', 4), ('Deadline', 5), ('HealthErrors', 6)) E (EventCode, SortOrder)
    LEFT   JOIN dbo.NotificationPreferences P ON P.UserId = @UserId AND P.EventCode = E.EventCode
    ORDER  BY E.SortOrder;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_NotificationPreferences_Save
    @UserId    INT,
    @EventCode VARCHAR(40),
    @IsEnabled BIT
AS
BEGIN
    SET NOCOUNT ON;
    IF @EventCode NOT IN ('FileReady', 'FileFailed', 'FileAssigned', 'ImportDone', 'Deadline', 'HealthErrors') RETURN;
    MERGE dbo.NotificationPreferences AS T
    USING (SELECT @UserId AS UserId, @EventCode AS EventCode) AS S
    ON T.UserId = S.UserId AND T.EventCode = S.EventCode
    WHEN MATCHED THEN UPDATE SET IsEnabled = @IsEnabled, ModifiedAt = SYSDATETIME()
    WHEN NOT MATCHED THEN INSERT (UserId, EventCode, IsEnabled) VALUES (@UserId, @EventCode, @IsEnabled);
END
GO

/* Reading or un-reading a notification now needs its owner. @UserId stays optional so a caller
   that does not pass it keeps working, but the app always passes it. */
CREATE OR ALTER PROCEDURE dbo.sp_MarkNotificationAsRead
    @Id     INT,
    @UserId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tbl_UserNotifications SET IsRead = 1
    WHERE  NotificationId = @Id AND (@UserId IS NULL OR UserId = @UserId);
    SELECT @@ROWCOUNT AS Changed;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_MarkNotificationAsUnread
    @Id     INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tbl_UserNotifications SET IsRead = 0 WHERE NotificationId = @Id AND UserId = @UserId;
    SELECT @@ROWCOUNT AS Changed;
END
GO

PRINT '070: notification centre (file events, deadlines, health; preferences; owner checks).';
GO
GO

/* ############################################################################
   071_safe_harbor_advice.sql
   ############################################################################ */
PRINT '=== 071_safe_harbor_advice.sql';
GO
/* =============================================================================
   071 - Best safe harbor for each employee (idea #12)

   Advice only: nothing here changes a code. For an employer and tax year it
   tests each month where line 16 needs an affordability safe harbor (the
   employee was offered coverage, did not enrol, and no other line 16 code
   applies) against all three:

     2F  W-2:          premium <= affordability % x W-2 wages / months employed
     2H  Rate of pay:  premium <= affordability % x hourly rate x 130 hours
                       (salaried: x annual salary / 12)
     2G  Federal poverty line: premium <= the year's FPL amount

   from the figures sp_Generate1095Codes kept for that month (script 058:
   EmployeeCodeMonthDetail), so the advice matches the codes as generated.
   The import carries one pay figure ("W-2 income or annual salary"), so the
   W-2 and salaried rate-of-pay tests use the same number.

   sp_SafeHarborAdvice returns
     1. per safe harbor: months it covers, employees fully covered, and
        months the current line 16 codes leave without a safe harbor;
     2. per employee: months tested, months each one covers, the codes used
        now, the best safe harbor and the months it would add.

   Reads only. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_SafeHarborAdvice
    @EmployerId BIGINT,
    @Year       INT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Months AS (
        SELECT C.employeeId AS EmployeeId, D.MonthNum, D.Line16, D.Premium,
               CAST(CASE WHEN D.Premium <= D.MonthlyFpl THEN 1 ELSE 0 END AS INT) AS FplOk,
               CAST(CASE WHEN D.AnnualW2 > 0 AND D.HiredMonths > 0
                          AND D.Premium <= D.AffordabilityPct * D.AnnualW2 / D.HiredMonths THEN 1 ELSE 0 END AS INT) AS W2Ok,
               CAST(CASE WHEN D.HourlyRate > 0 AND D.Premium <= D.AffordabilityPct * D.HourlyRate * ISNULL(NULLIF(D.FullTimeHours, 0), 130) THEN 1
                         WHEN ISNULL(D.HourlyRate, 0) = 0 AND D.AnnualW2 > 0 AND D.Premium <= D.AffordabilityPct * D.AnnualW2 / 12 THEN 1
                         ELSE 0 END AS INT) AS RopOk
        FROM   dbo.EmployeeCode C
        JOIN   dbo.EmployeeCodeMonthDetail D ON D.EmployeeCodeId = C.id
        JOIN   dbo.Employee E ON E.id = C.employeeId AND ISNULL(E.IsDeleted, 0) = 0
        WHERE  C.employerId = @EmployerId AND C.filingYear = @Year
          AND  D.Employed = 1
          AND  D.Premium IS NOT NULL
          AND  ISNULL(D.Line14, '') NOT IN ('', '1A', '1H')           -- an offer that is not already a qualifying offer
          AND  ISNULL(D.Line16, '') NOT IN ('2A', '2B', '2C', '2D', '2E')   -- employed, full time, not enrolled, no waiting period
    ), PerEmployee AS (
        SELECT EmployeeId,
               COUNT(*) AS MonthsTested,
               SUM(FplOk) AS FplMonths, SUM(W2Ok) AS W2Months, SUM(RopOk) AS RopMonths,
               SUM(CASE WHEN (Line16 = '2G' AND FplOk = 1) OR (Line16 = '2F' AND W2Ok = 1) OR (Line16 = '2H' AND RopOk = 1)
                        THEN 1 ELSE 0 END) AS CurrentMonths,
               STRING_AGG(ISNULL(Line16, '-'), ',') WITHIN GROUP (ORDER BY MonthNum) AS CurrentCodes,
               MIN(Premium) AS MinPremium, MAX(Premium) AS MaxPremium
        FROM   Months
        GROUP  BY EmployeeId
    ), Ranked AS (
        SELECT P.*,
               B.Code AS BestCode, B.Months AS BestMonths
        FROM   PerEmployee P
        CROSS  APPLY (SELECT TOP 1 X.Code, X.Months
                      FROM (VALUES ('2H', P.RopMonths, 1), ('2F', P.W2Months, 2), ('2G', P.FplMonths, 3)) X (Code, Months, Pref)
                      -- a tie keeps the safe harbor already in use
                      ORDER BY X.Months DESC, CASE WHEN CHARINDEX(X.Code, P.CurrentCodes) > 0 THEN 0 ELSE 1 END, X.Pref) B
    )
    SELECT * INTO #Advice FROM Ranked;

    -- 1. Per safe harbor
    SELECT S.Code,
           S.Name,
           ISNULL(SUM(CASE S.Code WHEN '2F' THEN A.W2Months WHEN '2G' THEN A.FplMonths ELSE A.RopMonths END), 0) AS MonthsCovered,
           ISNULL(SUM(CASE WHEN (CASE S.Code WHEN '2F' THEN A.W2Months WHEN '2G' THEN A.FplMonths ELSE A.RopMonths END) = A.MonthsTested
                           THEN 1 ELSE 0 END), 0) AS EmployeesFullyCovered,
           ISNULL(SUM(A.MonthsTested), 0) AS MonthsTested,
           (SELECT COUNT(*) FROM #Advice) AS EmployeesTested,
           ISNULL(SUM(A.CurrentMonths), 0) AS CurrentMonthsCovered
    FROM   (VALUES ('2H', 'Rate of pay'), ('2F', 'W-2'), ('2G', 'Federal poverty line')) S (Code, Name)
    LEFT   JOIN #Advice A ON 1 = 1
    GROUP  BY S.Code, S.Name
    ORDER  BY MonthsCovered DESC, S.Code DESC;

    -- 2. Per employee: those the best safe harbor would help first
    SELECT TOP 1000
           A.EmployeeId, LTRIM(RTRIM(CONCAT(E.firstName, ' ', E.lastName))) AS EmployeeName,
           A.MonthsTested, A.FplMonths, A.W2Months, A.RopMonths, A.CurrentMonths, A.CurrentCodes,
           A.BestCode, A.BestMonths, A.BestMonths - A.CurrentMonths AS MonthsGained,
           A.MinPremium, A.MaxPremium
    FROM   #Advice A
    JOIN   dbo.Employee E ON E.id = A.EmployeeId
    ORDER  BY A.BestMonths - A.CurrentMonths DESC, A.MonthsTested - A.BestMonths DESC, EmployeeName;
END
GO

PRINT '071: safe harbor advice (sp_SafeHarborAdvice).';
GO
GO

/* ############################################################################
   072_state_filings.sql
   ############################################################################ */
PRINT '=== 072_state_filings.sql';
GO
/* =============================================================================
   072 - State filings: reports and exports (idea #15)

   California, New Jersey, Rhode Island and the District of Columbia want
   to know who among their residents had health coverage, month by month;
   Massachusetts wants the MA 1099-HC sent to residents and, from employers
   with six or more Massachusetts employees, the HIRD form. This first step
   reports and exports; it does not produce state e-file files.

   Employers report the coverage they self-insure; insurers report insured
   plans. So each state's figures show residents, residents with coverage,
   and residents covered by a self-insured plan (FundingType 2), which are
   the ones the employer itself would report.

   1. FilingDeadlines: Massachusetts HIRD (due 15 December of the coverage
      year), marked "check" like the other state dates.
   2. sp_StateFilings_Summary: per employer the user may see, per state.
   3. sp_StateFilings_Residents: one employer and state, each resident with
      the months covered and their covered dependents (for the export).

   Reads only, apart from the two deadline rows. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

INSERT INTO dbo.FilingDeadlines (FilingYear, Jurisdiction, Kind, Title, DueDate, Notes, NeedsCheck, IsActive, ModifiedBy, ModifiedAt)
SELECT V.FilingYear, 'MA', 'E-file', 'Massachusetts: HIRD form (employers with 6+ MA employees)', V.DueDate,
       'Filed through MassTaxConnect for the coverage year.', 1, 1, 'script 072', SYSDATETIME()
FROM   (VALUES (2025, CAST('2025-12-15' AS DATE)), (2026, CAST('2026-12-15' AS DATE))) V (FilingYear, DueDate)
WHERE  NOT EXISTS (SELECT 1 FROM dbo.FilingDeadlines D WHERE D.FilingYear = V.FilingYear AND D.Jurisdiction = 'MA' AND D.Title LIKE '%HIRD%');
GO

CREATE OR ALTER PROCEDURE dbo.sp_StateFilings_Summary
    @UserId INT,
    @Year   INT
AS
BEGIN
    SET NOCOUNT ON;

    -- The residents first, once: reading them inside the coverage query made it re-run the
    -- employer scope for each code (2.5 s on average, 34 s at worst, on the local data).
    CREATE TABLE #Scope (EmployerId BIGINT PRIMARY KEY);
    INSERT INTO #Scope (EmployerId)
    SELECT DISTINCT S.EmployerId
    FROM   dbo.fn_DashboardEmployerScope(@UserId, @Year) S
    JOIN   dbo.Employer R ON R.id = S.EmployerId AND R.filingYear = @Year;

    CREATE TABLE #Residents (EmployeeId BIGINT PRIMARY KEY, EmployerId BIGINT NOT NULL, State CHAR(2) NOT NULL);
    INSERT INTO #Residents (EmployeeId, EmployerId, State)
    SELECT E.id, E.EmployerId, UPPER(LTRIM(RTRIM(E.state)))
    FROM   dbo.Employee E
    JOIN   #Scope S ON S.EmployerId = E.EmployerId
    WHERE  ISNULL(E.IsDeleted, 0) = 0
      AND  UPPER(LTRIM(RTRIM(E.state))) IN ('CA', 'NJ', 'RI', 'DC', 'MA');

    SELECT R.EmployerId, Emp.name AS EmployerName, Emp.taxid AS EIN, R.State,
           COUNT(*) AS Residents,
           SUM(ISNULL(V.Covered, 0)) AS Covered,
           SUM(ISNULL(V.SelfInsured, 0)) AS SelfInsuredCovered
    FROM   #Residents R
    JOIN   dbo.Employer Emp ON Emp.id = R.EmployerId
    OUTER  APPLY (
        SELECT MAX(CASE WHEN D.Enrolled = 1 THEN 1 ELSE 0 END) AS Covered,
               MAX(CASE WHEN D.Enrolled = 1 AND D.FundingType = '2' THEN 1 ELSE 0 END) AS SelfInsured
        FROM   dbo.EmployeeCode C
        JOIN   dbo.EmployeeCodeMonthDetail D ON D.EmployeeCodeId = C.id
        WHERE  C.employeeId = R.EmployeeId AND C.filingYear = @Year
    ) V
    GROUP  BY R.EmployerId, Emp.name, Emp.taxid, R.State
    ORDER  BY Emp.name, R.State;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_StateFilings_Residents
    @EmployerId BIGINT,
    @Year       INT,
    @State      CHAR(2)
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Residents (employees) with the months they were enrolled, and whether self-insured.
    SELECT E.id AS EmployeeId, E.firstName AS FirstName, E.middleName AS MiddleName, E.lastName AS LastName,
           E.ssn AS Ssn, E.birthday AS Birthdate, E.address AS Address, E.city AS City, UPPER(E.state) AS State, E.zip AS Zip,
           ISNULL(M.Months, '') AS CoveredMonths,
           CAST(ISNULL(M.SelfInsured, 0) AS BIT) AS SelfInsured,
           M.PlanName
    FROM   dbo.Employee E
    OUTER  APPLY (
        SELECT STRING_AGG(CAST(D.MonthNum AS VARCHAR(2)), ',') WITHIN GROUP (ORDER BY D.MonthNum) AS Months,
               MAX(CASE WHEN D.FundingType = '2' THEN 1 ELSE 0 END) AS SelfInsured,
               MAX(D.PlanName) AS PlanName
        FROM   dbo.EmployeeCode C
        JOIN   dbo.EmployeeCodeMonthDetail D ON D.EmployeeCodeId = C.id
        WHERE  C.employeeId = E.id AND C.filingYear = @Year AND D.Enrolled = 1
    ) M
    WHERE  E.EmployerId = @EmployerId
      AND  ISNULL(E.IsDeleted, 0) = 0
      AND  UPPER(LTRIM(RTRIM(E.state))) = @State
    ORDER  BY E.lastName, E.firstName;

    -- 2. Their covered dependents (not the employee), with the months their coverage overlaps.
    SELECT CI.id AS CoveredIndividualId, C.employeeId AS EmployeeId,
           CI.firstName AS FirstName, CI.middleName AS MiddleName, CI.lastName AS LastName,
           CI.ssn AS Ssn, CI.birthday AS Birthdate, CI.Relationship,
           STUFF((SELECT ',' + CAST(N.M AS VARCHAR(2))
                  FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) N (M)
                  WHERE DATEFROMPARTS(@Year, N.M, 1) <= ISNULL(CI.coverageEndDate, '9999-12-31')
                    AND EOMONTH(DATEFROMPARTS(@Year, N.M, 1)) >= ISNULL(CI.coverageStartDate, '1900-01-01')
                  ORDER BY N.M
                  FOR XML PATH('')), 1, 1, '') AS CoveredMonths
    FROM   dbo.CoveredIndividual CI
    JOIN   dbo.EmployeeCode C ON C.id = CI.employeeCodeId AND C.filingYear = @Year
    JOIN   dbo.Employee E ON E.id = C.employeeId
    WHERE  E.EmployerId = @EmployerId
      AND  ISNULL(E.IsDeleted, 0) = 0
      AND  ISNULL(CI.IsDeleted, 0) = 0
      AND  UPPER(LTRIM(RTRIM(E.state))) = @State
      AND  ISNULL(CI.Relationship, '') NOT IN ('Self', 'Employee')
      AND  REPLACE(ISNULL(CI.ssn, ''), '-', '') <> REPLACE(ISNULL(E.ssn, '#'), '-', '')   -- the employee is listed on the first line
    ORDER  BY C.employeeId, CI.lastName, CI.firstName;
END
GO

DECLARE @Reports INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'Report' AND ParentMenuId IS NULL AND Flag = 0 ORDER BY Id);
IF @Reports IS NULL THROW 50072, '072: the Reports menu container was not found.', 1;

DECLARE @Menu INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'StateFilings' ORDER BY Id);
IF @Menu IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (@Reports, 'State Filings', 'StateFilings', 'bx bx-map-alt',
            ISNULL((SELECT MAX(DisplayOrder) FROM dbo.tbl_Menus WHERE ParentMenuId = @Reports AND Flag = 0), 0) + 1,
            1, GETDATE(), 0, 1);
    SET @Menu = SCOPE_IDENTITY();
    PRINT '  menu item added';
END
ELSE
    UPDATE dbo.tbl_Menus SET ParentMenuId = @Reports, IsActive = 1, Flag = 0, IsVisible = 1, DateModified = GETDATE()
    WHERE Id = @Menu AND (ParentMenuId <> @Reports OR IsActive <> 1 OR Flag <> 0 OR IsVisible <> 1);

INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT r.ID, @Menu, a.Id, 1, GETDATE(), 'script 072'
FROM   dbo.tbl_Roles r
CROSS  JOIN dbo.tbl_ActionTypes a
WHERE  r.RoleName IN ('SuperAdmin', 'Admin', 'ACA Director', 'DA Supervisor', 'Data Analyst',
                      'Account Manager Supervisor', 'Account Manager')
  AND  a.ActionName IN ('Index', 'View')
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions p WHERE p.RoleId = r.ID AND p.MenuId = @Menu AND p.ActionID = a.Id);

PRINT '072: state filings (summary, residents, MA HIRD deadline); Reports > State Filings.';
GO
GO

/* ############################################################################
   073_employer_column_maps.sql
   ############################################################################ */
PRINT '=== 073_employer_column_maps.sql';
GO
/* =============================================================================
   073 - Remember column mappings for each employer's files (idea #5)

   A template knows each column's header and a few alternate names. When an
   employer's file calls a column something else, the file used to fail the
   structure check and had to be fixed and uploaded again, every time.

   Now staff map the file's header to the template column once, from the
   rejected file; the mapping is kept for that employer (by EIN, so it holds
   across tax years) and template, and every later file from the employer
   is checked and read with it.

   1. EmployerColumnMaps.
   2. sp_EmployerColumnMaps_Get / _Save / _Delete.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.EmployerColumnMaps') IS NULL
BEGIN
    CREATE TABLE dbo.EmployerColumnMaps (
        EmployerColumnMapId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployerColumnMaps PRIMARY KEY,
        EmployerEin      VARCHAR(20)   NOT NULL,   -- digits only
        TemplateId       INT           NOT NULL,
        SheetName        NVARCHAR(128) NOT NULL,   -- TemplateColumnMaps.SourceSheetName
        TargetColumnName NVARCHAR(128) NOT NULL,   -- TemplateColumnMaps.TargetColumnName
        SourceHeader     NVARCHAR(200) NOT NULL,   -- the header as the employer's file has it
        CreatedBy        NVARCHAR(256) NULL,
        CreatedAt        DATETIME2(0)  NOT NULL CONSTRAINT DF_EmployerColumnMaps_CreatedAt DEFAULT SYSDATETIME(),
        CONSTRAINT UQ_EmployerColumnMaps UNIQUE (EmployerEin, TemplateId, SheetName, TargetColumnName)
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_EmployerColumnMaps_Get
    @EmployerEin VARCHAR(20),
    @TemplateId  INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT M.EmployerColumnMapId, M.EmployerEin, M.TemplateId, M.SheetName, M.TargetColumnName, M.SourceHeader,
           M.CreatedBy, M.CreatedAt, T.SourceColumnName AS TemplateHeader
    FROM   dbo.EmployerColumnMaps M
    LEFT   JOIN dbo.TemplateColumnMaps T ON T.TemplateId = M.TemplateId
                                         AND T.SourceSheetName = M.SheetName AND T.TargetColumnName = M.TargetColumnName
    WHERE  M.EmployerEin = REPLACE(@EmployerEin, '-', '')
      AND  (@TemplateId IS NULL OR M.TemplateId = @TemplateId)
    ORDER  BY M.SheetName, M.TargetColumnName;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_EmployerColumnMaps_Save
    @EmployerEin      VARCHAR(20),
    @TemplateId       INT,
    @SheetName        NVARCHAR(128),
    @TargetColumnName NVARCHAR(128),
    @SourceHeader     NVARCHAR(200),
    @CreatedBy        NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @EmployerEin = REPLACE(@EmployerEin, '-', '');
    -- Only a column the template really has.
    IF NOT EXISTS (SELECT 1 FROM dbo.TemplateColumnMaps
                   WHERE TemplateId = @TemplateId AND SourceSheetName = @SheetName AND TargetColumnName = @TargetColumnName)
    BEGIN
        SELECT CAST(0 AS INT) AS Saved;
        RETURN;
    END

    MERGE dbo.EmployerColumnMaps AS T
    USING (SELECT @EmployerEin AS EmployerEin, @TemplateId AS TemplateId, @SheetName AS SheetName, @TargetColumnName AS TargetColumnName) AS S
    ON  T.EmployerEin = S.EmployerEin AND T.TemplateId = S.TemplateId AND T.SheetName = S.SheetName AND T.TargetColumnName = S.TargetColumnName
    WHEN MATCHED THEN UPDATE SET SourceHeader = @SourceHeader, CreatedBy = @CreatedBy, CreatedAt = SYSDATETIME()
    WHEN NOT MATCHED THEN INSERT (EmployerEin, TemplateId, SheetName, TargetColumnName, SourceHeader, CreatedBy)
                          VALUES (@EmployerEin, @TemplateId, @SheetName, @TargetColumnName, @SourceHeader, @CreatedBy);
    SELECT CAST(1 AS INT) AS Saved;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_EmployerColumnMaps_Delete
    @EmployerColumnMapId INT,
    @EmployerEin         VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.EmployerColumnMaps
    WHERE  EmployerColumnMapId = @EmployerColumnMapId AND EmployerEin = REPLACE(@EmployerEin, '-', '');
    SELECT @@ROWCOUNT AS Deleted;
END
GO

PRINT '073: employer column mappings (EmployerColumnMaps).';
GO
GO

/* ############################################################################
   074_bulk_actions.sql
   ############################################################################ */
PRINT '=== 074_bulk_actions.sql';
GO
/* =============================================================================
   074 - Bulk actions (idea #20)

   The list pages let staff pick several rows and act on them at once. Most
   bulk actions repeat the existing one-row action for each row (and so its
   checks); two need the database:

   1. sp_EmployeeCodes_SetLocked: lock or unlock the codes of several
      employees for a tax year (locked codes are left alone when codes are
      generated), each change written to EmployeeCodeAuditTrail as
      FieldName 'IsLocked' so the employee's history shows who did it.
   2. sp_AssignableStaff: the account managers or data analysts that
      employers can be assigned to (the assignment screens key them by
      their staff account, tbl_User.Ref_ID).

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_EmployeeCodes_SetLocked
    @EmployeeIds NVARCHAR(MAX),   -- JSON array of employee ids
    @Year        INT,
    @Locked      BIT,
    @UserId      INT,
    @UserName    NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Changed TABLE (EmployeeId BIGINT, EmployerId BIGINT, WasLocked BIT);

    UPDATE C
    SET    C.IsLocked = @Locked
    OUTPUT inserted.employeeId, inserted.employerId, deleted.IsLocked INTO @Changed
    FROM   dbo.EmployeeCode C
    WHERE  C.filingYear = @Year
      AND  C.employeeId IN (SELECT TRY_CAST(value AS BIGINT) FROM OPENJSON(@EmployeeIds))
      AND  ISNULL(C.IsLocked, 0) <> @Locked;

    INSERT INTO dbo.EmployeeCodeAuditTrail (EmployeeId, EmployerId, FilingYear, MonthCode, AppliesToAllMonths, LineNumber,
                                            FieldName, OldValue, NewValue, ChangeSource, ChangeReason, SafeHarborApplied,
                                            ChangedByUserId, ChangedByUserName, IsSystemGenerated, ChangedOn)
    SELECT EmployeeId, EmployerId, @Year, 'ALL', 1, 0,
           'IsLocked', CASE WHEN ISNULL(WasLocked, 0) = 1 THEN '1' ELSE '0' END, CASE WHEN @Locked = 1 THEN '1' ELSE '0' END,
           'MANUAL', CASE WHEN @Locked = 1 THEN 'Codes locked (bulk)' ELSE 'Codes unlocked (bulk)' END, NULL,
           @UserId, @UserName, 0, GETDATE()
    FROM   @Changed;

    SELECT COUNT(*) AS Changed FROM @Changed;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_AssignableStaff
    @Side VARCHAR(2)   -- AM or DA
AS
BEGIN
    SET NOCOUNT ON;
    SELECT U.Ref_ID AS RefId, U.Role_ID AS RoleId, U.User_Name AS UserName,
           COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(S.Staff_FName, ' ', S.Staff_LName))), ''), U.User_Name) AS Name, R.RoleName
    FROM   dbo.tbl_User U
    JOIN   dbo.tbl_Roles R ON R.ID = U.Role_ID
    LEFT   JOIN dbo.StaffAccounts S ON S.Staff_ID = U.Ref_ID
    WHERE  ISNULL(U.Flag, 1) = 1
      AND  U.Ref_ID IS NOT NULL
      AND  ((@Side = 'AM' AND R.RoleName IN ('Account Manager', 'Account Manager Supervisor'))
         OR (@Side = 'DA' AND R.RoleName IN ('Data Analyst', 'DA Supervisor')))
    ORDER  BY Name;
END
GO

PRINT '074: bulk actions (lock/unlock codes, assignable staff).';
GO
GO

/* ############################################################################
   075_my_sessions.sql
   ############################################################################ */
PRINT '=== 075_my_sessions.sql';
GO
/* =============================================================================
   075 - "My sign-in sessions" on Account Settings > Security

   Lets a user see where they are signed in (the server-side sessions of
   script 023) and sign out everywhere except the browser they are using.

   1. sp_AuthSession_ListForSubject: the subject's sessions that are still
      live (not revoked, used within the idle timeout).
   2. sp_AuthSession_RevokeOthers: revokes every live session of the
      subject except the one given.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_ListForSubject
    @Subject     NVARCHAR(100),
    @IdleMinutes INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SessionId, CreatedAt, LastActivityAt, IpAddress, UserAgent
    FROM   dbo.tbl_AuthSessions
    WHERE  Subject = @Subject
      AND  RevokedAt IS NULL
      AND  LastActivityAt >= DATEADD(MINUTE, -@IdleMinutes, SYSUTCDATETIME())
    ORDER  BY LastActivityAt DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_RevokeOthers
    @Subject   NVARCHAR(100),
    @KeepSessionId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tbl_AuthSessions
    SET    RevokedAt = SYSUTCDATETIME()
    WHERE  Subject = @Subject AND RevokedAt IS NULL AND SessionId <> @KeepSessionId;
    SELECT @@ROWCOUNT AS Revoked;
END
GO

PRINT '075: my sign-in sessions (list, sign out everywhere else).';
GO
GO

PRINT '=== Done: 50 scripts (023-075, without 035).';
GO
