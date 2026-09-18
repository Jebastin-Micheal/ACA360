// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using ACA360.BusinessLogic.Interfaces;
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

        [HttpGet]
        public async Task<IActionResult> Activate(int id, bool reset = false)
        {
            try
            {
                if (id <= 0) return BadRequest("A valid employee id is required.");

                var yearString = GetCurrentFilingYear();
                int year = !string.IsNullOrEmpty(yearString) ? int.Parse(yearString) : DateTime.UtcNow.Year;

                // Pull the employee's name + work email (from the Employee join in the consent proc).
                var info = await _consentService.GetConsentAsync(id, year);

                if (string.IsNullOrWhiteSpace(info.WorkEmail))
                {
                    ViewBag.Error = "This employee has no work email on file. Add one before activating portal access.";
                    ViewBag.EmployeeName = info.EmployeeName;
                    return View();
                }

                var email = info.WorkEmail.Trim();

                // Guard: if the employee already has a working login (password set), do NOT
                // silently reset it. Show an "already active" screen with an explicit reset option.
                var existing = await _auth.GetByEmailAsync(email);
                bool alreadyActive = existing != null && existing.IsActive
                                     && !string.IsNullOrEmpty(existing.PasswordHash)
                                     && !existing.RequirePasswordReset;

                if (alreadyActive && !reset)
                {
                    ViewBag.AlreadyActive = true;
                    ViewBag.EmployeeName = info.EmployeeName;
                    ViewBag.Email = email;
                    ViewBag.LoginUrl = Url.Action("Login", "Account", new { area = "Portal" }, Request.Scheme);
                    ViewBag.ResetUrl = Url.Action("Activate", new { id, reset = true });
                    return View();
                }

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
                _logger.LogError(ex, nameof(Activate), "PortalAdminController", $"Error activating portal access for Employee {id}.");
                return StatusCode(500, "An unexpected error occurred while activating portal access.");
            }
        }
    }
}
