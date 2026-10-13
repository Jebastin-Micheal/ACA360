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
