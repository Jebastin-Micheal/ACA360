namespace ACA360.Web.Models
{
    public class BreadcrumbViewModel
    {
        public string? PageTitle { get; set; }
        public List<BreadcrumbItem> Items { get; set; } = new List<BreadcrumbItem>();
    }

    public class BreadcrumbItem
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public bool IsActive { get; set; }
    }
}
