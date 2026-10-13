using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IAccountService
    {
        Task<UserModel?> LoginAsync(string username, string password);
        Task<UserProfileViewModel?> GetUserProfileAsync(string userId);
        Task<bool> SetNewPasswordAsync(string username, string newPassword);
    }
}
