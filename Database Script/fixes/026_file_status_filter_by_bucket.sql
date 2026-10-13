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
