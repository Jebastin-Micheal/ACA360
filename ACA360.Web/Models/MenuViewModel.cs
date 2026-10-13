using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Web.Models
{
    public class MenuFormViewModel
    {
        public Menu Menu { get; set; } = new Menu();

        public List<ParentMenuDto>? ParentMenus { get; set; }
        public List<int> SelectedActionIds { get; set; } = new List<int>(); // multiselect actions

        public List<SelectListItem> AvailableActions { get; set; } = new List<SelectListItem>();
    }

    public class MenuViewModel
    {
        public List<Menu> Menus { get; set; } = new List<Menu>();
        public PaginationViewEntity Pagination { get; set; } = new PaginationViewEntity();
        public PaginationViewEntity Metadata { get; internal set; } = new PaginationViewEntity();
        // Search string for filtering employers
        public string? Search { get; set; }

        // Column index to determine which column to sort by
        public int SortColumn { get; set; }

        // Sort order, either "asc" or "desc"
        public string? SortOrder { get; set; }
        public bool IsVisible { get; set; } // <--- Add this
    }
}
