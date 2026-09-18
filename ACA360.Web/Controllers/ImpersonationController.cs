using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using ACA360.Web.Helpers;

namespace ACA360.Web.Controllers
{
    [Authorize]
    [Route("Impersonation")]
    public class ImpersonationController : BaseController
    {
        private readonly UserManager<UserModel> _userManager;
        private readonly SignInManager<UserModel> _signInManager;

        public ImpersonationController(
            UserManager<UserModel> userManager, 
            SignInManager<UserModel> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        /// <summary>
        /// Starts an impersonation session for the target user.
        /// Available only to SuperAdmin and Admin roles.
        /// </summary>
        [HttpPost("Start")]
        [Authorize(Roles = "SuperAdmin, Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(string userId)
        {
            // 1. Identify who the current REAL Admin is
            var currentAdminId = User.FindFirst("UserId")?.Value;

            // 2. Load the TARGET user to impersonate
            var targetUser = await _userManager.FindByIdAsync(userId);
            if (targetUser == null)
            {
                return NotFound("Target user not found.");
            }

            // 3. Construct new identity for the target user
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, targetUser.User_Name ?? "Unknown"),
                new Claim(ClaimTypes.NameIdentifier, targetUser.Ref_ID?.ToString() ?? string.Empty),
                new Claim(ClaimTypes.Role, targetUser.Role_Name ?? "User"),
                new Claim("UserId", targetUser.User_ID ?? string.Empty),
                new Claim("ProfilePicture", targetUser.Profile_Picture ?? ""),
                
                // 4. CRITICAL: Inject the claim that identifies this as an impersonated session
                new Claim("OriginalUserId", currentAdminId ?? string.Empty),
                new Claim("Profile_Name", targetUser.Profile_Name ?? "Unknown")
            };

            // Propagate permissions if they exist
            if (targetUser.Permissions != null)
            {
                foreach (var permission in targetUser.Permissions)
                {
                    if (!string.IsNullOrWhiteSpace(permission?.Controller) && !string.IsNullOrWhiteSpace(permission?.Action))
                    {
                        claims.Add(new Claim("Permission", $"{permission.Controller}:{permission.Action}"));
                    }
                }
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            // 5. Sign in as the target user
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            // Set session for employer context
            if (targetUser.Ref_ID.HasValue)
            {
                HttpContext.Session.SetString("EmployerId", targetUser.Ref_ID.Value.ToString());
            }

            return RedirectToAction("Index", "Dashboard");
        }

        /// <summary>
        /// Safely terminates the impersonation and restores the original Admin identity.
        /// </summary>
        [HttpPost("Stop")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Stop()
        {
            // 1. Identify the TRUE Admin from the claim
            var originalAdminId = User.FindFirst("OriginalUserId")?.Value;
            
            if (string.IsNullOrEmpty(originalAdminId))
            {
                return RedirectToAction("Index", "Dashboard"); // Not currently impersonating
            }

            // 2. Re-load the true Admin identity from DB
            var adminUser = await _userManager.FindByIdAsync(originalAdminId);
            if (adminUser == null)
            {
                // Safety: If admin no longer exists, force a full logout
                return RedirectToAction("Logout", "Account");
            }

            // 3. Rebuild the Admin's ORIGINAL Claims (Omitting the OriginalUserId claim)
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, adminUser.User_Name ?? "Admin"),
                new Claim(ClaimTypes.NameIdentifier, adminUser.Ref_ID?.ToString() ?? string.Empty),
                new Claim(ClaimTypes.Role, adminUser.Role_Name ?? "Admin"),
                new Claim("UserId", adminUser.User_ID ?? string.Empty),
                new Claim("ProfilePicture", adminUser.Profile_Picture ?? "")
            };

            if (adminUser.Permissions != null)
            {
                foreach (var permission in adminUser.Permissions)
                {
                    if (!string.IsNullOrWhiteSpace(permission?.Controller) && !string.IsNullOrWhiteSpace(permission?.Action))
                    {
                        claims.Add(new Claim("Permission", $"{permission.Controller}:{permission.Action}"));
                    }
                }
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            // 4. Swap the cookie back to the real Admin
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            // Clear the hijacked session state
            HttpContext.Session.Remove("EmployerId");

            return RedirectToAction("Index", "Dashboard");
        }
    }
}
