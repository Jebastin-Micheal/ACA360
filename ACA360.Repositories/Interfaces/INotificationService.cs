using ACA360.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface INotificationService
    {
        Task AddNotificationAsync(string userId, string title, string message, string type, string linkUrl = null);
        Task<List<UserNotification>> GetUnreadNotificationsAsync(string userId);
        Task MarkAsReadAsync(int notificationId);
        Task<List<UserNotification>> GetAllNotificationsAsync(string userId);
        // NEW 1: Pagination Method
        Task<(List<UserNotification> Notifications, int TotalCount)> GetNotificationsPaginatedAsync(string userId, string filter, int pageNumber, int pageSize);

        // NEW 2: Mark All Read
        Task MarkAllAsReadAsync(string userId);
    }
}