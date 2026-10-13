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
