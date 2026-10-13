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
