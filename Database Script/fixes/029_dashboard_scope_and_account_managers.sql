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
