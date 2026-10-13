using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Web.Models
{
    public class SettingsListViewModel
    {
        public UserProfileViewModel UserProfile { get; set; } = new UserProfileViewModel();
        public ChangePasswordViewModel ChangePassword { get; set; } = new ChangePasswordViewModel();
    }
}
