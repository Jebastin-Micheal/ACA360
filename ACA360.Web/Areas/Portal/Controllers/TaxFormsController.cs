// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Web.Areas.Portal.Controllers
{
    /// <summary>
    /// Employee "My Tax Forms" page — delivery preference + consent, backed by ConsentService.
    /// </summary>
    [Area("Portal")]
    [Authorize(Roles = UserRoles.EmployeeSelfService)]
    public class TaxFormsController : Controller
    {
        private readonly IConsentService _consentService;
        private readonly IPdfService _pdfService;
        private readonly ILoggerService _logger;

        public TaxFormsController(IConsentService consentService, IPdfService pdfService, ILoggerService logger)
        {
            _consentService = consentService;
            _pdfService = pdfService;
            _logger = logger;
        }

        private int EmployeeId => int.TryParse(User.FindFirst("EmployeeId")?.Value, out var v) ? v : 0;
        private int LatestYear => int.TryParse(User.FindFirst("TaxYear")?.Value, out var v) ? v : DateTime.UtcNow.Year;

        /// <summary>Builds the year tabs (years with a form, plus the current filing year), newest first.</summary>
        private async Task<List<int>> GetYearTabsAsync()
        {
            var formYears = await _consentService.GetAvailableFormYearsAsync(EmployeeId);
            var tabs = new List<int>(formYears)
            {
                LatestYear,       // current filing year
                LatestYear - 1    // previous year
            };
            tabs = tabs.Where(y => y > 0).Distinct().OrderByDescending(y => y).ToList();
            if (tabs.Count == 0) tabs.Add(LatestYear);
            return tabs;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? year)
        {
            try
            {
                if (EmployeeId <= 0) return RedirectToAction("Login", "Account", new { area = "Portal" });

                var tabs = await GetYearTabsAsync();
                int selectedYear = (year.HasValue && tabs.Contains(year.Value)) ? year.Value : tabs.First();

                var model = await _consentService.GetConsentAsync(EmployeeId, selectedYear);
                var formYears = await _consentService.GetAvailableFormYearsAsync(EmployeeId);

                ViewBag.SelectedYear = selectedYear;
                ViewBag.AvailableYears = tabs;
                ViewBag.FormsCount = formYears.Count;
                ViewBag.FormAvailable = await _consentService.IsFormAvailableAsync(EmployeeId, selectedYear);
                ViewBag.FormType = await _consentService.GetFormTypeAsync(EmployeeId, selectedYear);
                ViewBag.NotifyRequested = await _consentService.IsNotifyRequestedAsync(EmployeeId, selectedYear);
                ViewBag.IsCurrentYear = selectedYear >= LatestYear;   // only the current year is editable

                if (TempData["Saved"] != null) ViewBag.Saved = true;
                if (TempData["Notify"] != null) ViewBag.NotifyMessage = TempData["Notify"];
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Index), "Portal.TaxFormsController", "Error loading My Tax Forms.");
                return StatusCode(500, "An unexpected error occurred while loading your tax form preferences.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePreference(EmployeeConsentViewModel model)
        {
            try
            {
                if (EmployeeId <= 0) return RedirectToAction("Login", "Account", new { area = "Portal" });

                int year = model.TaxYear > 0 ? model.TaxYear : LatestYear;
                model.EmployeeId = EmployeeId;
                model.TaxYear = year;

                // Only the current filing year's preference can be changed.
                if (year < LatestYear)
                    return RedirectToAction(nameof(Index), new { year });

                bool isElectronic = string.Equals(model.DeliveryPreference, "Electronic", StringComparison.OrdinalIgnoreCase);
                if (isElectronic && !model.AgreeToConsent)
                    ModelState.AddModelError(nameof(model.AgreeToConsent), "You must consent to receive your ACA tax forms electronically.");
                if (isElectronic && string.IsNullOrWhiteSpace(model.NotificationEmail))
                    ModelState.AddModelError(nameof(model.NotificationEmail), "A notification email is required for electronic delivery.");

                if (!ModelState.IsValid)
                {
                    var reloaded = await _consentService.GetConsentAsync(EmployeeId, year);
                    model.EmployeeName = reloaded.EmployeeName;
                    model.WorkEmail = reloaded.WorkEmail;
                    model.History = reloaded.History;
                    ViewBag.SelectedYear = year;
                    ViewBag.AvailableYears = await GetYearTabsAsync();
                    ViewBag.FormAvailable = await _consentService.IsFormAvailableAsync(EmployeeId, year);
                    ViewBag.FormType = await _consentService.GetFormTypeAsync(EmployeeId, year);
                    return View("Index", model);
                }

                string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                string ua = ACA360.Web.Helpers.UserAgentHelper.Friendly(Request.Headers["User-Agent"].ToString());
                await _consentService.SaveConsentAsync(model, ip, ua);

                TempData["Saved"] = true;
                return RedirectToAction(nameof(Index), new { year });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SavePreference), "Portal.TaxFormsController", "Error saving preference.");
                return StatusCode(500, "An unexpected error occurred while saving your preference.");
            }
        }

        /// <summary>POST: Logs that the employee opened the electronic delivery disclosure (truthful audit).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkDisclosureViewed(int? year)
        {
            if (EmployeeId <= 0) return Unauthorized();
            await _consentService.LogEventAsync(EmployeeId, year ?? LatestYear,
                "Disclosure Viewed", "Employee viewed electronic delivery disclosure.");
            return Ok();
        }

        /// <summary>POST: Registers the employee to be notified when the form for a year is ready.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NotifyWhenReady(int year)
        {
            try
            {
                if (EmployeeId <= 0) return RedirectToAction("Login", "Account", new { area = "Portal" });

                await _consentService.RequestFormNotificationAsync(EmployeeId, year > 0 ? year : LatestYear);
                TempData["Notify"] = "You'll be notified by email as soon as your form is ready.";
                return RedirectToAction(nameof(Index), new { year });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(NotifyWhenReady), "Portal.TaxFormsController", "Error requesting form notification.");
                return StatusCode(500, "An unexpected error occurred. Please try again.");
            }
        }

        /// <summary>GET: Streams the employee's ACA form PDF for the selected tax year.</summary>
        [HttpGet]
        public async Task<IActionResult> Download(int? year)
        {
            int downloadYear = year ?? LatestYear;
            try
            {
                if (EmployeeId <= 0) return RedirectToAction("Login", "Account", new { area = "Portal" });

                if (!await _consentService.IsFormAvailableAsync(EmployeeId, downloadYear))
                    return NotFound("Your ACA form is not available yet for this tax year.");

                // Electronic download requires active electronic consent; paper is delivered by mail.
                var consent = await _consentService.GetConsentAsync(EmployeeId, downloadYear);
                if (!consent.IsElectronicActive)
                    return NotFound("This form is delivered by mail. Choose electronic delivery to download it here.");

                var formType = await _consentService.GetFormTypeAsync(EmployeeId, downloadYear);
                byte[] pdfBytes = string.Equals(formType, "1095-B", StringComparison.OrdinalIgnoreCase)
                    ? await _pdfService.Generate1095BForEmployeeAsync(EmployeeId, downloadYear)
                    : await _pdfService.Generate1095CForEmployeeAsync(EmployeeId, downloadYear);

                return File(pdfBytes, "application/pdf", $"My_{formType}_{downloadYear}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Download), "Portal.TaxFormsController", $"Form download failed for Year {downloadYear}.");
                return StatusCode(500, "An unexpected error occurred while generating your form. Please try again.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Withdraw(int? year)
        {
            int withdrawYear = year ?? LatestYear;
            try
            {
                if (EmployeeId <= 0) return RedirectToAction("Login", "Account", new { area = "Portal" });

                // Only the current filing year's consent can be withdrawn.
                if (withdrawYear < LatestYear)
                    return RedirectToAction(nameof(Index), new { year = withdrawYear });

                await _consentService.WithdrawAsync(EmployeeId, withdrawYear);
                TempData["Saved"] = true;
                return RedirectToAction(nameof(Index), new { year = withdrawYear });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Withdraw), "Portal.TaxFormsController", "Error withdrawing consent.");
                return StatusCode(500, "An unexpected error occurred while withdrawing your consent.");
            }
        }
    }
}
