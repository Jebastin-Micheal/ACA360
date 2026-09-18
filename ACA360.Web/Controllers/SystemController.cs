using ACA360.Core.Attributes;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Web.Models;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using ACA360.Repositories;
using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class SystemController : Controller
    {
        private readonly ICacheService _cacheService;
        private readonly ILoggerService _logger;
        private readonly ISystemSettingsService _systemSettings;
        private readonly IAutomationService _automationservice;
        private readonly ISystemService _systemService;

        public SystemController(
            ICacheService cacheService,
            ILoggerService logger,
            ISystemSettingsService systemSettings,
            IAutomationService automationservice,
            ISystemService systemService)
        {
            _cacheService = cacheService;
            _logger = logger;
            _systemSettings = systemSettings;
            _automationservice = automationservice;
            _systemService = systemService;
        }

        /// <summary>
        /// Retrieves the main System Settings dashboard.
        /// Loads all configuration categories to populate the UI tabs.
        /// </summary>
        /// <returns>A view presenting the system settings model.</returns>
        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                // Fetch system-wide settings across various categories
                var model = new SystemSettingsViewModel
                {
                    MaintenanceMode = await _systemSettings.GetMaintenanceModeAsync(),
                    Security = await _systemSettings.GetSecuritySettingsAsync(),
                    Database = await _systemSettings.GetDatabaseSettingsAsync(),
                    Automation = await _automationservice.GetJobsAsync(),
                    General = await _systemSettings.GetGeneralSettingsAsync(),
                    Smtp = await _systemSettings.GetSmtpSettingsAsync(),
                    Irs = await _systemSettings.GetIrsSettingsAsync(),
                    AuditLogs = await _systemSettings.GetAuditLogsAsync(50), // Retrieve the 50 most recent logs
                    IsApiEnabled = await _systemSettings.GetBoolSettingAsync("IsApiEnabled")
                };

                return View(model);
            }
            catch (Exception ex)
            {
                // Log the failure to load settings with the user's IP for tracking
                _logger.LogError(ex, nameof(Index), nameof(SystemController), "Error loading settings", HttpContext?.Connection?.RemoteIpAddress?.ToString());

                // Return a generic 500 Internal Server Error status
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading system settings.");
            }
        }

        /// <summary>
        /// Renders the static maintenance mode view.
        /// </summary>
        /// <returns>The maintenance view.</returns>
        [HttpGet]
        public IActionResult Maintenance()
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                // Log unexpected errors during view rendering
                _logger.LogError(ex, nameof(Maintenance), nameof(SystemController), "Error loading maintenance view");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        /// <summary>
        /// Clears the application-wide cache.
        /// </summary>
        /// <returns>A JSON response indicating success or failure.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ClearCache()
        {
            try
            {
                // Execute the cache clearing mechanism
                _cacheService.ClearAll();
                return Json(new { success = true, message = "Cache cleared successfully!" });
            }
            catch (Exception ex)
            {
                // Log cache clearing failures securely
                _logger.LogError(ex, nameof(ClearCache), nameof(SystemController), "Error clearing cache");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while clearing the cache." });
            }
        }

        /// <summary>
        /// Executes a system health check.
        /// </summary>
        /// <returns>A JSON payload containing the health check results.</returns>
        [HttpGet]
        public async Task<IActionResult> HealthCheck()
        {
            try
            {
                var result = await _systemService.RunHealthCheckAsync();
                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(HealthCheck), nameof(SystemController), "Error executing health check");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while running the health check." });
            }
        }

        /// <summary>
        /// Retrieves the current maintenance mode status.
        /// </summary>
        /// <returns>A JSON response with the maintenance mode boolean.</returns>
        [HttpGet]
        public async Task<IActionResult> CheckMode()
        {
            try
            {
                // Fetch current maintenance setting
                var maintenance = await _systemSettings.GetMaintenanceModeAsync();
                return Json(new { maintenanceMode = maintenance });
            }
            catch (Exception ex)
            {
                // Log state retrieval failure
                _logger.LogError(ex, nameof(CheckMode), nameof(SystemController), "Error checking maintenance mode");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while checking maintenance mode." });
            }
        }

        /// <summary>
        /// Retrieves a recent snapshot of raw audit logs.
        /// </summary>
        /// <returns>A JSON array of recent log entries.</returns>
        [HttpGet]
        public async Task<IActionResult> AuditLogs()
        {
            try
            {
                // Fetch recent logs using dynamic dispatch on the logger implementation
                var logs = await (_logger as dynamic).GetLastLogsAsync() ?? new List<object>();
                return Json(logs);
            }
            catch (Exception ex)
            {
                // Log the failure to fetch audit entries
                _logger.LogError(ex, nameof(AuditLogs), nameof(SystemController), "Error fetching audit logs");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while fetching audit logs." });
            }
        }

        /// <summary>
        /// Toggles the application's maintenance mode on or off.
        /// </summary>
        /// <param name="enabled">The desired maintenance state.</param>
        /// <returns>A JSON response confirming the state change.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleMaintenanceMode(bool enabled)
        {
            try
            {
                // Apply the maintenance setting change to the data store
                await _systemSettings.SetMaintenanceModeAsync(enabled);
                return Json(new { success = true, message = $"Maintenance Mode {(enabled ? "Enabled" : "Disabled")}" });
            }
            catch (Exception ex)
            {
                // Log toggle failure
                _logger.LogError(ex, nameof(ToggleMaintenanceMode), nameof(SystemController), $"Error toggling maintenance mode to {enabled}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while toggling maintenance mode." });
            }
        }

        /// <summary>
        /// Triggers a restart of the Hangfire background job processor.
        /// </summary>
        /// <returns>A JSON response confirming the restart command.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RestartHangfire()
        {
            try
            {
                // Enqueue a marker job to record the restart request
                BackgroundJob.Enqueue(() => _logger.LogError(
                    new Exception("Hangfire Restart Triggered"), nameof(SystemController), "Maintenance", "Restart Request", nameof(SystemController)));

                // Terminate the active connection to force a recycle of the worker processes
                Hangfire.JobStorage.Current.GetConnection().Dispose();

                return Json(new { success = true, message = "Hangfire background processes restarted successfully." });
            }
            catch (Exception ex)
            {
                // Log failures to restart the scheduler
                _logger.LogError(ex, nameof(RestartHangfire), nameof(SystemController), "Error restarting Hangfire");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while restarting Hangfire." });
            }
        }

        /// <summary>
        /// Loads the advanced log viewer UI.
        /// </summary>
        /// <returns>The log viewer view.</returns>
        [HttpGet]
        public IActionResult Logs()
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                // Log unexpected errors during view rendering
                _logger.LogError(ex, nameof(Logs), nameof(SystemController), "Error loading log viewer");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        /// <summary>
        /// Retrieves paginated system logs based on advanced filter criteria.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLogs(
            int page = 1,
            int pageSize = 25,
            string? search = null,
            string? level = null,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            try
            {
                // Fetch the filtered logs through the logging service
                var result = await _logger.GetLogsAsync(page, pageSize, search, level, startDate, endDate);
                return Json(result);
            }
            catch (Exception ex)
            {
                // Gracefully fail AJAX data retrieval
                _logger.LogError(ex, nameof(GetLogs), nameof(SystemController), "Error querying logs");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while querying logs." });
            }
        }

        /// <summary>
        /// Retrieves aggregated metrics and trend data for the logging dashboard charts.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLogStats()
        {
            try
            {
                // Retrieve structured analytics from the logger
                var stats = await _logger.GetLogStatsAsync();

                // Project the data into chart-compatible arrays
                return Json(new
                {
                    levelStats = stats.Levels.Select(x => new { label = x.Label, value = x.Value }),
                    trendStats = stats.Trend.Select(x => new { label = x.Label, value = x.Value })
                });
            }
            catch (Exception ex)
            {
                // Handle metrics aggregation failures
                _logger.LogError(ex, nameof(GetLogStats), nameof(SystemController), "Error calculating log stats");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while fetching log statistics." });
            }
        }

        /// <summary>
        /// Deletes historical logs exceeding the specified retention period.
        /// </summary>
        /// <param name="days">The retention duration in days.</param>
        /// <returns>A JSON response confirming the purge.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CleanupOldLogs(int days)
        {
            try
            {
                // Ensure a valid timeframe is provided
                if (days < 0)
                {
                    return BadRequest(new { success = false, message = "Retention days cannot be negative." });
                }

                // Execute the log cleanup process
                await _logger.CleanupOldLogsAsync(days);

                return Json(new
                {
                    success = true,
                    message = $"Logs older than {days} days were removed successfully."
                });
            }
            catch (Exception ex)
            {
                // Handle cleanup database errors securely
                _logger.LogError(ex, nameof(CleanupOldLogs), nameof(SystemController), $"Error cleaning logs older than {days} days");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while cleaning up old logs." });
            }
        }

        /// <summary>
        /// Saves global security configuration settings.
        /// </summary>
        /// <param name="model">The updated security settings payload.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSecuritySettings([FromBody] SecuritySettingsDto model)
        {
            try
            {
                // Apply data validation attributes defined on the DTO
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Persist updates
                await _systemSettings.SaveSecuritySettingsAsync(model);

                return Json(new { success = true, message = "Security settings updated successfully." });
            }
            catch (Exception ex)
            {
                // Handle save failures
                _logger.LogError(ex, nameof(SaveSecuritySettings), nameof(SystemController), "Error saving security settings");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while saving security settings." });
            }
        }

        /// <summary>
        /// Saves global database configuration settings.
        /// </summary>
        /// <param name="model">The updated database settings payload.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDatabaseSettings([FromBody] DatabaseSettingsDto model)
        {
            try
            {
                // Enforce structural validity of the settings payload
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Extract user ID from current claims context for auditing the change
                int? userId = null;
                if (int.TryParse(User.FindFirst("UserId")?.Value, out var uid))
                {
                    userId = uid;
                }

                // Execute the save operation
                await _systemSettings.SaveDatabaseSettingsAsync(model, userId);

                return Json(new { success = true, message = "Database settings saved successfully." });
            }
            catch (Exception ex)
            {
                // Log issues updating the database configuration
                _logger.LogError(ex, nameof(SaveDatabaseSettings), nameof(SystemController), "Error saving database settings");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while saving database settings." });
            }
        }

        /// <summary>
        /// Triggers an immediate backup of the application database.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunBackup()
        {
            try
            {
                // Attempt to trace the requesting user
                int? userId = null;
                if (int.TryParse(User.FindFirst("UserId")?.Value, out var uid))
                {
                    userId = uid;
                }

                // Request the backup process
                var (success, message) = await _systemSettings.RunDatabaseBackupAsync(userId);

                return Json(new { success, message });
            }
            catch (Exception ex)
            {
                // Handle backup execution errors
                _logger.LogError(ex, nameof(RunBackup), nameof(SystemController), "Error running database backup");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while executing the database backup." });
            }
        }

        /// <summary>
        /// Retrieves the list of scheduled background automation jobs.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetJobs()
        {
            try
            {
                // Query configured automation tasks
                var jobs = await _automationservice.GetJobsAsync();
                return Json(jobs);
            }
            catch (Exception ex)
            {
                // Handle failure reading the automation manifest
                _logger.LogError(ex, nameof(GetJobs), nameof(SystemController), "Error retrieving automation jobs");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while fetching background jobs." });
            }
        }

        /// <summary>
        /// Updates the schedule or status of a specific background job.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateJob([FromBody] AutomationJobDto model)
        {
            try
            {
                // Apply strict model validation to the incoming schedule update
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Modify the job configuration in the scheduler
                await _automationservice.UpdateJobAsync(model.JobKey, model.CronExpression, model.IsEnabled);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log and respond to scheduling issues
                _logger.LogError(ex, nameof(UpdateJob), nameof(SystemController), $"Error updating job {model?.JobKey}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while updating the job schedule." });
            }
        }

        /// <summary>
        /// Forces an immediate manual execution of a background job.
        /// </summary>
        /// <param name="jobKey">The identifier of the job to run.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunJobNow(string jobKey)
        {
            try
            {
                // Check if the job identifier is valid
                if (string.IsNullOrWhiteSpace(jobKey))
                {
                    return BadRequest(new { success = false, message = "A valid Job Key is required." });
                }

                // Trigger execution
                await _automationservice.RunJobNowAsync(jobKey);
                return Json(new { success = true, message = "Job started." });
            }
            catch (Exception ex)
            {
                // Catch invocation issues
                _logger.LogError(ex, nameof(RunJobNow), nameof(SystemController), $"Error triggering job {jobKey}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while attempting to start the job." });
            }
        }

        /// <summary>
        /// Saves the generic application settings payload.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveGeneralSettings(GeneralSettingsDto General)
        {
            try
            {
                // Ensure data payload structure is valid
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Commit the general configuration settings
                await _systemSettings.SaveGeneralSettingsAsync(General);
                return Json(new { success = true, message = "General settings saved." });
            }
            catch (Exception ex)
            {
                // Safely log and decline saving invalid/problematic general data
                _logger.LogError(ex, nameof(SaveGeneralSettings), nameof(SystemController), "Error saving general settings");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while saving general settings." });
            }
        }

        /// <summary>
        /// Saves the system's SMTP configuration used for outbound email.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSmtpSettings(SmtpSettingsDto Smtp)
        {
            try
            {
                // Validate form payload
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Commit the mail delivery settings
                await _systemSettings.SaveSmtpSettingsAsync(Smtp);
                return Json(new { success = true, message = "SMTP settings saved." });
            }
            catch (Exception ex)
            {
                // Log failures dealing with SMTP config persistence
                _logger.LogError(ex, nameof(SaveSmtpSettings), nameof(SystemController), "Error saving SMTP settings");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while saving SMTP settings." });
            }
        }

        /// <summary>
        /// Saves global configuration rules specific to IRS reporting.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveIrsSettings(IrsSettingsDto Irs)
        {
            try
            {
                // Guarantee payload structure is compliant
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Persist the IRS setup values
                await _systemSettings.SaveIrsSettingsAsync(Irs);
                return Json(new { success = true, message = "IRS settings saved." });
            }
            catch (Exception ex)
            {
                // Catch any failures updating IRS keys and parameters
                _logger.LogError(ex, nameof(SaveIrsSettings), nameof(SystemController), "Error saving IRS settings");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while saving IRS settings." });
            }
        }

        /// <summary>
        /// Fetches a paginated HTML partial view representing the audit log table.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAuditLogPartial(AuditLogRequest request)
        {
            try
            {
                // Return bad request if filter parameters are malformed
                if (request == null)
                {
                    return BadRequest("Invalid audit log query request.");
                }

                // Request filtered chunk of logs
                var model = await _systemSettings.GetPagedAuditLogsAsync(request);
                return PartialView("_AuditLogTablePartial", model);
            }
            catch (Exception ex)
            {
                // Log error loading the table partial
                _logger.LogError(ex, nameof(GetAuditLogPartial), nameof(SystemController), "Error fetching audit log partial");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the audit logs.");
            }
        }

        /// <summary>
        /// Generates a downloadable CSV containing the complete history of system audit logs.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ExportAuditLogs()
        {
            try
            {
                var stream = await _systemService.ExportAuditLogsToCsvAsync();
                return File(stream, "text/csv", $"AuditLogs_{DateTime.Now:yyyyMMdd}.csv");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(ExportAuditLogs), nameof(SystemController), "Error exporting audit logs to CSV", HttpContext?.Connection?.RemoteIpAddress?.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while generating the audit log export file.");
            }
        }

        /// <summary>
        /// Attempts to establish a connection using the saved SMTP settings to verify outbound mail delivery.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestSmtp()
        {
            try
            {
                var (success, message) = await _systemService.TestSmtpConnectionAsync();
                if (!success) return BadRequest(new { success, message });
                return Json(new { success, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(TestSmtp), nameof(SystemController), "SMTP test failed.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = $"SMTP Connection Failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Performs format and sanity checks against the active IRS settings without actually submitting data.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestIrsConfig()
        {
            try
            {
                var (success, message) = await _systemService.ValidateIrsFormatAsync();
                if (!success) return BadRequest(new { success, message });
                return Json(new { success, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(TestIrsConfig), nameof(SystemController), "Error testing IRS config");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while validating the IRS configuration." });
            }
        }

        /// <summary>
        /// Enables or disables external programmatic access to the API endpoints.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleApi(bool isEnabled)
        {
            try
            {
                // Format the requested boolean to a string representation for key-value storage
                string valueToSave = isEnabled.ToString().ToLower();

                // Issue the update operation via the generic setting manager
                await _systemSettings.UpdateSettingAsync("IsApiEnabled", valueToSave);

                // Flash feedback to the UI
                if (isEnabled)
                {
                    TempData["SuccessMessage"] = "API Access is now ENABLED.";
                }
                else
                {
                    TempData["ErrorMessage"] = "API Access has been DISABLED. External requests will be rejected.";
                }

                // Enforce post/redirect/get pattern
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                // Log and gracefully handle toggle faults securely
                _logger.LogError(ex, nameof(ToggleApi), nameof(SystemController), "Error toggling API access");

                // Set the TempData error for the redirect, and also issue the requested 500 status internally
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while toggling API access.");
            }
        }
             

        #region ErrorLogs
        /// <summary>
        /// Fetches a paginated HTML partial view representing the error log table.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GetErrorLogsDataTables()
        {
            var draw = Request.Form["draw"].FirstOrDefault();
            var start = Request.Form["start"].FirstOrDefault();
            var length = Request.Form["length"].FirstOrDefault();
            var searchValue = Request.Form["search[value]"].FirstOrDefault();
            var sortColumnIndex = Request.Form["order[0][column]"].FirstOrDefault();
            var sortColumn = Request.Form[$"columns[{sortColumnIndex}][name]"].FirstOrDefault();
            var sortDirection = Request.Form["order[0][dir]"].FirstOrDefault();

            // Grab our new custom parameters
            var customLevel = Request.Form["customLevel"].FirstOrDefault();
            var customStartDate = Request.Form["startDate"].FirstOrDefault();
            var customEndDate = Request.Form["endDate"].FirstOrDefault();

            int pageSize = length != null ? Convert.ToInt32(length) : 10;
            int skip = start != null ? Convert.ToInt32(start) : 0;
            int pageNumber = (skip / pageSize) + 1;

            var request = new ErrorLogRequest
            {
                Page = pageNumber,
                PageSize = pageSize,
                Search = searchValue,
                Level = customLevel, // Apply the custom dropdown
                SortColumn = string.IsNullOrEmpty(sortColumn) ? "LoggedOnDate" : sortColumn,
                SortDirection = string.IsNullOrEmpty(sortDirection) ? "DESC" : sortDirection.ToUpper()
            };

            // Safely parse the custom dates
            if (DateTime.TryParse(customStartDate, out DateTime sDate)) request.StartDate = sDate;
            if (DateTime.TryParse(customEndDate, out DateTime eDate)) request.EndDate = eDate;

            var model = await _systemSettings.GetPagedErrorLogsAsync(request);

            return Json(new { draw = draw, recordsTotal = model.TotalRecords, recordsFiltered = model.TotalRecords, data = model.Logs });
        }
        [HttpGet]
        public async Task<IActionResult> GetErrorStats()
        {
            try
            {
                var stats = await _systemSettings.GetErrorSummaryStatsAsync();
                return Json(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetErrorStats), nameof(SystemController), "Error retrieving summary statistics for error logs");
                return Json(new { totalLogs = 0, totalErrors = 0, todayErrors = 0 });
            }
        }

        #endregion


        #region FilingYear

        [AuthorizePermission("Index")]
        [HttpGet] // Add this to explicitly allow GET requests
        public async Task<IActionResult> FilingYearListPartial(int pageIndex, string search, int sortColumn, string sortOrder, int PageSize)
        {
            try
            {
                var paginationEntity = new PaginationEntity
                {
                    PageIndex = pageIndex,
                    PageSize = PageSize,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder
                };

                var (filingYears, metadata) = await _systemSettings.GetFilingYearList(paginationEntity);

                var viewModel = new FilingYearViewModel
                {
                    FilingYears = filingYears,
                    Metadata = metadata,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder
                };

                // Standard way to return a partial view located in the same controller's view folder (Views/System)
                return PartialView("_FilingYearListPartial", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(FilingYearListPartial), nameof(SystemController), "Error loading partial list.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }




        public async Task<IActionResult> GetFilingYear_by_ID(int? id)
        {
            try
            {
                var filingYear = id.HasValue ? await _systemSettings.GetFilingYearFormData(id.Value) : new FilingYearModel();
                var model = new FilingYearFormViewModel { FilingYear = filingYear };
                return PartialView("_FilingYearFormModal", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetFilingYear_by_ID), nameof(SystemController), "Error fetching form.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Add_New()
        {
            var model = new FilingYearFormViewModel { FilingYear = new FilingYearModel() };
            return PartialView("_FilingYearFormModal", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddOrUpdate(FilingYearFormViewModel model)
        {
            try
            {
                if (model == null || model.FilingYear == null)
                {
                    return BadRequest(new { success = false, message = "Invalid input data." });
                }

                await _systemSettings.AddOrUpdateFilingYear(model.FilingYear);
                return Json(new { success = true, message = "Filing Year saved successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddOrUpdate), nameof(SystemController), "Error saving data.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred." });
            }
        }
        
        #endregion
    }
}