using ACA360.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface INotificationService
    {
        Task AddNotificationAsync(string userId, string title, string message, string type, string? linkUrl = null);
        Task<List<UserNotification>> GetUnreadNotificationsAsync(string userId);
        Task MarkAsReadAsync(int notificationId);
        // NEW 1: Pagination Method
        Task<(List<UserNotification> Notifications, int TotalCount)> GetNotificationsPaginatedAsync(string userId, string filter, int pageNumber, int pageSize);

        // NEW 2: Mark All Read
        Task MarkAllAsReadAsync(string userId);

        // Script 070: the bell, read/unread by the owner only, preferences, and the daily reminders.
        Task<(int Unread, List<UserNotification> Items)> GetRecentAsync(int userId, int top = 8);
        /// <summary>True when the notification is the user's and is now marked.</summary>
        Task<bool> SetReadAsync(int notificationId, int userId, bool isRead);
        Task<UserNotification?> GetOwnAsync(int notificationId, int userId);
        Task<List<NotificationPreference>> GetPreferencesAsync(int userId);
        Task SavePreferenceAsync(int userId, string eventCode, bool isEnabled);
        /// <summary>Hangfire, daily: deadline reminders and the System Health digest.</summary>
        Task RunDailyRemindersAsync();
    }
}