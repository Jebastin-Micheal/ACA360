namespace ACA360.Core.Models
{
    public class Menu
    {
        public int Id { get; set; }
        public int? ParentMenuId { get; set; }
        public string? MenuName { get; set; }
        public string? Controller { get; set; }
        public string? Icon { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime? DateModified { get; set; }
        public string? Flag { get; set; }
        public List<MenuAction> Actions { get; set; } = new List<MenuAction>();
        public bool IsVisible { get; set; } // <--- Add this
    }
    public class MenuFormDataDto
    {
        public Menu? Menu { get; set; }
        public List<ParentMenuDto>? ParentMenus { get; set; }
        public List<int>? SelectedActionIds { get; set; }
    }

    public class ParentMenuDto
    {
        public int Id { get; set; }
        public string? MenuName { get; set; }
        public int? ParentMenuId { get; set; }
    }
}
