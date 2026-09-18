namespace ACA360.Core.Models.Tracker
{
    public class TrackerProcess
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public int CategoryId { get; set; }
        public string? CategoryName { get; set; } // For Display
        public int? FollowUpDays { get; set; }
        public int? DisplayOrder { get; set; }
        public int TotalCount { get; set; } // For Pagination
        public string WebName { get; set; }
        public bool IsDoNotDisplay { get; set; }
        public bool IsDeleted { get; set; }
        public int CompletionPercentage { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}