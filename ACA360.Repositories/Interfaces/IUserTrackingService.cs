using System.Threading.Tasks;
using ACA360.Core.Models;

namespace ACA360.Repositories.Interfaces
{
    public interface IUserTrackingService
    {
        // Preferences
        Task UpdateUserPreferenceAsync(string userId, int employerId, int year);
        Task<UserPreferenceModel> GetUserPreferenceAsync(string userId);

        // Activity Tracking
        Task<int> LogLoginAsync(string userId, string ip, string userAgent, string sessionId, string originalUserId = null);
        Task LogLogoutAsync(int logId);
        Task UpdateLastActiveAsync(int logId);
    }
}