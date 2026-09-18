using ACA360.Core.Models;
using System.Collections.Generic;

namespace ACA360.Web.Models
{
    public class EmployeeListViewModel
    {
        // List of employees to be displayed
        public List<Employee>? Employees { get; set; }

        // Detailed info for a selected employee
        public EmployeeBasicDetails? SelectedEmployeeDetails { get; set; }

        // Flags raised for the selected employee (Flags tab in split view)
        public List<EmployeeFlagDetail> EmployeeFlags { get; set; } = new List<EmployeeFlagDetail>();
        //public List<CoveredIndividualModel>? CoveredIndividuals { get; set; }
        //public List<CoveredIndividualModel>? Dependents { get; set; }

        // Metadata for pagination (e.g., total count, pages)
        public PaginationViewEntity? Metadata { get; set; }

        // Search string for filtering employees
        public string? Search { get; set; }

        // Column index for sorting
        public int SortColumn { get; set; }

        // Sort direction: "asc" or "desc"d
        public string? SortOrder { get; set; }
        public string? ViewMode { get; set; }
        public List<EmployeeFilterRequest> EmployeesFilter { get; set; } = new List<EmployeeFilterRequest>();
    }
}
