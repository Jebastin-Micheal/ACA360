namespace ACA360.Core.Models
{
    /// <summary>The states with their own coverage reporting (script 072).</summary>
    public static class StateFilingStates
    {
        public static readonly IReadOnlyList<(string Code, string Name, string Note)> All = new List<(string, string, string)>
        {
            ("CA", "California", "Report residents' coverage to the Franchise Tax Board; statements to residents."),
            ("NJ", "New Jersey", "Report residents' coverage to the Division of Taxation; statements to residents."),
            ("RI", "Rhode Island", "Report residents' coverage to the Division of Taxation; statements to residents."),
            ("DC", "District of Columbia", "Report residents' coverage to the Office of Tax and Revenue."),
            ("MA", "Massachusetts", "MA 1099-HC to residents (from the insurer or plan); the HIRD form from employers with 6+ MA employees."),
        };

        public static bool IsState(string? code) => All.Any(s => s.Code == code);
        public static string NameOf(string code) => All.FirstOrDefault(s => s.Code == code).Name ?? code;
    }

    public class StateFilingSummaryRow
    {
        public long EmployerId { get; set; }
        public string? EmployerName { get; set; }
        public string? EIN { get; set; }
        public string State { get; set; } = "";
        public int Residents { get; set; }
        public int Covered { get; set; }
        /// <summary>Covered by a self-insured plan: the coverage the employer itself reports.</summary>
        public int SelfInsuredCovered { get; set; }
    }

    public class StateFilingResident
    {
        public long EmployeeId { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Ssn { get; set; }
        public DateTime? Birthdate { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        /// <summary>Months enrolled, "1,2,3".</summary>
        public string? CoveredMonths { get; set; }
        public bool SelfInsured { get; set; }
        public string? PlanName { get; set; }
    }

    public class StateFilingDependent
    {
        public long CoveredIndividualId { get; set; }
        public long EmployeeId { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Ssn { get; set; }
        public DateTime? Birthdate { get; set; }
        public string? Relationship { get; set; }
        public string? CoveredMonths { get; set; }
    }
}
