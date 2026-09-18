using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Web.Models
{
    public class RoleFormViewModel
    {
        public Role Role { get; set; } = new Role();

    }

    public class RoleViewModel
    {
        public List<Role>? Roles { get; set; }
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
