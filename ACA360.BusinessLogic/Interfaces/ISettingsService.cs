using ACA360.Core.Models;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface ISettingsService
    {
        Task<UserProfileViewModel> GetUserProfileAsync(string userId);
        Task UpdateUserProfileAsync(UserProfileViewModel model);
        Task<bool> ChangePasswordAsync(string userId, string oldPassword, string newPassword);
        Task UpdateMFAAsync(string userId, bool enable);
        Task DeactivateUserAsync(string userId, int roleId, int refId);
    }
}