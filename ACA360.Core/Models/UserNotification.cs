namespace ACA360.Core.Models
{
    public class UserNotification
    {
        public int NotificationId { get; set; }
        public string? UserId { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? Type { get; set; } // "Success", "Error"
        public bool IsRead { get; set; }
        public string? LinkUrl { get; set; }
        public DateTime CreatedDate { get; set; }
        // Add this for the Dapper mapping (It won't affect your database table)
        public int TotalCount { get; set; }
    }
    public class NotificationListViewModel
    {
        public List<UserNotification> Notifications { get; set; } = new List<UserNotification>();

        // Pagination Properties
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);

        // Current Filter ('All', 'Unread', 'Read')
        public string? ActiveFilter { get; set; }
    }
}