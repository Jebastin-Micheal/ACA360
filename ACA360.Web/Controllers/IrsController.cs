using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ACA360.Web.Filters;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Handles all IRS ACA filing operations including XML generation, file downloads,
    /// transmission tracking, acknowledgement processing, and correction batch management.
    /// Restricted to authorised roles only via the class-level Authorize attribute.
    /// </summary>
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," +
                       UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," +
                       UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," +
                       UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class IrsController : BaseController
    {
        private readonly IIrsXmlService _xmlService;
        private readonly IFilingYearService _yearService;
        private readonly ILoggerService _logger;

        /// <summary>
        /// Initialises the IrsController with the XML service, filing year service,
        /// and logger injected via the DI container.
        /// </summary>
        public IrsController(
            IIrsXmlService xmlService,
            IFilingYearService yearService,
            ILoggerService logger)
        {
            _xmlService = xmlService;
            _yearService = yearService;
            _logger = logger;
        }

        /// <summary>
        /// GET: Renders the IRS filing dashboard for the specified filing year.
        /// Displays submission candidates alongside historical submission records.
        /// Defaults to the current calendar year if no year is provided.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(int year = 0)
        {
            try
            {
                // Default to the current year when no year parameter is supplied
                if (year == 0)
                {
                    var yearString = GetCurrentFilingYear();
                    year = !string.IsNullOrEmpty(yearString) ? int.Parse(yearString) : 0;
                }

                // Step 1: Fetch the combined dashboard data � candidates awaiting submission and past submission history
                var dashboardItems = await _xmlService.GetIrsDashboardAsync(year);

                // Step 2: Load all available filing years to populate the year-selector dropdown
                var years = await GetAllFilingYearsAsync();
                ViewBag.Years = years.Select(y => new SelectListItem
                {
                    Text = y.FilingYear.ToString(),
                    Value = y.FilingYear.ToString(),
                    Selected = (y.FilingYear.ToString() == year.ToString())
                }).ToList();

                ViewBag.SelectedYear = year;

                return View(dashboardItems);
            }
            catch (Exception ex)
            {
                // Log the failure with context and redirect to a generic error page
                _logger.LogError(ex, nameof(Index), "IrsController", "Error loading IRS dashboard", GetUserIp());
                return StatusCode(500, "An unexpected error occurred while loading the IRS dashboard.");
            }
        }

        /// <summary>
        /// POST: Triggers the XML batch generation process for a given employer and filing year.
        /// Invokes the submission engine (Phase 5) to build manifest and form XML files.
        /// Returns a JSON result indicating success or failure � consumed by client-side AJAX.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope]
        public async Task<IActionResult> GenerateXml(int employerId, int year)
        {
            try
            {
                // Resolve the currently authenticated user's identifier for audit trail purposes
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                // Delegate to the XML generation engine; this produces both manifest and form files
                await _xmlService.GenerateSubmissionFilesAsync(employerId, year, userId);

                return Json(new { success = true, message = "XML Batch generated successfully." });
            }
            catch (Exception ex)
            {
                // Log the generation failure with employer and year context to aid diagnosis
                _logger.LogError(ex, nameof(GenerateXml), "IrsController",
                    $"XML generation failed for EmployerId={employerId}, Year={year}", GetUserIp());

                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        /// <summary>
        /// GET: Streams a generated XML submission file (manifest or form) to the client for download.
        /// Returns 404 if the submission record does not exist or if the physical file is missing from storage.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DownloadFile(int id, string type)
        {
            try
            {
                // Step 1: Retrieve the submission record; return 404 if it no longer exists
                var submission = await _xmlService.GetSubmissionByIdAsync(id);
                if (submission == null)
                    return NotFound($"Submission with ID {id} was not found.");

                // Step 2: Resolve the correct file path based on the requested type (manifest vs form)
                string filePath = (type == "manifest") ? submission.ManifestFilePath : submission.FormFilePath;

                // Step 3: Guard against files that have been deleted or moved from server storage
                if (!System.IO.File.Exists(filePath))
                    return NotFound("File missing from server storage.");

                // Step 4: Read file bytes and return as a downloadable XML attachment
                string fileName = Path.GetFileName(filePath);
                var bytes = await System.IO.File.ReadAllBytesAsync(filePath);

                return File(bytes, "application/xml", fileName);
            }
            catch (Exception ex)
            {
                // Log the download failure; do not expose internal path details in the response
                _logger.LogError(ex, nameof(DownloadFile), "IrsController",
                    $"File download failed for SubmissionId={id}, Type={type}", GetUserIp());

                return StatusCode(500, "An unexpected error occurred while downloading the file.");
            }
        }

        /// <summary>
        /// POST: Marks a submission as transmitted to the IRS AIR system.
        /// Updates the submission status record with a "Transmitted" label.
        /// Returns a JSON result consumed by client-side AJAX to reflect the updated state in the UI.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkTransmitted(int id)
        {
            try
            {
                // Update the submission status to reflect successful transmission to the AIR system
                await _xmlService.UpdateStatusAsync(id, 1, "Transmitted to AIR System");

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log the status update failure so it can be investigated without manual DB queries
                _logger.LogError(ex, nameof(MarkTransmitted), "IrsController",
                    $"Failed to mark submission as transmitted for SubmissionId={id}", GetUserIp());

                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        /// <summary>
        /// POST: Receives and processes an IRS acknowledgement (.ack) file for a given submission.
        /// Delegates parsing and status reconciliation to the XML service.
        /// Returns a JSON result indicating success or failure � consumed by client-side AJAX.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadAck(int id, IFormFile file)
        {
            try
            {
                // Validate that a file was actually included in the request before processing
                if (file == null || file.Length == 0)
                    return Json(new { success = false, message = "No acknowledgement file was provided." });

                // Delegate ACK parsing and submission status update to the XML service
                await _xmlService.ProcessAckFileAsync(id, file);

                return Json(new { success = true, message = "IRS Acknowledgement processed successfully." });
            }
            catch (Exception ex)
            {
                // Log the ACK processing failure; include submission ID for correlation with IRS records
                _logger.LogError(ex, nameof(UploadAck), "IrsController",
                    $"ACK file processing failed for SubmissionId={id}", GetUserIp());

                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        /// <summary>
        /// GET: Loads the correction details view for a submission that received IRS error feedback.
        /// Retrieves both the original submission record and its parsed IRS error list.
        /// Returns 404 if the submission cannot be found.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CorrectionDetails(int id)
        {
            try
            {
                // Step 1: Retrieve the original submission; return 404 if it does not exist
                var submission = await _xmlService.GetSubmissionByIdAsync(id);
                if (submission == null)
                    return NotFound($"Submission with ID {id} was not found.");

                // Step 2: Retrieve the structured IRS error list parsed from the error/ACK file
                var errors = await _xmlService.GetSubmissionErrorsAsync(id);

                // Expose submission context to the view for display and correction targeting
                ViewBag.EmployerId = submission.EmployerId;
                ViewBag.SubmissionId = id;
                ViewBag.ReceiptId = submission.ReceiptId;

                return View(errors);
            }
            catch (Exception ex)
            {
                // Log the failure and return a 500; do not expose raw error detail to the client
                _logger.LogError(ex, nameof(CorrectionDetails), "IrsController",
                    $"Error loading correction details for SubmissionId={id}", GetUserIp());

                return StatusCode(500, "An unexpected error occurred while loading correction details.");
            }
        }

        /// <summary>
        /// POST: Generates a correction XML batch for a previously rejected or errored submission.
        /// Invokes the correction engine (Phase 6) which builds corrected manifest and form files
        /// tied to the original submission's receipt ID.
        /// Returns a JSON result with the new submission ID � consumed by client-side AJAX.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateCorrectionBatch(int originalSubmissionId)
        {
            try
            {
                // Resolve the current user's identifier to associate the correction with the initiating user
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                // Delegate to the correction generation engine; returns the newly created submission log entry
                var newLog = await _xmlService.GenerateCorrectionFilesAsync(originalSubmissionId, userId);

                return Json(new
                {
                    success = true,
                    message = "Correction XML generated successfully!",
                    newSubmissionId = newLog.SubmissionId
                });
            }
            catch (Exception ex)
            {
                // Log the correction failure; include the original submission ID for traceability
                _logger.LogError(ex, nameof(GenerateCorrectionBatch), "IrsController",
                    $"Correction batch generation failed for OriginalSubmissionId={originalSubmissionId}", GetUserIp());

                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        #region Helpers

        /// <summary>
        /// Returns the remote IP address of the current HTTP request.
        /// Used to enrich log entries with caller context across all actions.
        /// </summary>
        private string GetUserIp()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        #endregion
    }
}