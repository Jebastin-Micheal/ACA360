using ACA360.BusinessLogic.Services;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ACA360.Web.Filters;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class FilingController : BaseController
    {
        private readonly IFilingService _filingService;
        private readonly IFilingYearService _yearService;
        private readonly ILoggerService _logger;

        public FilingController(
            IFilingService filingService,
            IFilingYearService yearService,
            ILoggerService logger)
        {
            _filingService = filingService;
            _yearService = yearService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves and renders the Filing Dashboard for a specific filing year.
        /// Aggregates filing data and populates filtering dropdowns.
        /// </summary>
        /// <param name="year">The target filing year to display.</param>
        /// <returns>An IActionResult containing the populated FilingDashboardViewModel.</returns>
        public async Task<IActionResult> Index(int year = 0)
        {
            try
            {
                if (year == 0)
                {
                    var yearString = GetCurrentFilingYear();
                    year = !string.IsNullOrEmpty(yearString) ? int.Parse(yearString) : 0;
                }

                var userId = GetCurrentUserId();
                var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

                // Default pagination state for initial load
                var pagination = new PaginationEntity { PageIndex = 1, PageSize = 10, SortColumn = 0, SortOrder = "asc", StatusFilter = -1 };
                var model = await _filingService.GetFilingDashboardAsync(userId, role, year, pagination);

                var years = await GetAllFilingYearsAsync();
                ViewBag.Years = years.Select(y => new SelectListItem
                {
                    Text = y.FilingYear.ToString(),
                    Value = y.FilingYear.ToString(),
                    Selected = (y.FilingYear == year)
                }).ToList();

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Index), "FilingController", $"Error loading Filing Dashboard for year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        // NEW: AJAX Action
        public async Task<IActionResult> FilingListPartial(int year, int pageIndex, string search, int sortColumn, string sortOrder, int pageSize = 10, int? statusFilter = null)
        {
            var userId = GetCurrentUserId();
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

            if (pageSize <= 0) pageSize = 10;
            if (pageIndex <= 0) pageIndex = 1;

            int? statusFilterValue = string.IsNullOrEmpty(Request.Query["statusFilter"])
        ? (int?)null
        : statusFilter;

            var pagination = new PaginationEntity
            {
                PageIndex = pageIndex,
                PageSize = pageSize,
                Search = search,
                SortColumn = sortColumn,
                SortOrder = sortOrder,
                StatusFilter = statusFilterValue
            };

            var model = await _filingService.GetFilingDashboardAsync(userId, role, year, pagination);
            return PartialView("_FilingListPartial", model);
        }

        /// <summary>
        /// Finalizes and logically locks the ACA filing records for a specified employer and year.
        /// </summary>
        /// <param name="employerId">The unique identifier of the employer.</param>
        /// <param name="year">The relevant filing year.</param>
        /// <returns>A JSON response indicating success or failure of the finalization process.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope]
        public async Task<IActionResult> Finalize(int employerId, int year)
        {
            try
            {
                // Validate incoming parameters
                if (employerId <= 0 || year <= 0)
                {
                    return BadRequest(new { success = false, message = "Valid Employer ID and Year are required." });
                }

                // Retrieve user ID to maintain an audit trail of who performed the finalization
                var userId = GetCurrentUserId();

                // Execute the finalization and locking workflow in the service layer
                await _filingService.FinalizeFilingAsync(employerId, year, userId);

                return Json(new { success = true, message = "Filing finalized and locked successfully." });
            }
            catch (Exception ex)
            {
                // Log the precise exception details for internal troubleshooting
                _logger.LogError(ex, nameof(Finalize), "FilingController", $"An error occurred while finalizing filing for Employer ID {employerId} and Year {year}.");

                // Return a 500 internal server error with a JSON payload for AJAX consumers
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while finalizing the filing." });
            }
        }

        /// <summary>
        /// Unlocks a previously finalized filing, reverting it to an editable state.
        /// Restricted to highly privileged roles (SuperAdmin, Admin).
        /// </summary>
        /// <param name="employerId">The unique identifier of the employer.</param>
        /// <param name="year">The relevant filing year.</param>
        /// <returns>A JSON response indicating success or failure of the unlock process.</returns>
        [HttpPost]
        [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin)]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope]
        public async Task<IActionResult> Unlock(int employerId, int year)
        {
            try
            {
                if (employerId <= 0 || year <= 0)
                {
                    return BadRequest(new { success = false, message = "Valid Employer ID and Year are required." });
                }

                //  NEW: get current user id
                var userId = GetCurrentUserId();

                //  CHANGED: pass userId
                await _filingService.UnlockFilingAsync(employerId, year, userId);

                return Json(new { success = true, message = "Filing unlocked. Data can now be edited." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Unlock), "FilingController", $"An error occurred while unlocking filing for Employer ID {employerId} and Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while unlocking the filing." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope]
        public async Task<IActionResult> ToggleArchive(int employerId, int year, bool isArchive)
        {
            try
            {
                if (employerId <= 0 || year <= 0)
                {
                    return BadRequest(new { success = false, message = "Valid Employer ID and Year are required." });
                }

                var userId = GetCurrentUserId();

                await _filingService.ToggleArchiveFilingAsync(employerId, year, isArchive, userId);

                string message = isArchive
                    ? "Filing archived successfully."
                    : "Filing restored to active filings successfully.";

                return Json(new { success = true, message = message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(ToggleArchive), "FilingController", $"Error toggling archive for Employer ID {employerId} and Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while updating the archive status." });
            }
        }


        public async Task<IActionResult> Timeline(int employerId, int year)
        {
            var timeline = await _filingService.GetFilingTimelineAsync(employerId, year);
            return PartialView("_FilingTimelinePartial", timeline);
        }
    }
}