namespace ACA360.Core.Models
{
    // Role.cs
    public class Roles
    {
        public int Id { get; set; }
        public string? RoleName { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    // Menu.cs
    public class Menus
    {
        public int Id { get; set; }
        public int? ParentMenuId { get; set; }
        public string? MenuName { get; set; }
        public string? Controller { get; set; }
        public string? Icon { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime DateModified { get; set; }
        public short Flag { get; set; }
        public List<MenuAction> Actions { get; set; } = new List<MenuAction>();
        public bool IsVisible { get; set; } // <--- Add this
    }

    // ActionType.cs
    public class ActionType
    {
        public int Id { get; set; }
        public string? ActionName { get; set; }
        public bool IsActive { get; set; }
        public DateTime DateAdded { get; set; }
        public string? AddedBy { get; set; }
        public DateTime? DateModified { get; set; }
        public string? ModifiedBy { get; set; }
    }

    // MenuAction.cs
    public class MenuAction
    {
        public int Id { get; set; }
        public int MenuId { get; set; }
        public int ActionId { get; set; }
        public bool IsActive { get; set; }
        public DateTime DateAdded { get; set; }
        public string? AddedBy { get; set; }
        public DateTime? DateModified { get; set; }
        public string? ModifiedBy { get; set; }
        public ActionType ActionType { get; set; } = new ActionType();
        public string? ActionName { get; set; }
    }

    // Permission.cs
    public class Permission
    {
        public int Id { get; set; }
        public int RoleId { get; set; }
        public int MenuId { get; set; }
        public int ActionId { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime DateAdded { get; set; }
        public string? AddedBy { get; set; }
        public DateTime? DateModified { get; set; }
        public string? ModifiedBy { get; set; }
    }

    // PermissionViewModel.cs
    public class PermissionViewModel
    {
        public int MenuId { get; set; }
        public string? MenuName { get; set; }
        public string? Controller { get; set; }
        public string? Icon { get; set; }
        public int DisplayOrder { get; set; }
        public int ActionId { get; set; }
        public string? ActionName { get; set; }
        public bool IsEnabled { get; set; }
    }

    // PermissionViewModels.cs
    public class RolePermissionViewModels
    {
        public int RoleId { get; set; }
        public string? RoleName { get; set; }
        public List<PermissionViewModel> Permissions { get; set; } = new List<PermissionViewModel>();
    }

}
