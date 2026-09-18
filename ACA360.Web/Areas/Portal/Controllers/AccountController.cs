// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Areas.Portal.Controllers
{
    /// <summary>
    /// Employee self-service portal authentication (email + password).
    /// Signs employees into the shared cookie scheme with the EmployeeSelfService role.
    /// </summary>
    [Area("Portal")]
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly IEmployeePortalAuthService _auth;
        private readonly ILoggerService _logger;

        public AccountController(IEmployeePortalAuthService auth, ILoggerService logger)
        {
            _auth = auth;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login() => View(new PortalLoginViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(PortalLoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _auth.ValidateLoginAsync(model.Email, model.Password);

            switch (result.Status)
            {
                case PortalLoginStatus.Success:
                    await SignInAsync(result.User!);
                    return RedirectToAction("Index", "TaxForms", new { area = "Portal" });

                case PortalLoginStatus.RequiresPasswordReset:
                    // Authorize the password-set step via server session (not a URL param).
                    HttpContext.Session.SetString("PortalResetEmail", model.Email.Trim());
                    return RedirectToAction(nameof(SetPassword));

                case PortalLoginStatus.InactiveAccount:
                    ModelState.AddModelError(string.Empty, "Your portal access is inactive. Please contact your administrator.");
                    return View(model);

                default:
                    ModelState.AddModelError(string.Empty, "Invalid email or password.");
                    return View(model);
            }
        }

        [HttpGet]
        public IActionResult SetPassword()
        {
            // Only reachable after a verified step (temp login or forgot-password) set this.
            var email = HttpContext.Session.GetString("PortalResetEmail");
            if (string.IsNullOrEmpty(email)) return RedirectToAction(nameof(Login));
            return View(new PortalSetPasswordViewModel { Email = email });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(PortalSetPasswordViewModel model)
        {
            // Trust the session email set by the verified step — never the posted value.
            var email = HttpContext.Session.GetString("PortalResetEmail");
            if (string.IsNullOrEmpty(email)) return RedirectToAction(nameof(Login));
            model.Email = email;

            if (!ModelState.IsValid) return View(model);

            var user = await _auth.GetByEmailAsync(email);
            if (user == null || !user.IsActive)
            {
                ModelState.AddModelError(string.Empty, "This account cannot be updated. Please contact your administrator.");
                return View(model);
            }

            await _auth.SetPasswordAsync(email, model.NewPassword);
            HttpContext.Session.Remove("PortalResetEmail");

            var refreshed = await _auth.GetByEmailAsync(email);
            await SignInAsync(refreshed!);
            return RedirectToAction("Index", "TaxForms", new { area = "Portal" });
        }

        [HttpGet]
        public IActionResult ForgotPassword() => View(new PortalForgotPasswordViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(PortalForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var verified = await _auth.VerifyIdentityAsync(model.Email, model.Last4SSN, model.DateOfBirth!.Value);
            if (!verified)
            {
                ModelState.AddModelError(string.Empty, "We couldn't verify your identity. Check your email, last 4 of your SSN, and date of birth.");
                return View(model);
            }

            // Authorize the password-set step for this email.
            HttpContext.Session.SetString("PortalResetEmail", model.Email.Trim());
            return RedirectToAction(nameof(SetPassword));
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        private async Task SignInAsync(EmployeePortalUser user)
        {
            // Ongoing model: show the latest filing year, not the year pinned at activation.
            var latest = await _auth.GetLatestFilingYearAsync();
            var year = latest ?? user.TaxYear ?? DateTime.UtcNow.Year;
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.EmployeeName ?? user.Email),
                new Claim(ClaimTypes.Role, UserRoles.EmployeeSelfService),
                new Claim("EmployeeId", user.EmployeeId.ToString()),
                new Claim("TaxYear", year.ToString()),
                new Claim("PortalEmail", user.Email)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        }
    }
}
