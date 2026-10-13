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
