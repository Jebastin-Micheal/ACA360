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
