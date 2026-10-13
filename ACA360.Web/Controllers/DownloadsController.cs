using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Logging.Interfaces;
using ACA360.Repositories;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class DownloadsController : BaseController
    {
        private readonly IDownloadService _downloadService;
        private readonly ILoggerService _logger;
        public DownloadsController(IDownloadService downloadService, ILoggerService logger)
        {
            _downloadService = downloadService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves and renders the Downloads Dashboard for the currently authenticated user.
        /// Filters available downloads based on the user's role and identity.
        /// </summary>
        /// <returns>The View containing the Downloads Dashboard model.</returns>
        [HttpGet]
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                int year = 0;
                if (year == 0)
                {
                    var yearString = GetCurrentFilingYear();
                    year = !string.IsNullOrEmpty(yearString) ? int.Parse(yearString) : 0;
                }
                // Resolve entitlement server-side and hand the service the answer.
                // Previously this passed userId and role, and the service used
                // neither — sp_GetDownloadHistory took no parameters and returned
                // every batch in the system to every caller.
                var permitted = await GetPermittedEmployerIdsAsync();
                var model = await _downloadService.GetDownloadsDashboardAsync(
                    permitted, IsUnrestrictedEmployerRole());
                var years = await GetAllFilingYearsAsync();
                ViewBag.Years = years.Select(y => new SelectListItem
                {
                    Value = y.FilingYear.ToString(),
                    Text = y.FilingYear.ToString(),
                    Selected = (y.FilingYear.ToString() == year.ToString())
                }).ToList();
                return View(model);
            }
            catch (Exception ex)
            {
                // Log the exception to the configured logging sink
                _logger.LogError(ex, nameof(Index), "DownloadsController", "An error occurred while loading the Downloads Dashboard.");

                // Return a generic 500 Internal Server Error to avoid exposing sensitive stack traces
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the dashboard.");
            }
        }

        /// <summary>
        /// Retrieves a specific file by its name and returns it to the client as a zip download.
        /// Includes security checks to prevent directory traversal attacks.
        /// </summary>
        /// <param name="fileName">The name of the file requested for download.</param>
        /// <returns>A File result containing the requested file stream, or an error status code.</returns>
        [HttpGet]
        public async Task<IActionResult> GetFile(string fileName)
        {
            try
            {
                // Validation: Ensure the filename is not null or empty
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return BadRequest("File name cannot be empty.");
                }

                // Security: Prevent directory traversal by rejecting paths containing navigation characters
                if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
                {
                    return BadRequest("Invalid filename.");
                }

                // Ownership check. The traversal guard above stops a caller reaching
                // outside the folder; this stops them reaching a file inside it that
                // belongs to an employer they cannot see. Uses the same predicate as
                // the listing, so a file is never fetchable without being listable.
                var permitted = await GetPermittedEmployerIdsAsync();
                if (!await _downloadService.CanAccessFileAsync(
                        fileName, permitted, IsUnrestrictedEmployerRole()))
                {
                    _logger.LogError(new UnauthorizedAccessException("Download scope denied"),
                        nameof(GetFile), "DownloadsController",
                        $"User {GetCurrentUserId()} requested generated archive '{fileName}', " +
                        "which is outside their permitted employer set.");

                    return Forbid();
                }

                // Fetch the file stream from the underlying storage service
                var stream = await _downloadService.GetFileBytesAsync(fileName);

                // If the stream is null, the file does not exist or cannot be read
                if (stream == null)
                {
                    return NotFound("File not found in storage.");
                }

                // Append a cookie to signal the client frontend that the download process has started
                Response.Cookies.Append("fileDownloadToken", "true", new CookieOptions { Path = "/", HttpOnly = false });

                // Ensure the downloaded file uses a .zip extension regardless of its original extension
                fileName = Path.ChangeExtension(fileName, ".zip");

                // Return the stream as a downloadable file with the appropriate MIME type
                return File(stream, "application/zip", fileName);
            }
            catch (Exception ex)
            {
                // Log the exception details including the filename for troubleshooting
                _logger.LogError(ex, nameof(GetFile), "DownloadsController", $"An error occurred while attempting to download the file: {fileName}");

                // Return a generic 500 Internal Server Error
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while processing the file download.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadCodeReport(int employerId, int year)
        {
            // employerId arrives from the query string, so entitlement is checked
            // here rather than assumed. Without this, incrementing the id walks the
            // whole client base.
            if (!await IsEmployerPermittedAsync(employerId))
            {
                _logger.LogError(new UnauthorizedAccessException("Employer scope denied"),
                    nameof(DownloadCodeReport), "DownloadsController",
                    $"User {GetCurrentUserId()} requested the code report for unassigned employer {employerId}.");
                return Forbid();
            }

            try
            {
                var fileBytes = await _downloadService.GenerateCodeReportAsync(employerId, year);
                string fileName = $"ACA_1095C_Codes_{employerId}_{year}.xlsx";
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading code report for Employer: {EmployerId}, Year: {Year}",employerId.ToString(), year.ToString());
                return BadRequest("An error occurred generating the code report.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadPenaltyReport(int employerId, int year)
        {
            if (!await IsEmployerPermittedAsync(employerId))
            {
                _logger.LogError(new UnauthorizedAccessException("Employer scope denied"),
                    nameof(DownloadPenaltyReport), "DownloadsController",
                    $"User {GetCurrentUserId()} requested the penalty report for unassigned employer {employerId}.");
                return Forbid();
            }

            try
            {
                var fileBytes = await _downloadService.GeneratePenaltyReportAsync(employerId, year);
                string fileName = $"ACA_Penalty_Risk_Summary_{employerId}_{year}.xlsx";
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading penalty report for Employer: {EmployerId}, Year: {Year}", employerId.ToString(), year.ToString());
                return BadRequest("An error occurred generating the penalty summary report.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadFullReport(int employerId, int year)
        {
            if (!await IsEmployerPermittedAsync(employerId))
            {
                _logger.LogError(new UnauthorizedAccessException("Employer scope denied"),
                    nameof(DownloadFullReport), "DownloadsController",
                    $"User {GetCurrentUserId()} requested the master report for unassigned employer {employerId}.");
                return Forbid();
            }

            try
            {
                var fileBytes = await _downloadService.GenerateFullEmployerReportAsync(employerId, year);
                string fileName = $"ACA_Master_Employer_Report_{employerId}_{year}.xlsx";
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading full report for Employer: {EmployerId}, Year: {Year}", employerId.ToString(), year.ToString());
                return BadRequest("An error occurred generating the master employer report.");
            }
        }
    }
}