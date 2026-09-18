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

    /// <summary>Everything one render of the dashboard needs.</summary>
    public class EmployerDashboardPageViewModel
    {
        public EffectiveContext Context { get; set; } = new();
        public ViewAsOptions ViewAs { get; set; } = new();

        public int FilingYear { get; set; }
        public List<FilingYearModel> AvailableYears { get; set; } = new();

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