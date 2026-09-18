using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Entry and exit for View As. Note the role list excludes Employer entirely:
    /// an employer has nobody to view as, so the endpoints are not merely hidden
    /// from them, they are refused.
    /// </summary>
    [Authorize(Roles = UserRoles.InternalTeam + "," + UserRoles.Broker)]
    public class ViewAsController : BaseController
    {
        private readonly IViewAsService _viewAs;
        private readonly ILoggerService _logger;

        public ViewAsController(IViewAsService viewAs, ILoggerService logger)
        {
            _viewAs = viewAs;
            _logger = logger;
        }

        /// <summary>Targets the current user may select. Used to populate the picker.</summary>
        [HttpGet]
        public async Task<IActionResult> Options()
        {
            int.TryParse(GetCurrentUserId(), out int actorId);
            var role = User?.FindFirst(ClaimTypes.Role)?.Value ?? "";

            if (!ViewAsRules.CanViewAsAnything(role)) return Forbid();

            var opts = await _viewAs.GetOptionsAsync(actorId, role);
            return Json(new
            {
                success = true,
                canViewAsBroker = opts.CanViewAsBroker,
                canViewAsEmployer = opts.CanViewAsEmployer,
                brokers = opts.Brokers,
                employers = opts.Employers
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Begin(string mode, int targetId)
        {
            if (!Enum.TryParse<ImpersonationMode>(mode, true, out var parsed)
                || parsed == ImpersonationMode.None)
            {
                return Json(new { success = false, message = "Choose Broker or Employer." });
            }

            var ok = await _viewAs.BeginAsync(HttpContext, parsed, targetId);
            if (!ok)
            {
                _logger.LogError(
                    new UnauthorizedAccessException("View As denied"),
                    nameof(Begin), nameof(ViewAsController),
                    $"User {GetCurrentUserId()} was refused {mode} target {targetId}.");

                return Json(new
                {
                    success = false,
                    message = "You do not have access to that account, or you are already viewing as someone else."
                });
            }

            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> End(string returnUrl = null)
        {
            await _viewAs.EndAsync(HttpContext);

            // A form post navigates to whatever comes back, so a JSON body leaves the
            // user staring at {"success":true}. Answer with JSON only when asked.
            var wantsJson = Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                         || (Request.Headers["Accept"].ToString()?.Contains("application/json") ?? false);

            if (wantsJson) return Json(new { success = true });

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Dashboard", "Employer");
        }
    }
}