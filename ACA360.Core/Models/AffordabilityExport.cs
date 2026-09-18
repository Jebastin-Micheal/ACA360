namespace ACA360.Core.Models
{
    /// <summary>
    /// One employee row used by the Affordability extract. Holds the identity
    /// fields shown in the export plus the raw inputs needed to run the
    /// W-2 and Rate-of-Pay affordability safe-harbor tests.
    /// </summary>
    public class AffordabilityExportRow
    {
        public int EmployeeId { get; set; }

        // Identity (columns written to the workbook)
        public string? SSN { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? TaxId { get; set; }
        public string? Employer { get; set; }

        // Affordability inputs
        public decimal? LowestCostPremium { get; set; } // cheapest self-only monthly premium offered
        public decimal? W2Wages { get; set; }           // annual W-2 Box 1 wages
        public decimal? HourlyRate { get; set; }
        public decimal? MonthlyHours { get; set; }
    }

    /// <summary>
    /// Which affordability bucket (worksheet) an employee lands in.
    /// </summary>
    public enum AffordabilityBucket
    {
        Affordable = 0,        // "Rate of Pay or W2 affordable"
        W2Unaffordable = 1,    // "W2 unaffordable"
        RateOfPayUnaffordable = 2 // "Rate of Pay unaffordable"
    }
}
