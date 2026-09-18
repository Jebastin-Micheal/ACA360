using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Web.Filters;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    [Authorize(Roles = UserRoles.All)]
    public class DistributionController : BaseController
    {
        private readonly IDistributionService _distService;
        private readonly IEmployerService _employerService;
        private readonly IFilingYearService _yearService;
        private readonly INotificationService _notificationService;
        private readonly IBackgroundJobClient _jobClient; private readonly IPdfService _pdfService;
        private readonly IExportService _exportService;
        private readonly ILoggerService _logger;

        public DistributionController(
            IDistributionService distService,
            IEmployerService employerService,
            IFilingYearService yearService,
            INotificationService notificationService,
            IBackgroundJobClient jobClient, IPdfService pdfService, IExportService exportService,
            ILoggerService logger)
        {
            _distService = distService;
            _employerService = employerService;
            _yearService = yearService;
            _notificationService = notificationService;
            _jobClient = jobClient; _pdfService = pdfService;
            _exportService = exportService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves and renders the Distribution Dashboard.
        /// Loads employer and year dropdowns and fetches distribution statistics for the selected employer.
        /// </summary>
        /// <param name="employerId">The ID of the selected employer.</param>
        /// <param name="year">The selected filing year.</param>
        /// <returns>A view displaying the distribution dashboard data.</returns>
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> Index(int employerId = 0, int year = 0)
        {
            try
            {
                // Default to the current year if no year is provided in the request
                if (year == 0)
                {
                    var yearString = GetCurrentFilingYear();
                    year = !string.IsNullOrEmpty(yearString) ? int.Parse(yearString) : 0;
                }
                if (employerId == 0)
                {
                    var employerIdString = GetCurrentEmployerId();
                    employerId = !string.IsNullOrEmpty(employerIdString) ? int.Parse(employerIdString) : 0;
                }

                // 1. Load Dropdowns
                var role = User.FindFirstValue(ClaimTypes.Role);
                string userId = GetCurrentUserId();

                // Fetch Employers for Dropdown (Filtered by user context/role)
                var employers = await _employerService.GetAllEmployersAsync(userId.ToString());
                ViewBag.Employers = employers.Select(e => new SelectListItem
                {
                    Value = e.Id.ToString(),
                    Text = e.Name,
                    Selected = (e.Id == employerId.ToString())
                }).ToList();

                // Fetch available filing years for dropdown
                var years = await GetAllFilingYearsAsync();
                ViewBag.Years = years.Select(y => new SelectListItem
                {
                    Value = y.FilingYear.ToString(),
                    Text = y.FilingYear.ToString(),
                    Selected = (y.FilingYear.ToString() == year.ToString())
                }).ToList();

                // 2. Fetch Data (If Employer Selected)
                var model = new DistributionDashboardViewModel();
                if (employerId > 0)
                {
                    // Initial load with default pagination
                    var pagination = new PaginationEntity { PageIndex = 1, PageSize = 10, SortColumn = 0, SortOrder = "asc" };
                    model = await _distService.GetDistributionStatsAsync(employerId, year, pagination, "all");
                }

                // Preserve user selections in the view
                ViewBag.SelectedEmployerId = employerId;
                ViewBag.SelectedYear = year;

                return View(model);
            }
            catch (Exception ex)
            {
                // Handle exceptions by logging and returning a generic 500 status to avoid leaking stack traces
                _logger.LogError(ex, nameof(Index), "DistributionController", $"An error occurred while loading the Distribution Dashboard for Employer ID {employerId} and Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the dashboard.");
            }
        }
        // NEW ACTION: Handles AJAX Grid Reloads
        [RequireEmployerScope]
        public async Task<IActionResult> DistributionListPartial(int employerId, int year, int pageIndex, string search, int sortColumn, string sortOrder, int pageSize, string statusFilter)
        {
            var pagination = new PaginationEntity
            {
                PageIndex = pageIndex,
                PageSize = pageSize,
                Search = search,
                SortColumn = sortColumn,
                SortOrder = sortOrder
            };

            var model = await _distService.GetDistributionStatsAsync(employerId, year, pagination, statusFilter);
            model.Search = search;
            model.SortColumn = sortColumn;
            model.SortOrder = sortOrder;
            model.StatusFilter = statusFilter;

            ViewBag.SelectedYear = year; // Needed for the email/download actions
            return PartialView("_DistributionListPartial", model);
        }

        /// <summary>
        /// Generates a secure link and logs the email sending process for a single employee.
        /// </summary>
        /// <param name="employeeId">The ID of the employee receiving the email.</param>
        /// <param name="year">The relevant filing year.</param>
        /// <returns>A JSON response indicating success or failure.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendSecureEmail(int employeeId, int year)
        {
            try
            {
                // Validate input parameters to ensure valid data operations
                if (employeeId <= 0 || year <= 0)
                {
                    return BadRequest(new { success = false, message = "Valid Employee ID and Year are required." });
                }

                // 1. Generate Token: Create a secure access token for the employee
                string token = await _distService.GenerateSecureLinkAsync(employeeId, year);

                // 2. Construct Link: Build the absolute URL pointing to the forms access page
                string link = Url.Action("Access", "MyForms", new { token = token }, Request.Scheme) ?? string.Empty;

                // 3. Send it, and record what actually happened. This step used to be a
                //    commented-out line: the link was generated, a log row was written,
                //    and the user was told "sent successfully" while nothing left the
                //    building (F-37). The response now reflects the real outcome.
                var delivery = await _distService.SendEmployeeFormEmailAsync(employeeId, year, link);

                if (!delivery.Success)
                {
                    _logger.LogError(
                        new InvalidOperationException(delivery.Detail ?? "Delivery failed"),
                        nameof(SendSecureEmail), "DistributionController",
                        $"1095-C notification not delivered for Employee ID {employeeId}, year {year}: {delivery.Status} - {delivery.Detail}");

                    // 200 with success:false — the caller is AJAX and shows this text.
                    return Json(new
                    {
                        success = false,
                        status = delivery.Status,
                        message = delivery.Status switch
                        {
                            "Skipped"  => "No usable email address is on file for this employee.",
                            "Disabled" => "Email sending is not configured. Set the SendGrid API key and sender address.",
                            _          => $"The email could not be sent. {delivery.Detail}"
                        }
                    });
                }

                return Json(new { success = true, message = "Secure link sent successfully." });
            }
            catch (Exception ex)
            {
                // Log the precise error and return a 500 Internal Server Error formatted as JSON
                _logger.LogError(ex, nameof(SendSecureEmail), "DistributionController", $"An error occurred while generating secure email for Employee ID {employeeId} for Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while sending the secure link." });
            }
        }

        /// <summary>
        /// Queues a background job to process mass email distribution for multiple employees.
        /// </summary>
        /// <param name="employerId">The ID of the employer.</param>
        /// <param name="year">The filing year.</param>
        /// <param name="employeeIds">A list of target employee IDs.</param>
        /// <returns>A JSON response confirming the job was queued.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope]
        public IActionResult SendBatchEmails(int employerId, int year, List<int> employeeIds)
        {
            try
            {
                // Validate inputs to prevent queuing invalid background jobs
                if (employerId <= 0 || year <= 0 || employeeIds == null || !employeeIds.Any())
                {
                    return BadRequest(new { success = false, message = "Valid Employer ID, Year, and at least one Employee ID are required." });
                }

                // Enqueue the heavy batch process via Hangfire to prevent blocking the UI thread
                _jobClient.Enqueue<IDistributionService>(service =>
                    service.ProcessBatchEmailDistributionAsync(employerId, year, employeeIds));

                return Json(new { success = true, message = "Mass email campaign has been queued." });
            }
            catch (Exception ex)
            {
                // Log queueing failures and return a 500 response
                _logger.LogError(ex, nameof(SendBatchEmails), "DistributionController", $"An error occurred while queuing batch emails for Employer ID {employerId} for Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while queuing the mass email campaign." });
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DownloadPdf(int employeeId, int year)
        {
            try
            {
                // Generate the 1095-C PDF on the fly for the verified employee and requested year.
                // Note: For B-form employers, replace with Generate1095BForEmployeeAsync accordingly.
                var pdfBytes = await _pdfService.Generate1095CForEmployeeAsync(employeeId, year);

                // Return the PDF as a downloadable attachment with a descriptive file name
                return File(pdfBytes, "application/pdf", $"My_1095C_{year}.pdf");
            }
            catch (Exception ex)
            {
                // Log the PDF generation failure; include year for correlation with filing records
                _logger.LogError(ex, nameof(DownloadPdf), "MyFormsController",
                    $"PDF generation failed for Year={year}", GetUserIp());

                return StatusCode(500, "An unexpected error occurred while generating your form. Please try again.");
            }
        }

        /// <summary>
        /// Exports the Distribution Center employee list (Print/CSV/Excel/PDF).
        /// </summary>
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> ExportDistribution(int employerId, int year, string statusFilter = "all", string search = "", string format = "excel")
        {
            try
            {
                if (employerId <= 0 || year <= 0)
                {
                    return BadRequest("Valid Employer ID and Year are required.");
                }

                var pagination = new PaginationEntity
                {
                    PageIndex = 1,
                    PageSize = int.MaxValue,
                    Search = search,
                    SortColumn = 0,
                    SortOrder = "asc"
                };

                var model = await _distService.GetDistributionStatsAsync(employerId, year, pagination, statusFilter);

                return HandleExport(
                    _exportService,
                    format,
                    title: "Distribution Center",
                    fileName: "DistributionList",
                    columns: new List<ExportColumn>
                    {
        new ExportColumn("Name",        "Employee"),
        new ExportColumn("Email",       "Email Address"),
        new ExportColumn("ConsentType", "Consent"),
        new ExportColumn("Status",      "Status"),
        new ExportColumn("SentOn",      "Sent On")
                    },
                    rows: model.Employees.Select(e => new Dictionary<string, object>
                    {
                        ["Name"] = e.Name ?? "",
                        ["Email"] = string.IsNullOrEmpty(e.Email) ? "Missing" : e.Email,
                        ["ConsentType"] = e.HasConsent ? "Electronic" : "Paper",
                        ["Status"] = e.EmailSentDate.HasValue ? "Sent" : "Pending",
                        ["SentOn"] = e.EmailSentDate.HasValue ? e.EmailSentDate.Value.ToString("dd-MMM-yyyy hh:mm tt") : ""
                    }).ToList()
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(ExportDistribution), "DistributionController",
                    $"Error exporting distribution list for Employer {employerId}, Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred while exporting the distribution list.");
            }
        }
    }
}
