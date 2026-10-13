using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Web.Models
{
    public class EmployerListViewModel
    {
        // List of employers to be displayed
        public List<Employer> Employers { get; set; } = new List<Employer>();

        // Metadata for pagination (e.g., total count, total pages)
        public PaginationViewEntity Metadata { get; set; } = new PaginationViewEntity();

        // Search string for filtering employers
        public string? Search { get; set; }
        public string? SearchFields { get; set; }
        public List<SelectListItem> TypeFilters { get; set; } = new List<SelectListItem>();
        public string? TypeFilter { get; set; }
        // Column index to determine which column to sort by
        public int SortColumn { get; set; }

        // Sort order, either "asc" or "desc"
        public string? SortOrder { get; set; }
        public string? ViewMode { get; set; }
    }
}
