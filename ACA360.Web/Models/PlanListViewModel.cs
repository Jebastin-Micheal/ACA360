using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Web.Models
{
    public class PlanListViewModel
    {
        // List of employers to be displayed
        public List<Plan> Plans { get; set; } = new List<Plan>();

        public List<PlanBenefit> Benefits { get; set; } = new List<PlanBenefit>();

        public List<SelectListItem> Plan_Banding_Type { get; set; } = new List<SelectListItem>();

        public List<SelectListItem> Plan_Waiting_Period { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Plan_Type { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Plan_Funding_Type { get; set; } = new List<SelectListItem>();
        // Metadata for pagination (e.g., total count, total pages)
        public PaginationViewEntity Metadata { get; set; } = new PaginationViewEntity();

        // Search string for filtering employers
        public string? Search { get; set; }

        // Column index to determine which column to sort by
        public int SortColumn { get; set; }

        // Sort order, either "asc" or "desc"
        public string? SortOrder { get; set; }
        public string? ViewMode { get; set; }
    }
}
