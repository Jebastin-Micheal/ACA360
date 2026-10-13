namespace ACA360.Web.Models
{
    public class NotesWidgetViewModel
    {
        public string EntityType { get; set; } = string.Empty;
        public int EntityId { get; set; }
        public string Title { get; set; } = string.Empty;
        /// <summary>"Table" (default, used on Employer/Plan/Employee detail pages) or "Cards" (the global notes popup).</summary>
        public string DisplayMode { get; set; } = "Table";
    }
}
