using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.BusinessLogic.Validators;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories;
using ACA360.Security.Interfaces;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class SettingsController : BaseController
    {
        private readonly ISettingsService _settingsService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILoggerService _logger;
        private readonly IEncryptionService _encrypt;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private static readonly ConcurrentDictionary<string, bool> _passwordChangeLocks = new();
        private readonly ISystemSettingsService _systemSettingsService;

        public SettingsController(ISettingsService settingsService, IHttpContextAccessor httpContextAccessor, ILoggerService logger, IEncryptionService encrypt, IWebHostEnvironment webHostEnvironment, ISystemSettingsService systemSettingsService)
        {
            _settingsService = settingsService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _encrypt = encrypt;
            _webHostEnvironment = webHostEnvironment;
            _systemSettingsService = systemSettingsService;
        }

        /// <summary>
        /// Retrieves and displays the user settings dashboard, supporting different tabs like account or security.
        /// </summary>
        /// <param name="tab">The active tab to display, defaults to 'account'.</param>
        /// <returns>A view presenting the user profile and change password forms.</returns>
        [HttpGet]
        public async Task<IActionResult> Index(string tab = "account")
        {
            try
            {
                // Retrieve the current user's unique identifier
                string userId = GetCurrentUserId();

                // Fetch the user's profile information from the database
                var userProfile = await _settingsService.GetUserProfileAsync(userId);

                // Ensure the user exists, otherwise redirect them to login
                if (userProfile == null)
                {
                    return RedirectToAction("Login", "Account");
                }
                string activeTab = TempData["ActiveTab"]?.ToString() ?? tab;
                // Configure the active tab and initialize password change model
                userProfile.ActiveTab = string.IsNullOrEmpty(activeTab) ? "account" : activeTab;
                userProfile.PasswordModel = new ChangePasswordViewModel();

                ViewBag.PasswordPolicy = await _systemSettingsService.GetSecuritySettingsAsync();

                // Wrap the data in the settings list view model
                var model = new SettingsListViewModel
                {
                    UserProfile = userProfile,
                    ChangePassword = new ChangePasswordViewModel()
                };

                return View(model);
            }
            catch (Exception ex)
            {
                // Log the failure to load the settings profile, capturing the user's IP
                _logger.LogError(ex, nameof(Index), nameof(AccountController), "Error loading profile", GetUserIp());

                // Return a generic 500 Internal Server Error
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading settings.");
            }
        }

        /// <summary>
        /// Processes updates to the user's profile, including profile picture uploads.
        /// </summary>
        /// <param name="model">The submitted settings model containing updated user data.</param>
        /// <returns>A redirection to the settings index on success, or an error status.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(SettingsListViewModel model)
        {
            try
            {
                // Extract the profile payload
                var profile = model?.UserProfile;
                if (profile == null)
                {
                    return BadRequest("Invalid profile data submitted.");
                }

                // Security Check: Verify that the user is updating their own profile
                string currentUserId = GetCurrentUserId();
                if (profile.UserId != currentUserId)
                {
                    return Forbid();
                }

                // Handle optional profile picture upload
                if (profile.ProfileImage != null && profile.ProfileImage.Length > 0)
                {
                    // Ensure the target upload directory exists
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "profile-pictures");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    // Generate a unique filename to prevent collisions and save the file
                    string uniqueFileName = Guid.NewGuid() + "_" + profile.ProfileImage.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await profile.ProfileImage.CopyToAsync(fileStream);
                    }

                    // Update the model with the new image's relative path
                    profile.ProfilePicture = "/uploads/profile-pictures/" + uniqueFileName;
                }
                else
                {
                    // If no new image was uploaded, retain the existing profile picture
                    var existingUser = await _settingsService.GetUserProfileAsync(currentUserId);
                    profile.ProfilePicture = existingUser.ProfilePicture;
                }

                // Persist the updated profile details to the underlying database
                await _settingsService.UpdateUserProfileAsync(profile);

                TempData["SuccessMessage"] = "Profile updated successfully.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                // Log the profile update error including context
                _logger.LogError(ex, nameof(UpdateProfile), nameof(AccountController), "Error updating profile", GetUserIp());

                // Return a 500 error status to properly indicate server-side failure
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while updating the profile.");
            }
        }

        /// <summary>
        /// Processes a request to change the user's account password.
        /// </summary>
        /// <param name="model">The submitted settings model containing the new password data.</param>
        /// <returns>A redirection on success, or re-renders the view with validation messages.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(SettingsListViewModel model)
        {
            try
            {
                string userId = GetCurrentUserId();
                var passwordModel = model?.ChangePassword;
                if (passwordModel == null)
                    return BadRequest("Password data is missing.");

                if (!_passwordChangeLocks.TryAdd(userId, true))
                    return new EmptyResult();

                try
                {
                    foreach (var key in ModelState.Keys.Where(k =>
                        k.StartsWith("UserProfile", StringComparison.OrdinalIgnoreCase)).ToList())
                    {
                        ModelState.Remove(key);
                    }

                    // ★ fetch policy + validate new password against it
                    var policy = await _systemSettingsService.GetSecuritySettingsAsync();
                    var policyErrors = PasswordPolicyValidator.Validate(passwordModel.NewPassword, policy);

                    if (!ModelState.IsValid || policyErrors.Any())
                    {
                        foreach (var err in policyErrors)
                            ModelState.AddModelError("ChangePassword.NewPassword", err);

                        var profile = await _settingsService.GetUserProfileAsync(userId);
                        var wrapper = new SettingsListViewModel
                        {
                            UserProfile = profile,
                            ChangePassword = passwordModel
                        };
                        wrapper.UserProfile.ActiveTab = "security";
                        ViewBag.PasswordPolicy = policy;
                        return View("Index", wrapper);
                    }

                    bool result = await _settingsService
                        .ChangePasswordAsync(userId, passwordModel.CurrentPassword, passwordModel.NewPassword);

                    if (result)
                        TempData["SuccessMessage"] = "Password changed successfully.";
                    else
                        TempData["ErrorMessage"] = "Current password is incorrect.";

                    TempData["ActiveTab"] = "security";
                    return RedirectToAction(nameof(Index));
                }
                finally
                {
                    _passwordChangeLocks.TryRemove(userId, out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(ChangePassword), nameof(AccountController), "Error changing password", GetUserIp());
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while processing the password change.");
            }
        }


        /// <summary>
        /// Deactivates the user's account and terminates their current session.
        /// </summary>
        /// <param name="accountActivation">Boolean flag indicating explicit confirmation to deactivate.</param>
        /// <returns>A redirection to the login page on success, or back to the settings index otherwise.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateAccount(bool accountActivation)
        {
            try
            {
                // Validate that the user deliberately checked the confirmation flag
                if (!accountActivation)
                {
                    TempData["ErrorMessage"] = "You must confirm deactivation by checking the box.";
                    return RedirectToAction("Index");
                }

                string userId = GetCurrentUserId();
                var userProfile = await _settingsService.GetUserProfileAsync(userId);

                if (userProfile != null)
                {
                    // Execute the deactivation logic within the backend service
                    await _settingsService.DeactivateUserAsync(userId, userProfile.RoleId, userProfile.RefId);

                    // Log the critical security event 
                    _logger.LogInfo("Account Deactivated", nameof(AccountController), $"User {userProfile.UserName} deactivated their account.", GetUserIp());

                    // Terminate the user's active session and redirect them out
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    HttpContext.Session.Clear();

                    return RedirectToAction("Login", "Account");
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                // Log the account deactivation failure
                _logger.LogError(ex, nameof(DeactivateAccount), nameof(AccountController), "Error deactivating account", GetUserIp());

                // Return 500 internal server error
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while attempting to deactivate the account.");
            }
        }

        /// <summary>
        /// Toggles Multi-Factor Authentication (MFA) on or off for the user.
        /// </summary>
        /// <param name="enable">Boolean representing the desired MFA state.</param>
        /// <returns>A redirection to the security settings tab.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleMFA(bool enable)
        {
            try
            {
                string userId = GetCurrentUserId();

                // F-01: enabling is refused until a second factor actually exists.
                // Setting the flag used to lock the account out permanently — login
                // redirected to Account/MFA, which is not an action anywhere in the
                // solution — and the user could no longer reach this page to undo it.
                // Disabling stays available so anyone already flagged can clear it.
                if (enable)
                {
                    TempData["ErrorMessage"] =
                        "Multi-factor authentication is not available yet. It cannot be switched on " +
                        "until the verification step is built, because doing so would prevent you " +
                        "from signing in.";
                    return RedirectToAction("Index", new { tab = "security" });
                }

                await _settingsService.UpdateMFAAsync(userId, enable);

                TempData["SuccessMessage"] = "MFA has been disabled.";
                return RedirectToAction("Index", new { tab = "security" });
            }
            catch (Exception ex)
            {
                // Log the MFA toggle failure
                _logger.LogError(ex, nameof(ToggleMFA), nameof(AccountController), "Error updating MFA", GetUserIp());

                // Return 500 error code
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while toggling MFA.");
            }
        }
    }
}