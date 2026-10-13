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

        /// <summary>The user's choice per event (script 070).</summary>
        public List<NotificationPreference> Preferences { get; set; } = new();
    }

    /// <summary>Whether a user gets one kind of notification (script 070). No row in the table means yes.</summary>
    public class NotificationPreference
    {
        public string EventCode { get; set; } = "";
        public bool IsEnabled { get; set; }
        /// <summary>False when the event is not for this user's role (System Health is for admins).</summary>
        public bool IsAvailable { get; set; }

        public static readonly IReadOnlyDictionary<string, (string Label, string Help)> Events = new Dictionary<string, (string, string)>
        {
            ["FileReady"] = ("A file is ready to import", "Your upload, or a file assigned to you, passed validation."),
            ["FileFailed"] = ("A file needs attention", "Validation failed, the file was rejected, or processing stopped."),
            ["FileAssigned"] = ("A file is assigned to you", "Someone routed a file to you."),
            ["ImportDone"] = ("An import finished", "Your file's data is now in the live tables."),
            ["Deadline"] = ("Filing deadlines", "14 and 7 days before each IRS and state date."),
            ["HealthErrors"] = ("Errors on System Health", "Once a day, when errors were logged (admins)."),
        };
    }
}