using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.ViewModels
{
    public class PenaltyDashboardViewModel
    {
        public int EmployerId { get; set; }
        public string EmployerName { get; set; }
        public int TaxYear { get; set; }

        // Type A Data
        public bool TypeA_Triggered { get; set; }
        public int TotalFullTimeEmployees { get; set; }
        public int ReliefCount { get; set; }
        public decimal TypeA_MonthlyRate { get; set; }
        public decimal TypeA_TotalExposure { get; set; }
        public int TypeA_ApplicableMonths { get; set; }
        // Type B Data
        public int TypeB_TotalViolations { get; set; }
        public decimal TypeB_MonthlyRate { get; set; }
        public decimal TypeB_TotalExposure { get; set; }

        // List for 4980H(b) (Tack Hammer) 
        public List<PenaltyDetailRow> RiskDetails { get; set; } = new List<PenaltyDetailRow>();
        // Only populated if TypeA_Triggered = true
        // [NEW] List for 4980H(a) (Sledgehammer)
        public List<PenaltyDetailRow> TypeA_RiskDetails { get; set; } = new List<PenaltyDetailRow>();
    }

    // UPDATE THIS CLASS DEFINITION:
    public class PenaltyDetailRow
    {
        public int EmployeeId { get; set; }           // Added
        public string EmployeeName { get; set; }
        public string SSN { get; set; }
        public int BadMonthCount { get; set; }        // Added
        public string BadMonthsList { get; set; }     // Added (e.g. "Jan, Feb")
        public decimal EstimatedPenalty { get; set; } // Added

        // Removed 'Month' and 'CodeCombination' (single row) 
        // because we now group multiple months into one row per employee.
    }
}
