// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Attributes;
using ACA360.Core.Constants;
using ACA360.Logging.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Internal admin action to activate an employee's self-service portal login.
    /// (Hard-coded delivery for now: the generated temp password is shown to the admin
    /// to relay to the employee — email delivery is a later enhancement.)
    /// </summary>
    [Authorize(Roles = UserRoles.PortalActivators)]
    public class PortalAdminController : BaseController
    {
        private readonly IEmployeePortalAuthService _auth;
        private readonly IConsentService _consentService;
        private readonly ILoggerService _logger;

        public PortalAdminController(IEmployeePortalAuthService auth, IConsentService consentService, ILoggerService logger)
        {
            _auth = auth;
            _consentService = consentService;
            _logger = logger;
        }

        /// <summary>
        /// Shows the employee and a confirm button: "Activate" for a new or still-pending login,
        /// "Reset password" when the employee already signs in with their own password. Opening
        /// the page changes nothing; the change is the POST below (so a link or image elsewhere
        /// cannot reset someone's password).
        /// </summary>
        [HttpGet]
        [AuthorizePermission("Edit")]
        public async Task<IActionResult> Activate(int id)
        {
            try
            {
                if (id <= 0) return BadRequest("A valid employee id is required.");
                if (await CheckEmployeeAccessAsync(id, asJson: false) is { } denied) return denied;

                var (info, email, alreadyActive, _) = await LoadEmployeeAsync(id);
                ViewBag.EmployeeId = id;
                ViewBag.EmployeeName = info.EmployeeName;
                if (email == null)
                {
                    ViewBag.Error = "This employee has no work email on file. Add one before activating portal access.";
                    return View();
                }

                ViewBag.Email = email;
                ViewBag.LoginUrl = Url.Action("Login", "Account", new { area = "Portal" }, Request.Scheme);
                if (alreadyActive) ViewBag.AlreadyActive = true;
                else ViewBag.ConfirmActivate = true;
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Activate), "PortalAdminController", $"Error loading portal access for Employee {id}.");
                return StatusCode(500, "An unexpected error occurred while loading portal access.");
            }
        }

        /// <summary>
        /// Activates the employee's portal login, or resets its password when reset is set, and
        /// shows the temporary password once. An already-active login is only reset when asked.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Edit")]
        [ActionName("Activate")]
        public async Task<IActionResult> ActivateConfirmed(int id, bool reset = false)
        {
            try
            {
                if (id <= 0) return BadRequest("A valid employee id is required.");

                // Sets (or resets) the employee's portal password and shows it on screen,
                // so only for an employee of an employer this user can reach.
                if (await CheckEmployeeAccessAsync(id, asJson: false) is { } denied) return denied;

                var (info, email, alreadyActive, year) = await LoadEmployeeAsync(id);
                if (email == null) return RedirectToAction(nameof(Activate), new { id });
                if (alreadyActive && !reset) return RedirectToAction(nameof(Activate), new { id });

                // First-time activation, still-pending temp state, or an explicit reset.
                var tempPassword = await _auth.ActivateAsync(id, year, email, User?.Identity?.Name ?? "System");

                ViewBag.EmployeeName = info.EmployeeName;
                ViewBag.Email = email;
                ViewBag.TempPassword = tempPassword;
                ViewBag.WasReset = reset;
                ViewBag.LoginUrl = Url.Action("Login", "Account", new { area = "Portal" }, Request.Scheme);
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(ActivateConfirmed), "PortalAdminController", $"Error activating portal access for Employee {id}.");
                return StatusCode(500, "An unexpected error occurred while activating portal access.");
            }
        }

        /// <summary>
        /// The employee's name and work email (null when there is none), whether they already
        /// sign in with their own password, and the plan year.
        /// </summary>
        private async Task<(ACA360.Core.Models.EmployeeConsentViewModel info, string? email, bool alreadyActive, int year)> LoadEmployeeAsync(int id)
        {
            var yearString = GetCurrentFilingYear();
            int year = !string.IsNullOrEmpty(yearString) ? int.Parse(yearString) : DateTime.UtcNow.Year;

            // Name + work email come from the Employee join in the consent proc. carryForward:
            // false - only read-only contact fields are needed, so no standing-consent
            // carry-forward write as a side effect of opening this screen.
            var info = await _consentService.GetConsentAsync(id, year, carryForward: false);
            if (string.IsNullOrWhiteSpace(info.WorkEmail)) return (info, null, false, year);

            var email = info.WorkEmail.Trim();
            var existing = await _auth.GetByEmailAsync(email);
            bool alreadyActive = existing != null && existing.IsActive
                                 && !string.IsNullOrEmpty(existing.PasswordHash)
                                 && !existing.RequirePasswordReset;
            return (info, email, alreadyActive, year);
        }
    }
}
