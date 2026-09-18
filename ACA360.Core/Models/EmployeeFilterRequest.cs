using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class EmployeeFilterRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int SortColumn { get; set; } 
        public string SortOrder { get; set; } = "asc";
        public string Search { get; set; } = "asc";

        // Quick filter buttons above the list: all | flagged | missing | valid
        public string? QuickFilter { get; set; }

        public List<long> Employer_IDs { get; set; } = new();
        public List<long> EnrollmentIDs { get; set; } = new();
        public List<long> PlanIDs { get; set; } = new();
        public List<long> OtherIDs { get; set; } = new();
        public List<long> Flags { get; set; } = new();

        public string? FullName { get; set; }
        public string? EmployeeID { get; set; }
        public string? Email { get; set; }
        public List<string> Statuses { get; set; } = new();
        public string? City { get; set; }
        public string? Zip { get; set; }
        public string? Status { get; set; }
        public string? planName { get; set; }
        public string? FlagReason { get; set; }
        public int? UserID { get; set; }
        public int? Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Suffix { get; set; }
        public string? SSN { get; set; }
        public string? FilingYear { get; set; }

    }
}
