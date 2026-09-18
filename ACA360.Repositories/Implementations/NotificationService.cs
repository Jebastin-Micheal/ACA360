using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class NotificationService : INotificationService
    {
        private readonly string _connectionString;
        private readonly ILogger<NotificationService> _logger;

        /// <summary>
        /// Purpose: Initializes the NotificationService with connection string and logger dependencies.
        /// Input parameters: string connectionString, ILogger logger
        /// Output/return value: None
        /// </summary>
        public NotificationService(string connectionString, ILogger<NotificationService> logger)
        {
            try
            {
                _connectionString = connectionString;
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "Error initializing NotificationService.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Adds a new notification for a specific user.
        /// Input parameters: string userId, string title, string message, string type, string linkUrl
        /// Output/return value: Task
        /// </summary>
        public async Task AddNotificationAsync(string userId, string title, string message, string type, string linkUrl = null)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_AddNotification",
                    new { UserId = userId, Title = title, Message = message, Type = type, LinkUrl = linkUrl },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in AddNotificationAsync for UserId: {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves all unread notifications for a specific user.
        /// Input parameters: string userId
        /// Output/return value: Task of List of UserNotification
        /// </summary>
        public async Task<List<UserNotification>> GetUnreadNotificationsAsync(string userId)
        {
            try
            {
                using var db = Connection;
                return (await db.QueryAsync<UserNotification>(
                    "sp_GetUnreadNotifications",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetUnreadNotificationsAsync for UserId: {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Marks a specific notification as read.
        /// Input parameters: int notificationId
        /// Output/return value: Task
        /// </summary>
        public async Task MarkAsReadAsync(int notificationId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_MarkNotificationAsRead",
                    new { Id = notificationId },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in MarkAsReadAsync for NotificationId: {NotificationId}", notificationId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves the latest 50 notifications for a user's history page.
        /// Input parameters: string userId
        /// Output/return value: Task of List of UserNotification
        /// </summary>
        public async Task<List<UserNotification>> GetAllNotificationsAsync(string userId)
        {
            try
            {
                using var db = Connection;
                var result = await db.QueryAsync<UserNotification>(
                    "sp_GetAllNotifications",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure);

                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetAllNotificationsAsync for UserId: {UserId}", userId);
                throw;
            }
        }

        // NEW IMPLEMENTATION 1
        /// <summary>
        /// Purpose: Retrieves a paginated list of user notifications based on a filter.
        /// Input parameters: string userId, string filter, int pageNumber, int pageSize
        /// Output/return value: Task of (List of UserNotification Notifications, int TotalCount)
        /// </summary>
        public async Task<(List<UserNotification> Notifications, int TotalCount)> GetNotificationsPaginatedAsync(string userId, string filter, int pageNumber, int pageSize)
        {
            try
            {
                using var db = Connection;

                using var multi = await db.QueryMultipleAsync(
                    "sp_GetUserNotificationsPaginated",
                    new { UserId = userId, FilterType = filter, PageNumber = pageNumber, PageSize = pageSize },
                    commandType: CommandType.StoredProcedure
                );

                var list = (await multi.ReadAsync<UserNotification>()).ToList();

                int totalCount = list.Any() ? list.First().TotalCount : 0;

                return (list, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetNotificationsPaginatedAsync for UserId: {UserId}", userId);
                throw;
            }
        }

        // NEW IMPLEMENTATION 2
        /// <summary>
        /// Purpose: Marks all notifications as read for a specific user.
        /// Input parameters: string userId
        /// Output/return value: Task
        /// </summary>
        public async Task MarkAllAsReadAsync(string userId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_MarkAllNotificationsRead",
                    new { UserId = userId },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure
                );

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in MarkAllAsReadAsync for UserId: {UserId}", userId);
                throw;
            }
        }
    }
}