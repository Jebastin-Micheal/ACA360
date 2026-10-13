namespace ACA360.Core.Models
{
    /// <summary>
    /// Best line 16 safe harbor (script 071), advice only. Months tested are those where line 16 needs an
    /// affordability safe harbor: offered coverage, not enrolled, no other line 16 code.
    /// </summary>
    public class SafeHarborAdvice
    {
        public int EmployerId { get; set; }
        public string? EmployerName { get; set; }
        public int Year { get; set; }
        public List<SafeHarborOption> Options { get; set; } = new();
        public List<SafeHarborEmployee> Employees { get; set; } = new();
        public SafeHarborOption? Best => Options.OrderByDescending(o => o.MonthsCovered).FirstOrDefault();
    }

    public class SafeHarborOption
    {
        /// <summary>2F (W-2), 2G (federal poverty line) or 2H (rate of pay).</summary>
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int MonthsCovered { get; set; }
        public int EmployeesFullyCovered { get; set; }
        public int MonthsTested { get; set; }
        public int EmployeesTested { get; set; }
        /// <summary>Months the line 16 codes in use now protect (the same for every row).</summary>
        public int CurrentMonthsCovered { get; set; }
    }

    public class SafeHarborEmployee
    {
        public long EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public int MonthsTested { get; set; }
        public int FplMonths { get; set; }
        public int W2Months { get; set; }
        public int RopMonths { get; set; }
        public int CurrentMonths { get; set; }
        /// <summary>Line 16 per tested month, comma separated ("-" when blank).</summary>
        public string? CurrentCodes { get; set; }
        public string BestCode { get; set; } = "";
        public int BestMonths { get; set; }
        public int MonthsGained { get; set; }
        public decimal? MinPremium { get; set; }
        public decimal? MaxPremium { get; set; }
    }
}
