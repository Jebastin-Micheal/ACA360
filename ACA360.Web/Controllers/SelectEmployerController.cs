using ACA360.Core.Attributes;
using ACA360.Core.Constants;
using ACA360.Core.Extensions;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.All)]
    public class SelectEmployerController : BaseController
    {
        private readonly IEmployerService _employerService;
        private readonly ILoggerService _logger;
        private readonly IUserTrackingService _trackingService;

        public SelectEmployerController(IEmployerService employerService, ILoggerService logger, IUserTrackingService trackingService)
        {
            _employerService = employerService;
            _logger = logger;
            _trackingService = trackingService;
        }

        #region Select Employer Page

        /// <summary>
        /// Retrieves the main view for selecting an employer. 
        /// Supports pagination and sorting for the employer list.
        /// Handles both full page loads and AJAX partial updates.
        /// </summary>
        /// <param name="pageIndex">The current page number to display.</param>
        /// <param name="sortColumn">The column index used for sorting.</param>
        /// <param name="sortOrder">The sort direction (asc/desc).</param>
        /// <returns>A full view or partial view containing the employer list.</returns>
        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> Index(int pageIndex = 1, int sortColumn = 0, string sortOrder = "asc")
        {
            try
            {
                // Prepare pagination and sorting parameters
                var paginationEntity = new PaginationEntity
                {
                    PageIndex = pageIndex,
                    PageSize = 20,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder,
                    fillingYear = GetCurrentFilingYear(),
                    // Default the picker to parent companies only.
                    TypeFilter = ResolveTypeFilterForQuery(null)
                };

                // Fetch paginated employers data and metadata from the service
                var userId = GetCurrentUserId();
                var roleName = User?.FindFirst(ClaimTypes.Role)?.Value ?? "";
                var (employers, metadata) = await _employerService.GetEmployersListViewAsync(paginationEntity, userId, roleName);
                ViewBag.EmptyMessage = await BuildAssignmentEmptyMessageAsync(employers, userId, roleName, paginationEntity.fillingYear);
                // Map the retrieved metadata to the view entity
                var paginationView = new PaginationViewEntity
                {
                    RecordCount = metadata.RecordCount,
                    StartPage = metadata.StartPage,
                    EndPage = metadata.EndPage,
                    PageNumber = metadata.PageNumber,
                    PageSize = metadata.PageSize,
                    TotalCount = metadata.TotalCount,
                    TotalPages = metadata.TotalPages
                };

                // Construct the view model to bind data to the view
                var model = new EmployerListViewModel
                {
                    Employers = employers,
                    Metadata = paginationView,
                    Search = "",
                    SortColumn = sortColumn,
                    SortOrder = sortOrder,
                    TypeFilter = ResolveTypeFilterForDisplay(null),
                    TypeFilters = GetTypeFilterOptions()
                };

                ViewBag.Pagination = metadata;

                // Return partial view if the request is an AJAX call, else return the full view
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return PartialView("_EmployerList_selectPartial", model);
                }

                return View(model);
            }
            catch (Exception ex)
            {
                // Log the exception securely
                _logger.LogError(ex, nameof(Index), nameof(SelectEmployerController), $"Error loading Select Employer page index: {pageIndex}");

                // Return generic 500 error status
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the employers list.");
            }
        }

        /// <summary>
        /// Handles AJAX search requests for the employer list.
        /// Filters and paginates the employers based on the provided criteria.
        /// </summary>
        /// <param name="pagination">The pagination and search parameters.</param>
        /// <returns>A partial view containing the filtered employer list.</returns>
        [AuthorizePermission("Index")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SearchEmployers(PaginationEntity pagination)
        {
            try
            {
                // Validate input payload
                if (pagination == null)
                {
                    return BadRequest("Invalid search parameters provided.");
                }

                // Retrieve filtered and paginated list of employers
                var userId = GetCurrentUserId();
                var roleName = User?.FindFirst(ClaimTypes.Role)?.Value ?? "";

                // The modal's first load fires before the Type dropdown exists, so the
                // JS sends an empty value. Empty means "unspecified" and resolves to
                // Main, which is what makes the default stick on open as well as on
                // the full page.
                var typeFilterForDisplay = ResolveTypeFilterForDisplay(pagination.TypeFilter);
                pagination.TypeFilter = ResolveTypeFilterForQuery(pagination.TypeFilter);

                var (employers, metadata) = await _employerService.GetEmployersListViewAsync(pagination, userId, roleName);
                ViewBag.EmptyMessage = await BuildAssignmentEmptyMessageAsync(employers, userId, roleName, pagination.fillingYear);

                // Map the resulting metadata to the view entity
                var paginationView = new PaginationViewEntity
                {
                    RecordCount = metadata.RecordCount,
                    StartPage = metadata.StartPage,
                    EndPage = metadata.EndPage,
                    PageNumber = metadata.PageNumber,
                    PageSize = metadata.PageSize,
                    TotalCount = metadata.TotalCount,
                    TotalPages = metadata.TotalPages
                };

                // Construct view model for the partial rendering
                var model = new EmployerListViewModel
                {
                    Employers = employers,
                    Metadata = paginationView,
                    Search = pagination.Search,
                    SortColumn = pagination.SortColumn,
                    SortOrder = pagination.SortOrder,
                    TypeFilter = typeFilterForDisplay,
                    TypeFilters = GetTypeFilterOptions()
                };

                return PartialView("_EmployerList_selectPartial", model);
            }
            catch (Exception ex)
            {
                // Log failure and return 500 status code to gracefully fail the AJAX request
                _logger.LogError(ex, nameof(SearchEmployers), nameof(SelectEmployerController), "Error searching employers list.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while searching employers.");
            }
        }
        /// <summary>
        /// Builds a friendly empty-state message when the employer list has no real rows.
        /// For the assignment-restricted roles (Account Manager, AM Supervisor, Data Analyst,
        /// DA Supervisor) it distinguishes "no employer assigned at all" from "assigned, but this
        /// year has no records". Returns null when there are employers to show.
        /// </summary>
        private async Task<string?> BuildAssignmentEmptyMessageAsync(
            List<Employer> employers, string userId, string roleName, object filingYear)
        {
            // There are real employers to show -> no message needed.
            if (employers != null && employers.Any(e => !string.IsNullOrEmpty(e.Id)))
                return null;

            bool restricted = roleName == UserRoles.AccountManager || roleName == UserRoles.AMSupervisor
                           || roleName == UserRoles.DataAnalyst || roleName == UserRoles.DASupervisor;

            // Non-restricted roles (Admin, Director, etc.) just see a generic "nothing this year".
            if (!restricted)
                return $"No employers found for filing year {filingYear}.";

            // Restricted role with an empty list: do they have any assignment at all?
            // (Assigned employers are visible across every year their company has records,
            //  so an empty list here just means this particular year has no records.)
            var allAssigned = await _employerService.GetEmployersForUserAsync(userId, roleName);
            if (allAssigned != null && allAssigned.Any())
                return $"Your assigned employers have no records for filing year {filingYear}. Choose another filing year to view their files.";

            // No assignments at all.
            return "No employer assigned to you.";
        }


        /// <summary>
        /// Sets the selected employer into the user's session context and updates their database tracking preferences.
        /// Redirects the user back to their previous page or the main dashboard.
        /// </summary>
        /// <param name="id">The selected employer's unique identifier.</param>
        /// <param name="empname">The selected employer's name (optional, re-fetched internally for safety).</param>
        /// <returns>A redirection to the referring page or the Dashboard.</returns>
        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> SelectThis(int id, string empname, string returnUrl = null)
        {
            try
            {
                var employer = await _employerService.GetEmployerByIdAsync(id.ToString());
                if (employer == null) return RedirectToAction("Index");
                // Menu permission is not employer scope: verify this user is actually
                // entitled to the employer being selected before it enters session.
                var userId = GetCurrentUserId();
                var roleName = User?.FindFirst(ClaimTypes.Role)?.Value ?? "";

                if (!IsUnrestrictedRole(roleName))
                {
                    var allowed = await _employerService.GetEmployersForUserAsync(userId, roleName);
                    if (allowed == null || !allowed.Any(e => e.Id == employer.Id))
                    {
                        _logger.LogError(
                            new UnauthorizedAccessException("Employer scope denied"),
                            nameof(SelectThis), nameof(SelectEmployerController),
                            $"User {userId} ({roleName}) attempted to select unassigned employer {id}.");
                        return Forbid();
                    }
                }
                SetCurrentEmployerId(employer.Id.ToString());
                SetCurrentEmployerName(employer.Name);

                int currentFilingYear = Convert.ToInt32(GetCurrentFilingYear());
                if (currentFilingYear == 0) currentFilingYear = DateTime.Now.Year;

                if (!string.IsNullOrEmpty(userId))
                {
                    await _trackingService.UpdateUserPreferenceAsync(userId, id, currentFilingYear);
                }

                // Redirect to the returnUrl if one was provided
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                // Fallback: Redirect to Referer
                var referer = HttpContext.Request.Headers["Referer"].ToString();
                var currentHost = $"{Request.Scheme}://{Request.Host.Value}";

                if (!string.IsNullOrEmpty(referer))
                {
                    referer = referer.ToLower();
                    if (referer.StartsWith(currentHost) && !referer.Contains("/selectemployer"))
                    {
                        return Redirect(referer);
                    }
                }

                return RedirectToAction("Index", "Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SelectThis), nameof(SelectEmployerController), $"Error selecting employer ID: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while selecting the employer.");
            }
        }

        // Roles that legitimately see every employer. Account Manager and Data Analyst
        // are NOT listed here � they are restricted to their assignments.
        // >>> CONFIRM THIS LIST BEFORE APPLYING <<<
        private static bool IsUnrestrictedRole(string roleName) =>
            roleName == UserRoles.SuperAdmin ||
            roleName == UserRoles.Admin ||
            roleName == UserRoles.DASupervisor ||
            roleName == UserRoles.AMSupervisor ||
            roleName == UserRoles.ACADirector;

        /// <summary>
        /// The Type filter shown above the employer list.
        /// "All" carries a real value rather than an empty string: empty now means
        /// "caller did not specify", which resolves to the Main-only default.
        /// </summary>
        private List<SelectListItem> GetTypeFilterOptions()
        {
            return new List<SelectListItem>
            {
                new SelectListItem { Text = "Main",      Value = "Main" },
                new SelectListItem { Text = "Affiliate", Value = "Affiliate" },
                new SelectListItem { Text = "All",       Value = "All" }
            };
        }

        /// <summary>Employer type the picker falls back to when none is specified.</summary>
        private const string DefaultEmployerTypeFilter = "Main";

        /// <summary>
        /// What to show in the dropdown. Never null, so the selected option always matches.
        /// </summary>
        private static string ResolveTypeFilterForDisplay(string? requested) =>
            string.IsNullOrWhiteSpace(requested) ? DefaultEmployerTypeFilter : requested.Trim();

        /// <summary>
        /// What to send to sp_EmployersOnlyList. The procedure treats NULL as "no type
        /// filter" and only recognises 'Main' and 'Affiliate', so "All" maps to null —
        /// passing the literal string through would match no branch and return no rows.
        /// </summary>
        private static string? ResolveTypeFilterForQuery(string? requested)
        {
            var value = ResolveTypeFilterForDisplay(requested);

            return string.Equals(value, "All", StringComparison.OrdinalIgnoreCase) ? null : value;
        }

        #endregion

        /// <summary>
        /// Updates the employer and year context explicitly via an AJAX POST.
        /// Used primarily in context-switching scenarios where redirection is not required.
        /// </summary>
        /// <param name="employerId">The unique identifier of the selected employer.</param>
        /// <param name="year">The specific filing year to activate.</param>
        /// <returns>A JSON response indicating success or failure.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetEmployer(int employerId, int year)
        {
            try
            {
                // Validate inputs logically
                if (employerId <= 0 || year <= 0)
                {
                    return BadRequest(new { success = false, message = "Valid employer ID and year must be provided." });
                }

                // 1. Fetch exact employer details and update Session Context
                var employer = await _employerService.GetEmployerByIdAsync(employerId.ToString());
                if (employer == null)
                {
                    return NotFound(new { success = false, message = "The specified employer could not be found." });
                }

                // Entitlement check, matching SelectThis. Without it this action is a
                // way straight past the choke point: every session-scoped page in the
                // application trusts SelectedEmployerId precisely because the only
                // writers verify it first, so an unchecked write here would re-scope
                // the entire session to an employer the caller cannot otherwise reach.
                if (!await IsEmployerPermittedAsync(employerId))
                {
                    _logger.LogError(
                        new UnauthorizedAccessException("Employer scope denied"),
                        nameof(SetEmployer), nameof(SelectEmployerController),
                        $"User {GetCurrentUserId()} attempted to set session context to unassigned employer {employerId}.");

                    return StatusCode(StatusCodes.Status403Forbidden,
                        new { success = false, message = "You do not have access to that employer." });
                }

                SetCurrentEmployerId(employer.Id.ToString());
                SetCurrentEmployerName(employer.Name);
                SetCurrentFilingYear(year.ToString());

                // 2. Persist the updated preferences to the tracking database layer
                var userId = GetCurrentUserId();
                if (!string.IsNullOrEmpty(userId))
                {
                    await _trackingService.UpdateUserPreferenceAsync(userId, employerId, year);
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log errors executing the context change and return a structured JSON 500 failure response
                _logger.LogError(ex, nameof(SetEmployer), nameof(SelectEmployerController), $"Error dynamically setting context for Employer ID: {employerId}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while updating the employer context." });
            }
        }
    }
}