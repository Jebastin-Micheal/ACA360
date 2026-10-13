using ACA360.Core.Models;

namespace ACA360.Web.Helpers
{
    /// <summary>
    /// Where a menu entry goes, and which entry the sidebar highlights. The links are plain
    /// addresses (/Employee, not /Employee?menuId=12): a menu row holds only a controller, so the
    /// entry is found from the page's controller and action instead of a menu id in the URL.
    /// </summary>
    public static class MenuLinks
    {
        /// <summary>
        /// The action a menu entry opens. "Firm Management" and "Broker Management" both point at
        /// the Partner controller; the broker entry opens /Partner/Brokers, the same page with the
        /// broker list first. Every other entry opens its controller's Index.
        /// </summary>
        public static string ActionFor(string? controller, string? menuName) =>
            string.Equals(controller?.Trim(), "Partner", StringComparison.OrdinalIgnoreCase)
                && menuName?.Contains("Broker", StringComparison.OrdinalIgnoreCase) == true
                ? "Brokers"
                : "Index";

        /// <summary>
        /// The entry to highlight for the current page: one whose controller and action match,
        /// else the first one for the controller. Entries without children (the ones that open a
        /// page) come before their parents, which can share the controller (Master Settings and
        /// Validation Rules are both Rules). Null when no entry opens this controller.
        /// </summary>
        public static int? ActiveMenuId(IReadOnlyList<Menu> menus, string? controller, string? action)
        {
            if (string.IsNullOrEmpty(controller)) return null;

            var candidates = menus
                .Where(m => string.Equals(m.Controller?.Trim(), controller, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => menus.Any(c => c.ParentMenuId == m.Id) ? 1 : 0)
                .ToList();

            var exact = candidates.FirstOrDefault(m =>
                string.Equals(ActionFor(m.Controller, m.MenuName), action, StringComparison.OrdinalIgnoreCase));
            return (exact ?? candidates.FirstOrDefault())?.Id;
        }
    }
}
