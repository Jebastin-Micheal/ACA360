using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Attributes;
using ACA360.Core.Constants;
using ACA360.Core.Extensions;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Implementations;
using ACA360.Security.Interfaces;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System;
using System.Data;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    [ApplyUppercase]
    public class PlanController : BaseController
    {
        private readonly IPlanService _planService;       // Service handling all plan business logic and data access
        private readonly ILoggerService _logger;          // Domain logger for structured audit and error logging
        private readonly IEncryptionService _encrypt;     // Encryption service for decrypting plan IDs passed from the UI
        private readonly IUserPreferenceService _userPreferenceService;
        private readonly IEmployerService _employerService;
        /// <summary>
        /// Initializes the PlanController with required service dependencies.
        /// </summary>
        /// <param name="planService">Service responsible for plan CRUD and lookup operations.</param>
        /// <param name="logger">Domain logger for capturing structured errors with caller and IP context.</param>
        /// <param name="encrypt">Encryption service used to safely decrypt plan IDs received from the client.</param>
        public PlanController(IPlanService planService, ILoggerService logger, IEncryptionService encrypt, IUserPreferenceService userPreferenceService, IEmployerService employerService)
        {
            _planService = planService;
            _logger = logger;
            _encrypt = encrypt;
            _userPreferenceService = userPreferenceService;
            _employerService = employerService;
        }

        #region Plan List Page Region

        /// <summary>
        /// Renders the main Plan List page with pagination, search, and sort support.
        /// Redirects to the Employer selection screen if no employer context is available in the session.
        /// The view mode (Table/Split) is persisted via a cookie and restored on each load.
        /// GET: /Plan/Index
        /// </summary>
        /// <param name="pageIndex">The current page number for pagination (default: 1).</param>
        /// <param name="search">Optional search term to filter plans (default: empty).</param>
        /// <param name="sortColumn">Zero-based index of the column to sort by (default: 0).</param>
        /// <param name="sortOrder">Sort direction — "asc" or "desc" (default: "asc").</param>
        /// <returns>
        /// The Index view populated with the paginated plan list,
        /// a redirect to SelectEmployer if no employer is in context,
        /// or an "Error" view on unexpected failure.
        /// </returns>
        [AuthorizePermission("Index")]
        public async Task<IActionResult> Index(int pageIndex = 1, string search = "", int sortColumn = 0, string sortOrder = "asc")
        {
            // Guard: redirect to employer selection if no employer context is active in the session
            var employerId = GetCurrentEmployerId();
            if (string.IsNullOrWhiteSpace(employerId) || employerId == "0")
                return RedirectToAction("Index", "SelectEmployer");

            try
            {
                // Restore the user's preferred view mode from the cookie, defaulting to Table
                var userId = GetCurrentUserId();
                string viewMode = await _userPreferenceService.GetViewModeAsync(userId, "plan") ?? "Table";
                ViewBag.ModuleKey = "plan";
                ViewBag.ViewMode = viewMode;

                // Build the pagination and filter parameters for the service call
                var paginationEntity = new PaginationEntity
                {
                    PageIndex = pageIndex,
                    PageSize = 20,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder,
                    fillingYear = GetCurrentFilingYear()
                };

                // Fetch the paginated plan list and associated metadata for the current employer and filing year
                var (plans, metadata) = await _planService.GetPlanList(
                    employerId,
                    paginationEntity.fillingYear,
                    paginationEntity,
                     null,
                    null);

                // Map service results into the view model
                var viewModel = new PlanListViewModel
                {
                    Plans = plans,
                    Metadata = metadata,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder,
                    ViewMode = viewMode
                };
                ViewBag.FilingYear = GetCurrentFilingYear();
                return View(viewModel);
            }
            catch (Exception ex)
            {
                // Log the failure with pagination context and client IP for traceability
                _logger.LogError(
                    ex,
                    nameof(Index),
                    nameof(PlanController),
                    $"Error loading plan list for pageIndex: {pageIndex}, search: {search}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                return View("Error");
            }
        }

        /// <summary>
        /// Returns a partial view of the plan list used for in-page refresh via AJAX.
        /// Supports pagination, search, sort, view mode (Table/Split), and filter parameters.
        /// The active view mode determines which partial template is rendered.
        /// GET: /Plan/PlanListPartial
        /// </summary>
        /// <param name="pageIndex">The current page number for pagination.</param>
        /// <param name="search">Optional search term to filter plans.</param>
        /// <param name="sortColumn">Zero-based index of the column to sort by.</param>
        /// <param name="sortOrder">Sort direction — "asc" or "desc".</param>
        /// <param name="PageSize">Number of records to display per page.</param>
        /// <param name="viewMode">Display mode — "Table" renders _PlanListPartial, "Split" renders _PlanListPartial_am.</param>
        /// <param name="filterPlanType">Optional plan type filter value.</param>
        /// <param name="filterFundingType">Optional funding type filter value.</param>
        /// <returns>
        /// A partial view of the plan list rendered in the appropriate template,
        /// or an "Error" view on unexpected failure.
        /// </returns>
        [AuthorizePermission("Index")]
        public async Task<IActionResult> PlanListPartial(int pageIndex, string search, int sortColumn, string sortOrder, int PageSize, string viewMode, string filterPlanType, string filterFundingType)
        {
            try
            {
                // Resolve the current employer context for scoping the plan query
                var employerId = GetCurrentEmployerId();                
                // Build the pagination and filter parameters for the service call
                var paginationEntity = new PaginationEntity
                {
                    PageIndex = pageIndex,
                    PageSize = PageSize,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder,
                    fillingYear = GetCurrentFilingYear()
                };

                // Fetch the filtered and paginated plan list for the current employer and filing year
                var (plans, metadata) = await _planService.GetPlanList(
                    employerId,
                    paginationEntity.fillingYear,
                    paginationEntity,
                    filterPlanType,
                    filterFundingType);

                // Map service results into the view model
                var viewModel = new PlanListViewModel
                {
                    Plans = plans,
                    Metadata = metadata,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder,
                    ViewMode = viewMode
                };

                // Render the appropriate partial based on the active view mode
                // "Split" mode uses an account-manager optimised layout; all others use the standard table layout
                if (string.Equals(viewMode, "Split", StringComparison.OrdinalIgnoreCase))
                    return PartialView("_PlanListPartial_am", viewModel);
                else
                    return PartialView("_PlanListPartial", viewModel);
            }
            catch (Exception ex)
            {
                // Log the failure with pagination context and client IP for traceability
                _logger.LogError(
                    ex,
                    nameof(PlanListPartial),
                    nameof(PlanController),
                    $"Error loading plan list partial for pageIndex: {pageIndex}, search: {search}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                return View("Error");
            }
        }

        /// <summary>
        /// Loads the read-only plan detail partial view for a given encrypted plan ID.
        /// Fetches all supporting lookup data (banding types, waiting periods, plan types, funding types)
        /// and attaches them to the plan model for the partial view to render dropdowns correctly.
        /// If no plan ID is provided, returns an empty (new) plan model with lookups pre-populated.
        /// GET: /Plan/PlanFetch?planId={encryptedPlanId}
        /// </summary>
        /// <param name="planId">The encrypted plan ID from the UI, or null/empty for a blank model.</param>
        /// <returns>
        /// The _PlanDetailPartial partial view populated with the plan and its lookup data,
        /// or a 400 Bad Request response on failure.
        /// </returns>
        public async Task<IActionResult> PlanFetch(string planId)
        {
            try
            {
                // Initialise an empty plan model as a safe default
                var existingPlan = new Plan();

                // Fetch all reference/lookup data required by the plan detail form dropdowns
                var planBandingType = await _planService.GetAll_Plan_Banding_TypeAsync();
                var planWaitingPeriod = await _planService.GetAll_Plan_Waiting_PeriodAsync();
                var planType = await _planService.GetAll_Plan_TypeAsync();
                var planFundingType = await _planService.GetAll_Plan_Funding_TypeAsync();

                // If a plan ID was provided, decrypt it and fetch the existing plan record
                if (!string.IsNullOrEmpty(planId))
                    existingPlan = await _planService.getPlan(_encrypt.Decrypt(planId));

                // Attach lookup collections to the plan model for use in the partial view
                existingPlan.Plan_Banding_Type = planBandingType;
                existingPlan.Plan_Waiting_Period = planWaitingPeriod;
                existingPlan.Plan_Type = planType;
                existingPlan.Plan_Funding_Type = planFundingType;

                return PartialView("_PlanForm", existingPlan);
              //  return PartialView("_PlanDetailPartial", existingPlan);
            }
            catch (Exception ex)
            {
                // Log the failure with the encrypted plan ID for traceability without exposing decrypted data
                _logger.LogError(
                    ex,
                    nameof(PlanFetch),
                    nameof(PlanController),
                    $"Error in PlanFetch for planId: {planId}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                return BadRequest("Invalid Plan Fetch.");
            }
        }

        /// <summary>
        /// Loads the Add/Edit plan modal partial view pre-populated with all lookup data.
        /// Fetches banding types, waiting periods, plan types, and funding types from the service
        /// and attaches them to a blank plan model for the add form to render dropdowns correctly.
        /// POST: /Plan/Add_New
        /// </summary>
        /// <returns>
        /// The _PlanAddEditPatial partial view with an empty plan model and lookup data,
        /// or a 400 Bad Request response on failure.
        /// </returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add_New()
        {
            try
            {
                // Initialise a blank plan model for the add form
                var plan = new Plan();

                // Fetch all reference/lookup data required by the plan add/edit form dropdowns
                var planBandingType = await _planService.GetAll_Plan_Banding_TypeAsync();
                var planWaitingPeriod = await _planService.GetAll_Plan_Waiting_PeriodAsync();
                var planType = await _planService.GetAll_Plan_TypeAsync();
                var planFundingType = await _planService.GetAll_Plan_Funding_TypeAsync();

                // Attach lookup collections to the blank plan model for the partial view
                plan.Plan_Banding_Type = planBandingType;
                plan.Plan_Waiting_Period = planWaitingPeriod;
                plan.Plan_Type = planType;
                plan.Plan_Funding_Type = planFundingType;
                plan.EmployerId = GetCurrentEmployerId();
                return PartialView("_PlanForm", plan);
              //  return PartialView("_PlanAddEditPatial", plan);
            }
            catch (Exception ex)
            {
                // Log the failure; no specific context ID available since this is a new-record operation
                _logger.LogError(
                    ex,
                    nameof(Add_New),
                    nameof(PlanController),
                    "Error loading Add New plan form.",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                return BadRequest("Invalid Plan Add.");
            }
        }

        /// <summary>
        /// Persists a new or updated plan record including its associated benefits banding rows.
        /// Benefits are converted from the JSON model into a typed DataTable before being passed
        /// to the service layer for bulk insert via a table-valued parameter.
        /// Null benefit field values are explicitly mapped to DBNull to prevent DataTable crashes.
        /// POST: /Plan/PlanSave
        /// </summary>
        /// <param name="plan">The full plan model (including benefits) submitted as JSON from the AJAX call.</param>
        /// <returns>
        /// 200 OK with { success: true } on successful save (triggers AJAX redirect),
        /// or 400 Bad Request with { success: false, message } on failure (triggers AJAX error handler).
        /// </returns>
        [HttpPost]
        public async Task<IActionResult> PlanSave([FromBody] Plan plan)
        {
            try
            {
                // Build a typed DataTable to hold the benefits banding rows for bulk DB insert
                var benefitsTable = new DataTable();
                benefitsTable.Columns.Add("Plan_start_value", typeof(string));
                benefitsTable.Columns.Add("Plan_end_value", typeof(string));
                benefitsTable.Columns.Add("Premium_start", typeof(DateTime));
                benefitsTable.Columns.Add("Premium_end", typeof(DateTime));
                benefitsTable.Columns.Add("Amount", typeof(decimal));

                // Map each benefit row into the DataTable, substituting DBNull for any null values
                // to prevent DataTable row addition from throwing on nullable fields
                if (plan.Benefits != null)
                {
                    foreach (var b in plan.Benefits)
                    {
                        benefitsTable.Rows.Add(
                            (object)b.Plan_start_value ?? DBNull.Value,
                            (object)b.Plan_end_value ?? DBNull.Value,
                            (object)b.Premium_start ?? DBNull.Value,
                            (object)b.Premium_end ?? DBNull.Value,
                            (object)b.Amount ?? DBNull.Value
                        );
                    }
                }

                // Delegate the upsert (insert or update) to the service layer with the benefits DataTable
                bool result = await _planService.InsertOrUpdate(plan, benefitsTable);

                // Return 200 OK so the AJAX success callback fires and redirects the page
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                // Log failure; return 400 so the AJAX error callback fires and surfaces the message to the user
                _logger.LogError(
                    ex,
                    nameof(PlanSave),
                    nameof(PlanController),
                    "Error saving plan.",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                return BadRequest(new { success = false, message = "Failed to save plan details." });
            }
        }

        /// <summary>
        /// Soft or hard deletes a plan record by its plan ID.
        /// Returns a JSON result indicating whether the delete operation succeeded,
        /// allowing the calling AJAX handler to update the UI accordingly.
        /// POST: /Plan/Delete
        /// </summary>
        /// <param name="planid">The plan ID of the record to delete.</param>
        /// <returns>
        /// JSON { success: true } on successful deletion,
        /// or JSON { success: false, message } on failure.
        /// </returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string planid)
        {
            try
            {
                // Delegate the delete operation to the service layer
                bool result = await _planService.PlanDelete(planid);

                return Json(new { success = result });
            }
            catch (Exception ex)
            {
                // Log the failure with the plan ID for traceability
                _logger.LogError(
                    ex,
                    nameof(Delete),
                    nameof(PlanController),
                    $"Error deleting plan for planId: {planid}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                return Json(new { success = false, message = "An unexpected error occurred while deleting the plan." });
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetPlanYears()
        {
            try
            {
                var years = await GetAllFilingYearsAsync();
                var list = years.Select(y => y.FilingYear ?? y.Year).Where(y => y > 0)
                                .Distinct().OrderByDescending(y => y).ToList();
                return Json(new { current = GetCurrentFilingYear(), years = list });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetPlanYears), nameof(PlanController), "Loading plan filing years.");
                return Json(new { current = GetCurrentFilingYear(), years = new List<int>() });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPlanCompareDetails(string planId, string filingYear)
        {
            try
            {
                var curEmp = GetCurrentEmployerId();
                var targetEmployerId = await _employerService.GetEmployerIdForYearAsync(curEmp, filingYear);
                if (string.IsNullOrEmpty(targetEmployerId)) return Content("");
                var targetIde = await _planService.GetPlanIdForCompareAsync(planId, targetEmployerId);
                if (string.IsNullOrEmpty(targetIde)) return Content("");
                var existingPlan = await _planService.getPlan(targetIde);
                if (existingPlan == null) return Content("");
                existingPlan.Plan_Banding_Type = await _planService.GetAll_Plan_Banding_TypeAsync();
                existingPlan.Plan_Waiting_Period = await _planService.GetAll_Plan_Waiting_PeriodAsync();
                existingPlan.Plan_Type = await _planService.GetAll_Plan_TypeAsync();
                existingPlan.Plan_Funding_Type = await _planService.GetAll_Plan_Funding_TypeAsync();
                return PartialView("_PlanForm", existingPlan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetPlanCompareDetails), nameof(PlanController), $"planId:{planId}, filingYear:{filingYear}");
                return StatusCode(500, "Error loading comparison details.");
            }
        }

        // Optional — only needed if you switch plan-compare.js to idsUrl instead of navFromDom.
        [HttpGet]
        public async Task<IActionResult> GetPlanIds()
        {
            try
            {
                var empId = GetCurrentEmployerId();
                var pe = new PaginationEntity { PageIndex = 1, PageSize = 1000 };
                var (plans, _) = await _planService.GetPlanList(empId, null, pe, null, null);
                var ids = (plans ?? new List<Plan>()).Select(p => p.ide).Where(x => !string.IsNullOrEmpty(x)).ToList();
                return Json(new { ids, filingYear = GetCurrentFilingYear() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetPlanIds), nameof(PlanController), "Loading plan ids for compare nav.");
                return Json(new { ids = new List<string>(), filingYear = GetCurrentFilingYear() });
            }
        }

        #endregion
    }
}