namespace ACA360.Core.Models.Tracker
{
    public class ServiceItem
    {
        public int ServiceId { get; set; }
        public string ServiceName { get; set; }
        
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;

        // For Pagination logic
        public int TotalCount { get; set; }
    }
}