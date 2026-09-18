namespace ACA360.Core.Models
{
    public class PenaltyRiskDto
    {

        public decimal EstimatedPenaltyAmount => TypeA_Triggered ? TypeA_TotalExposure : TypeB_TotalExposure;

        public int AtRiskEmployeeCount => TypeA_Triggered ? TypeA_AtRiskEmployees.Count : AtRiskEmployees.Count;

        public int MissingPlanStartCount { get; set; }
        // General Info
        public int TaxYear { get; set; }

        // --- 4980H(A) "Sledgehammer" Data ---
        public bool TypeA_Triggered { get; set; } // If < 95% Offer
        public int TotalFullTimeEmployees { get; set; }
        public int ReliefCount { get; set; } = 30;
        public decimal TypeA_MonthlyRate { get; set; }
        public decimal TypeA_TotalExposure { get; set; }
        public int TypeA_ApplicableMonths { get; set; }
        // --- 4980H(B) "Tack Hammer" Data ---
        public int TypeB_TotalViolations { get; set; } // Total Employee-Months
        public decimal TypeB_MonthlyRate { get; set; }
        public decimal TypeB_TotalExposure { get; set; }

        // --- Combined Total ---
        public decimal TotalEstimatedPenalty => TypeA_TotalExposure + TypeB_TotalExposure;

        // Details List for Drill-down
        public List<AtRiskEmployeeDto> AtRiskEmployees { get; set; } = new List<AtRiskEmployeeDto>();
        public List<AtRiskEmployeeDto> TypeA_AtRiskEmployees { get; set; } = new List<AtRiskEmployeeDto>();
    }
    public class AtRiskEmployeeDto
    {
        public int EmployeeId { get; set; }
        public string? FullName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? SSN { get; set; }
        public int BadMonthCount { get; set; }
        public string? BadMonthsList { get; set; } // e.g. "Jan, Feb, Mar"
        public decimal EstimatedPenalty { get; set; }
    }
}