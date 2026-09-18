using ACA360.BusinessLogic.Services;
using ACA360.Core.Constants;
using ACA360.Core.Helpers;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," +
                       UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," +
                       UserRoles.AMSupervisor + "," + UserRoles.DataAnalyst + "," +
                       UserRoles.AccountManager + "," + UserRoles.StaffAC + "," +
                       UserRoles.StaffAU + "," + UserRoles.Employer)]
    public class ErrorTriageController : BaseController
    {
        private readonly IErrorTriageService _triageService;
        private readonly IFileUploadLogService _logRepo;
        private readonly ILoggerService _logger;

        public ErrorTriageController(
            IErrorTriageService triageService,
            IFileUploadLogService logRepo,
            ILoggerService logger)
        {
            _triageService = triageService;
            _logRepo = logRepo;
            _logger = logger;
        }

        /// <summary>
        /// Renders the main Error Triage Dashboard view for a specific file log.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ErrorTriageDashboard(
            int id, int pageIndex = 1, string search = "",
            int sortColumn = 0, string sortOrder = "asc",
            int pageSize = 10, string severity = "", string searchColumn = "")
        {
            try
            {
                // Build the view model using the internal helper method
                var vm = await BuildViewModelInternal(
                    id, pageIndex, search, sortColumn, severity, searchColumn, sortOrder, pageSize);

                // Return a 404 Not Found if the view model could not be constructed (e.g., file log not found)
                if (vm == null) return NotFound("Triage dashboard data not found.");

                return View(vm);
            }
            catch (Exception ex)
            {
                // Log the exception and return a standard 500 Internal Server Error
                _logger.LogError(ex, nameof(ErrorTriageDashboard), "ErrorTriageController", $"An error occurred loading the Error Triage Dashboard for file log ID {id}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the triage dashboard.");
            }
        }

        /// <summary>
        /// Returns a partial view containing the list of errors for AJAX-based datatable updates.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ErrorTriageListPartial(
            int id, int pageIndex = 1, string search = "", string searchColumn = "",
            int sortColumn = 0, string sortOrder = "asc",
            int pageSize = 10, string severity = "")
        {
            try
            {
                // Build the view model using the search and pagination parameters
                var vm = await BuildViewModelInternal(
                    id, pageIndex, search, sortColumn, severity, searchColumn, sortOrder, pageSize);

                // If data is missing, render the partial view with a "Not Found" indicator
                if (vm == null)
                    return PartialView("_ErrorTriageListPartial",
                        new ErrorTriageViewModel { OriginalFileName = "Not Found" });

                return PartialView("_ErrorTriageListPartial", vm);
            }
            catch (Exception ex)
            {
                // Log the issue and return a 500 Internal Server Error string for the AJAX caller to handle
                _logger.LogError(ex, nameof(ErrorTriageListPartial), "ErrorTriageController", $"An error occurred loading the Error Triage list partial for file log ID {id}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the error list.");
            }
        }

        /// <summary>
        /// Retrieves the specific staging row data and renders it in an offcanvas editor partial view.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetStagingRow(
            int fileLogId, string tableName, long rowId)
        {
            try
            {
                // Fetch the editable row view model from the triage service
                var vm = await _triageService.BuildEditRowViewModelAsync(fileLogId, tableName, rowId);

                if (vm == null) return NotFound("Staging row not found.");

                return PartialView("_EditRowOffcanvasPartial", vm);
            }
            catch (Exception ex)
            {
                // Log the error accessing specific staging row data
                _logger.LogError(ex, nameof(GetStagingRow), "ErrorTriageController", $"An error occurred fetching staging row {rowId} from table {tableName}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while retrieving the staging row.");
            }
        }

        /// <summary>
        /// Updates a specific field in the staging table and triggers revalidation for that row.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateField([FromBody] UpdateFieldPayload payload)
        {
            try
            {
                // Validate incoming payload
                if (payload?.Values == null)
                {
                    return BadRequest(new { IsSuccess = false, message = "No data received." });
                }

                // Apply the field update and revalidate the record rules
                var result = await _triageService.UpdateFieldAndRevalidateAsync(payload);

                // Return the live counts so JS can update UI badges dynamically without a full reload
                return Json(new
                {
                    result.IsSuccess,
                    result.Errors,
                    newErrorCount = result.NewErrorCount,
                    newWarningCount = result.NewWarningCount
                });
            }
            catch (Exception ex)
            {
                // Log and gracefully fail, sending a structured JSON error response
                _logger.LogError(ex, nameof(UpdateField), "EINUploadController", "An error occurred updating a field in the staging table.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { IsSuccess = false, message = "An unexpected error occurred while updating the field." });
            }
        }

        /// <summary>
        /// Updates the status of a specific error (e.g., marking a warning as "Skipped").
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateErrorStatus([FromBody] UpdateStatusPayload payload)
        {
            try
            {
                // Validate payload
                if (payload == null)
                {
                    return BadRequest(new { success = false, message = "Invalid payload data." });
                }

                // Update the error's status in the repository
                await _triageService.UpdateErrorStatusAsync(payload.StagingRowErrorId, payload.Status);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log and return JSON formatted 500 error
                _logger.LogError(ex, nameof(UpdateErrorStatus), "ErrorTriageController", $"An error occurred updating the status for error ID {payload?.StagingRowErrorId}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while updating the error status." });
            }
        }

        public class BulkSkipPayload { public List<long> ErrorIds { get; set; } }

        /// <summary>
        /// Performs a bulk update to skip multiple warnings simultaneously.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkSkipWarnings([FromBody] BulkSkipPayload payload)
        {
            try
            {
                // Ensure the payload contains a valid list of error IDs
                if (payload?.ErrorIds == null || !payload.ErrorIds.Any())
                {
                    return BadRequest(new { success = false, message = "No error IDs provided." });
                }

                int count = 0;

                // Iterate through requested IDs and update their status to "Skipped"
                foreach (var id in payload.ErrorIds)
                {
                    await _triageService.UpdateErrorStatusAsync(id, "Skipped");
                    count++;
                }

                return Json(new { success = true, count });
            }
            catch (Exception ex)
            {
                // Log bulk operation failure and return 500
                _logger.LogError(ex, nameof(BulkSkipWarnings), "ErrorTriageController", "An error occurred during the bulk skip warnings operation.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while skipping warnings." });
            }
        }

        // SendForApproval was removed with the AM authorisation step. A Data Analyst who
        // has cleared triage now imports the file directly from the File Upload screen;
        // the fatal-error check that used to gate this submission lives in
        // FileUploadController.ImportData, which will not import a file that still has one.

        /// <summary>
        /// Generates and downloads an Excel report containing all identified triage errors for the file.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DownloadErrorReport(int id)
        {
            try
            {

                // Generate the Excel memory stream
                var stream = await _triageService.GenerateErrorReportAsync(id);
                var fileLog = await _logRepo.GetLogByIdAsync(id);

                // Reset stream position for the client download
                stream.Position = 0;

                return File(stream,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"ErrorReport_{fileLog.OriginalFileName}");
            }
            catch (Exception ex)
            {
                // Log the file generation error
                _logger.LogError(ex, nameof(DownloadErrorReport), "ErrorTriageController", $"An error occurred generating the error report for file log ID {id}.");

                // Maintain existing pattern of redirecting back to the dashboard with an error banner
                TempData["Error"] = "Report Error: An unexpected error occurred while generating the file.";
                return RedirectToAction("ErrorTriageDashboard", new { id });
            }
        }

        // -- Private helpers ---------------------------------------------------

        /// <summary>
        /// Centralized helper to construct the ErrorTriageViewModel containing paginated triage data.
        /// </summary>
        private async Task<ErrorTriageViewModel> BuildViewModelInternal(
 int id, int pageIndex, string search, int sortColumn,
 string severity, string searchColumn = "",
 string sortOrder = null, int pageSize = 10)
        {
            // Map incoming parameters to the pagination entity
            var paginationEntity = new PaginationEntity
            {
                PageIndex = pageIndex,
                PageSize = pageSize,
                Search = search,
                SearchColumn = searchColumn,
                SortColumn = sortColumn,
                SortOrder = sortOrder
            };



            // Fetch structured data from the service layer
            var vm = await _triageService.BuildTriageViewModelAsync(id, paginationEntity, severity);
            if (vm == null) return null;



            // Ensure Metadata is consistently populated for UI pagination links
            if (vm.Metadata == null)
                vm.Metadata = vm.Pagination ?? new PaginationViewEntity();
            int totalItems = Convert.ToInt32(vm.Metadata.TotalCount);



            // Enforce current context values into the metadata model
            vm.Metadata.CurrentPage = pageIndex;
            vm.Metadata.PageSize = pageSize;
            vm.Metadata.TotalCount = vm.TotalErrorsCount;
            vm.Metadata.TotalPages = (int)Math.Ceiling((double)totalItems / paginationEntity.PageSize);
            vm.Metadata.StartPage = ((paginationEntity.PageIndex - 1) / 10) * 10 + 1;
            vm.Metadata.EndPage = Math.Min(((paginationEntity.PageIndex - 1) / 10) * 10 + 10, (int)Math.Ceiling((double)totalItems / paginationEntity.PageSize));
            return vm;
        }
    }
}
