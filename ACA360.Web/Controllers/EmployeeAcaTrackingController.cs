using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Logging.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.Employer)]
    public class EmployeeAcaTrackingController : BaseController
    {
        private readonly IAcaTrackingService _trackingService;
        private readonly IExportService _exportService;
        private readonly ILoggerService _logger;

        public EmployeeAcaTrackingController(
            IAcaTrackingService trackingService,
            IExportService exportService,
            ILoggerService logger)
        {
            _trackingService = trackingService;
            _exportService = exportService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int year = 0, int months = 6)
        {
            try
            {
                if (year == 0)
                {
                    var yearString = GetCurrentFilingYear();
                    year = !string.IsNullOrEmpty(yearString) ? int.Parse(yearString) : DateTime.UtcNow.Year;
                }

                int employerId = int.Parse(GetCurrentEmployerId());

                var model = await _trackingService.GetTrackingSummaryAsync(employerId, year);
                model.LocationBreakdown = await _trackingService.GetConsentByLocationAsync(employerId, year);
                model.Trend = await _trackingService.GetConsentTrendAsync(employerId, months, year);
                model.RecentActivity = await _trackingService.GetRecentActivityAsync(employerId, 5, year);

                ViewBag.SelectedYear = year;
                ViewBag.TrendMonths = months;
                ViewBag.EmployerId = employerId;
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Index), "EmployeeAcaTrackingController",
                    $"Error loading ACA Tracking dashboard for Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading ACA Tracking.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendConsentReminder(int year)
        {
            try
            {
                int employerId = int.Parse(GetCurrentEmployerId());
                await _trackingService.SendConsentReminderAsync(employerId, year);
                return Json(new { success = true, message = "Consent reminder sent to employees without a preference." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SendConsentReminder), "EmployeeAcaTrackingController",
                    $"Error sending consent reminders for Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadConsentReport(int year, string format = "excel")
        {
            try
            {
                int employerId = int.Parse(GetCurrentEmployerId());
                var locations = await _trackingService.GetConsentByLocationAsync(employerId, year);

                return HandleExport(
                    _exportService,
                    format,
                    title: "ACA Consent Report",
                    fileName: "AcaConsentReport",
                    columns: new List<ExportColumn>
                    {
                        new ExportColumn("WorkLocation", "Work Location"),
                        new ExportColumn("TotalEmployees", "Total Employees"),
                        new ExportColumn("ElectronicCount", "Consented Electronic"),
                        new ExportColumn("PaperCount", "Paper Delivery"),
                        new ExportColumn("NoPreferenceCount", "No Preference"),
                        new ExportColumn("ConsentRate", "Consent Rate")
                    },
                    rows: locations.Select(l => new Dictionary<string, object>
                    {
                        ["WorkLocation"] = l.WorkLocation,
                        ["TotalEmployees"] = l.TotalEmployees,
                        ["ElectronicCount"] = l.ElectronicCount,
                        ["PaperCount"] = l.PaperCount,
                        ["NoPreferenceCount"] = l.NoPreferenceCount,
                        ["ConsentRate"] = $"{l.ConsentRate}%"
                    }).ToList()
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(DownloadConsentReport), "EmployeeAcaTrackingController",
                    $"Error exporting consent report for Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while exporting.");
            }
        }
    }
}