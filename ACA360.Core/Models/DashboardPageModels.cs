using ACA360.Core.Models.Dashboard;
using System;
using System.Collections.Generic;
namespace ACA360.Core.Models
{
    /// <summary>Book-of-business band. Scope is decided server-side.</summary>
    public class PortfolioCountsDto
    {
        public int TotalClientsAllEins { get; set; }
        public int TotalClientsMainEins { get; set; }
        public int ClientsFormsMailed { get; set; }
        public int ClientsEfiled { get; set; }
        public int ClientsWithPenalties { get; set; }
        public decimal TotalPenaltyExposure { get; set; }
        public int PastDueClients { get; set; }
        public int OldestPastDueDays { get; set; }
    }

    public class PortfolioProcessRow
    {
        public int EmployerId { get; set; }
        public string? EmployerName { get; set; }
        public string? Ein { get; set; }
        public string? ProcessStep { get; set; }
        public DateTime? DueDate { get; set; }
        public int DaysOverdue { get; set; }

        public string Severity =>
            DaysOverdue > 14 ? "crit" : DaysOverdue > 0 ? "warn" : "good";
    }

    public class PlanSummaryRow
    {
        public int PlanId { get; set; }
        public string? PlanName { get; set; }
        public string? PlanType { get; set; }
        public string? FundingType { get; set; }
        public string? BandingType { get; set; }
        public int? RenewalMonth { get; set; }
        public string? WaitingPeriod { get; set; }
        public bool OfferedSpouse { get; set; }
        public bool OfferedDependents { get; set; }
        public bool MinimumValue { get; set; }
        public decimal? LowestCost { get; set; }

        public string RenewalMonthName =>
            RenewalMonth is >= 1 and <= 12
                ? System.Globalization.CultureInfo.GetCultureInfo("en-US")
                    .DateTimeFormat.GetMonthName(RenewalMonth.Value)
                : "—";
    }

    /// <summary>
    /// IRS AIR transmission results for a plan year, counted per submission.
    /// StatusId: 0 ready to transmit, 1 transmitted, 2 accepted,
    /// 3 accepted with errors, 4 rejected; no submission = not generated yet.
    /// </summary>
    public class IrsFilingOverview
    {
        public int NotGenerated { get; set; }
        public int ReadyToTransmit { get; set; }
        public int Transmitted { get; set; }
        public int Accepted { get; set; }
        public int AcceptedWithErrors { get; set; }
        public int Rejected { get; set; }
        public List<IrsRejectionDto> RecentRejections { get; set; } = new();

        public int Total => NotGenerated + ReadyToTransmit + Transmitted + Accepted + AcceptedWithErrors + Rejected;
    }

    /// <summary>
    /// File results for one analyst in a plan year (sp_GetDashboardFileCounts, RS1).
    /// Categories follow dbo.fn_FileStatusBucket, like the File Upload page.
    /// AnalystUserId null = files not assigned to anyone yet.
    /// </summary>
    public class AnalystFileCounts
    {
        public int? AnalystUserId { get; set; }
        public string? AnalystName { get; set; }
        public int Accepted { get; set; }
        public int Rejected { get; set; }
        public int Processing { get; set; }
        public int CriticalErrors { get; set; }
        public int Waiting { get; set; }
    }

    /// <summary>System figures for the Admin dashboard (sp_GetDashboardSystemStats, script 032).</summary>
    public class DashboardSystemStats
    {
        public int ActiveUsers { get; set; }
        public int ActiveAdmins { get; set; }
        public int LockedOutUsers { get; set; }
        public int PendingPassword { get; set; }
        public int SignedInNow { get; set; }
        public int FilesThisMonth { get; set; }
        public int FilesLastMonth { get; set; }
    }

    /// <summary>An open file in the analyst's priority queue (sp_GetDashboardFileCounts, RS2).</summary>
    public class DashboardQueueFile
    {
        public int FileLogId { get; set; }
        public string? OriginalFileName { get; set; }
        public string? EmployerName { get; set; }
        public DateTime UploadedAt { get; set; }
        public int ValidationStatusId { get; set; }
        public int WorkflowStatusId { get; set; }
    }

    /// <summary>An employer's account manager (sp_GetDashboardAccountManagers, RS1).</summary>
    public class EmployerManagerLink
    {
        public int EmployerId { get; set; }
        public int AccountManagerUserId { get; set; }
        public string? AccountManagerName { get; set; }
    }

    /// <summary>One employer in the AM Supervisor's "by account manager" table.</summary>
    public class ManagerClientRow
    {
        public int EmployerId { get; set; }
        public string? EmployerName { get; set; }
        public string? Ein { get; set; }
        public bool IsAffiliate { get; set; }
        /// <summary>Comma-separated when an employer has more than one; null when unassigned.</summary>
        public string? ManagerNames { get; set; }
        public string? ProcessStep { get; set; }
        public DateTime? DueDate { get; set; }
        public int DaysOverdue { get; set; }
    }

    /// <summary>A plain-language alert for the dashboard, with where to act on it.</summary>
    public class DashboardAlert
    {
        public string Severity { get; set; } = "warn";   // "crit" | "warn" | "info"
        public string Text { get; set; } = "";
        public string? LinkText { get; set; }
        public string? LinkUrl { get; set; }
    }

    /// <summary>Role-specific tiles on the home dashboard.</summary>
    public class RoleSectionData
    {
        public IrsFilingOverview? Irs { get; set; }
        public List<DashboardAlert> Alerts { get; set; } = new();

        /// <summary>Employer / process step / due date, with the account manager (AM Supervisor).</summary>
        public List<ManagerClientRow> ClientsByManager { get; set; } = new();
        public List<StaffLoad> TeamWorkload { get; set; } = new();

        /// <summary>File results per analyst (DA Supervisor) or the analyst's own row (Data Analyst).</summary>
        public List<AnalystFileCounts> FileCounts { get; set; } = new();
        /// <summary>The Data Analyst's open files, most urgent first.</summary>
        public List<DashboardQueueFile> Queue { get; set; } = new();

        /// <summary>System figures (Admin / SuperAdmin).</summary>
        public DashboardSystemStats? System { get; set; }

        /// <summary>Show the user-management tile; false when the user may not open User Accounts.</summary>
        public bool CanManageUsers { get; set; }
        /// <summary>Full user management (add, delete, reset) rather than password resets only.</summary>
        public bool CanAddUsers { get; set; }
    }

    /// <summary>Everything one render of the dashboard needs.</summary>
    public class EmployerDashboardPageViewModel
    {
        public EffectiveContext Context { get; set; } = new();
        public ViewAsOptions ViewAs { get; set; } = new();

        /// <summary>
        /// The page hosting the dashboard. The employer and year pickers and the
        /// process-table links reload this page, so a user on the home dashboard
        /// stays on it rather than being sent to Employer/Dashboard.
        /// </summary>
        public string PageController { get; set; } = "Employer";
        public string PageAction { get; set; } = "Dashboard";

        /// <summary>Key the customized tile layout is saved under.</summary>
        public string DashboardKey { get; set; } = "employer";

        /// <summary>
        /// Role-specific tiles shown above the shared bands (a partial view name),
        /// or null when the role has none.
        /// </summary>
        public string? RoleSectionPartial { get; set; }

        /// <summary>Data for the role-specific tiles; which members are set depends on the role.</summary>
        public RoleSectionData Role { get; set; } = new();

        public int FilingYear { get; set; }
        public List<FilingYearModel> AvailableYears { get; set; } = new();

        /// <summary>The next filing deadlines (dbo.FilingDeadlines, script 063), shown to every role.</summary>
        public List<FilingDeadline> UpcomingDeadlines { get; set; } = new();

        /// <summary>Employers in scope whose estimated penalty grew most since last year (script 064).</summary>
        public List<ExposureGrowthRow> ExposureGrowth { get; set; } = new();

        public PortfolioCountsDto Portfolio { get; set; } = new();
        public List<PortfolioProcessRow> ProcessRows { get; set; } = new();

        public Employer? Employer { get; set; }
        // <summary>Employer.Id is stored as a string; parse once for the view.</summary>
        public int EmployerIdValue =>
            int.TryParse(Employer?.Id, out var v) ? v : 0;
        public List<Employer> SelectableEmployers { get; set; } = new();
        public EmployerDashboardDto Employer360 { get; set; } = new();
        public List<PlanSummaryRow> Plans { get; set; } = new();
        public List<AleTrendMonthDto> AleTrend { get; set; } = new();
        public int[] CoveragePercent { get; set; } = new int[12];
        public decimal PenaltyExposure { get; set; }
        public string? PenaltyType { get; set; }
        public int WorkforceFullTime { get; set; }
        public int WorkforcePartTime { get; set; }
        public int WorkforceVariable { get; set; }
        public int WorkforceCobra { get; set; }
        /// <summary>An Employer user has one client — themselves — so the band is noise.</summary>
        public bool ShowPortfolioBand =>
            !string.Equals(Context.EffectiveRole, "Employer", StringComparison.OrdinalIgnoreCase);
        public decimal PenaltyTypeA { get; set; }
        public decimal PenaltyTypeB { get; set; }
        public bool PenaltyTypeATriggered { get; set; }
        public int PenaltyChargeableFTEs { get; set; }
        public int PenaltyApplicableMonths { get; set; }
        public int PenaltyTypeBViolations { get; set; }
        public DateTime? PenaltyCalculatedDate { get; set; }

        /// <summary>Which section(s) are in play, for the card's badge.</summary>
        public string PenaltyLabel =>
            PenaltyTypeA > 0 && PenaltyTypeB > 0 ? "§4980H(a) + (b)"
            : PenaltyTypeA > 0 ? "§4980H(a)"
            : PenaltyTypeB > 0 ? "§4980H(b)"
            : "";
        /// <summary>
        /// The 1095 process has 57 stages, 16 of them client-visible, and several are
        /// alternatives rather than steps in sequence. Rendering them as a line would
        /// imply an employer passes through all of them. These five phases are derived
        /// from Tracker_Process.CompletionPercentage, so a new stage lands in the right
        /// phase automatically.
        /// </summary>
        public static readonly (string Name, int From, int To)[] ReadinessPhases =
        {
            ("Setup",         0,  10),
            ("Data collection", 11, 49),
            ("Production",    50,  79),
            ("Client review", 80,  94),
            ("Filing",        95, 100)
        };
    }

}