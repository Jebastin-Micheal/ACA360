using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Web.Models
{
    public class RulesFormViewModel
    {
        public ValidationRule Rules { get; set; } = new ValidationRule();
        public List<SelectListItem> TargetTables { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> ValidationTypes { get; set; } = new List<SelectListItem>();

    }

    public class RulesViewModel
    {
        public List<ValidationRule>? Rules { get; set; }
        public PaginationViewEntity? Pagination { get; set; }
        public PaginationViewEntity Metadata { get; set; } = new PaginationViewEntity();
        // Search string for filtering employers
        public string? Search { get; set; }
        // Column index to determine which column to sort by
        public int SortColumn { get; set; }
        // Sort order, either "asc" or "desc"
        public string? SortOrder { get; set; }
    }
}
