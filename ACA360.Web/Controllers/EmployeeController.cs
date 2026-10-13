using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Attributes;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Implementations;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Builders;
using ACA360.Web.Extensions;
using ACA360.Web.Filters;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," +
                       UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," +
                       UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," +
                       UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    [ApplyUppercase]
    public class EmployeeController : BaseController
    {
        private readonly IEmployeeService _employeeService;
        private readonly ILoggerService _logger;
        private readonly IPartnerRepository _partnerService;
        private readonly IEmployerService _employerService;
        private readonly IACALogicService _acaLogicService;
        private readonly EmployeeViewModelBuilder _builder;
        private readonly IUserPreferenceService _userPreferenceService;

        public EmployeeController(
            IEmployeeService employeeService, 
            ILoggerService logger,
            IPartnerRepository partnerService,
            IEmployerService employerService,
            IACALogicService acaLogicService,
            IUserPreferenceService userPreferenceService,
            EmployeeViewModelBuilder builder)
        {
            _employeeService = employeeService;            
            _logger = logger;
            _partnerService = partnerService;
            _employerService = employerService;
            _acaLogicService = acaLogicService;
            _userPreferenceService = userPreferenceService;
            _builder = builder;
        }

        // ===============================================
        // HELPER METHOD FOR PARTIAL VIEW REFRESHES
        // ===============================================

        /// <summary>
        /// Rebuilds the EmployeeListViewModel and populates necessary ViewBags 
        /// to safely return updated Partial Views without null references.
        /// </summary>
        private async Task<EmployeeListViewModel> GetUpdatedEmployeeViewModel(
            string employeeId, bool refreshFlags = false)
        {
            string filingYear = GetCurrentFilingYear();

            // Flags describe the employee as the engine last saw them, so any
            // save has to re-flag before they are read back below - otherwise
            // the panel redraws showing the state from before the edit.
            //
            // No employer here on purpose: this page identifies an employee by
            // id and year, and the procedure derives the employer from the
            // employee. The session's SelectedEmployerId is the employer picker,
            // not the employee in front of you.
            if (refreshFlags
                && int.TryParse(employeeId, out int reflagEmployeeId)
                && int.TryParse(filingYear, out int reflagYear))
            {
                await _acaLogicService.RecalculateFlagsForEmployeeAsync(reflagYear, reflagEmployeeId);
            }

            var employeeDetails = await _employeeService.GetEmployeeBasicDetails(employeeId, filingYear);
            var model = _builder.BuildDetailsViewModel(employeeDetails);

            // Repopulate ViewBags required by dropdowns in the partial views
            _builder.PopulateViewBag(ViewBag, filingYear);
            ViewBag.PlanDropdown = new SelectList(await _employeeService.GetAll_Plan_by_employer_Async(int.Parse(GetCurrentEmployerId() ?? "0")));
            // Global calls from BaseController
            ViewBag.CountryDropdown = await GetAllCountriesAsync();
            ViewBag.States = await GetAllStatesAsync();

            if (int.TryParse(employeeId, out int empIdInt) && int.TryParse(filingYear, out int filingYearInt))
            {
                model.EmployeeFlags = await _employeeService.GetEmployeeFlagDetailsAsync(empIdInt, filingYearInt);
            }

            return model;
        }

        /// <summary>
        /// The employee's change history (script 068): what each import changed, hand edits and code
        /// changes, newest first. Staff only.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = UserRoles.InternalTeam)]
        public async Task<IActionResult> History(int id, [FromServices] ACA360.BusinessLogic.Services.IChangeHistoryService history)
        {
            var denied = await CheckEmployeeAccessAsync(id, asJson: false);
            if (denied != null) return denied;
            try
            {
                return PartialView("_EmployeeHistory", await history.EmployeeHistoryAsync(id));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(History), nameof(EmployeeController), $"employeeId:{id}");
                return StatusCode(500, "The history could not be loaded.");
            }
        }

        // ===============================================
        // STANDARD VIEWS & LISTS
        // ===============================================

        [AuthorizePermission("Index")]
        public async Task<IActionResult> Index(int pageIndex = 1, string search = "", int sortColumn = 0, string sortOrder = "asc", string focusId = "")
        {
            try
            {
                var employerId = GetCurrentEmployerId();
                if (string.IsNullOrWhiteSpace(employerId) || employerId == "0")
                    return RedirectToSelectEmployer("Employees");

                var userId = GetCurrentUserId();
                string viewMode = await _userPreferenceService.GetViewModeAsync(userId, "employee") ?? "Table";
                // Consent feature deep-link — "View Employee Profile" from the Consent page
                // opens the split view preselected on one employee (see focus script at the end of Index.cshtml).
                if (!string.IsNullOrWhiteSpace(focusId))
                {
                    viewMode = "Split";
                    ViewBag.FocusEmployeeId = focusId;
                }
                ViewBag.ViewMode = viewMode;
                ViewBag.ModuleKey = "employee";

                string filingYear = GetCurrentFilingYear();
                ViewBag.FilingYear = filingYear;
               
                ViewBag.EmployerId = employerId;
                var (employees, paginationMeta) = await _employeeService.GetEmployeesAsync(
                    employerId, filingYear,
                    new PaginationEntity { PageIndex = pageIndex, PageSize = 25, Search = search, SortColumn = sortColumn, SortOrder = sortOrder, fillingYear = filingYear },
                    GetCurrentUserIdInt(),
                    new EmployeeFilterRequest { PageIndex = pageIndex, PageSize = 25, Search = search, SortColumn = sortColumn, SortOrder = sortOrder, FilingYear = filingYear });

                return View(new EmployeeListViewModel { Employees = employees, Metadata = paginationMeta, ViewMode = viewMode, Search = search, SortColumn = sortColumn, SortOrder = sortOrder });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Index), nameof(EmployeeController), "Error loading Employee list");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the Employee list.");
            }
        }
        // Prev/Next record navigation — whole filtered id list for the nav bar
        // (called via GET by the syncNavList override in Index.cshtml).
        [HttpGet]
        public async Task<IActionResult> GetFilteredEmployeeIds(EmployeeFilterRequest filter)
        {
            filter.FilingYear ??= GetCurrentFilingYear();
            filter.SortOrder ??= "asc";
            if (!await NarrowFilterEmployersAsync(filter))
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "You do not have access to that employer." });
            var ids = await _employeeService.GetFilteredEmployeeIdsAsync(
                GetCurrentEmployerId(), filter.FilingYear, GetCurrentUserIdInt(), filter);
            return Json(ids);
        }
        // end Prev/Next record navigation
        [AuthorizePermission("Index")]
        public async Task<IActionResult> EmployeeListPartial(EmployeeFilterRequest filter, string viewMode = "Split")
        {
            try
            {
                filter.PageIndex = filter.PageIndex > 0 ? filter.PageIndex : 1;
                filter.PageSize = filter.PageSize > 0 ? filter.PageSize : 25;
                filter.FilingYear ??= GetCurrentFilingYear();
                filter.SortOrder ??= "asc";

                if (!await NarrowFilterEmployersAsync(filter))
                    return StatusCode(StatusCodes.Status403Forbidden, "You do not have access to that employer.");

                var paginationEntity = new PaginationEntity
                {
                    PageIndex = filter.PageIndex,
                    PageSize = filter.PageSize,
                    Search = filter.Search ?? "",
                    SortColumn = filter.SortColumn,
                    SortOrder = filter.SortOrder,
                    fillingYear = filter.FilingYear
                };

                var (employees, paginationMeta) = await _employeeService.GetEmployeesAsync(
                    GetCurrentEmployerId(), filter.FilingYear, paginationEntity, GetCurrentUserIdInt(), filter);

                var model = new EmployeeListViewModel { Employees = employees, Metadata = paginationMeta, ViewMode = viewMode, Search = filter.Search, SortColumn = filter.SortColumn, SortOrder = filter.SortOrder };
                string partialName = string.Equals(viewMode, "Split", StringComparison.OrdinalIgnoreCase) ? "_EmployeeListPartial_am" : "_EmployeeListPartial";

                return PartialView(partialName, model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(EmployeeListPartial), nameof(EmployeeController), "Error loading Employee list partial");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error loading employee data.");
            }
        }

        [AuthorizePermission("Delete")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEmployee(int id)
        {
            try
            {
                if (id <= 0) return BadRequest(new { success = false, message = "Invalid ID." });

                // The id comes from the client: only employees of an employer this user can reach.
                var employee = await _employeeService.GetEmployeeByIdAsync(id);
                if (employee == null)
                    return NotFound(new { success = false, message = "Employee not found." });
                if (!await IsEmployerPermittedAsync(employee.EmployerId ?? 0))
                    return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "You do not have access to this employee." });

                await _employeeService.DeleteEmployeeSoft(id);
                return Json(new { success = true, message = "Employee deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(DeleteEmployee), nameof(EmployeeController), $"Error deleting employee with ID {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Error deleting the employee." });
            }
        }

        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> GetEmployeeBasicDetailsAsync(string? employeeId)
        {
            try
            {
                if (!int.TryParse(employeeId, out int id)) return BadRequest("Employee ID is required.");
                if (await CheckEmployeeAccessAsync(id) is { } denied) return denied;
                var model = await GetUpdatedEmployeeViewModel(employeeId);
                return PartialView("EmployeeDetails", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployeeBasicDetailsAsync), nameof(EmployeeController), "Error fetching basic details");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error fetching employee details.");
            }
        }
        /// <summary>
        /// Returns the blank default detail placeholder.
        /// Called when the list is empty or an employee detail load fails.
        /// </summary>
        [AuthorizePermission("Index")]
        [HttpGet]
        public IActionResult GetDefaultView()
        {
            return PartialView("_DefaultEmployeedetails");
        }
        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add_New()
        {
            try
            {
                _builder.PopulateViewBag(ViewBag, GetCurrentFilingYear());
                ViewBag.CountryDropdown = await _employeeService.GetAll_Country_Async();
                var states = await _partnerService.GetAllStatesAsync();
                ViewBag.States = new SelectList(states, "StateId", "StateName");
                return PartialView("_AddEmployeeBasicdetails", _builder.BuildNewEmployeeViewModel(GetCurrentEmployerId(), GetCurrentEmployerName(), GetCurrentFilingYear()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Add_New), nameof(EmployeeController), "Error generating new employee form.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error generating the form.");
            }
        }

        // ===============================================
        // PARTIAL VIEW REFRESH POST ACTIONS
        // ===============================================

        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeedetailSave([FromBody] EmployeeBasicDetails employeeBasicDetails)
        {
            try
            {
                if (employeeBasicDetails == null) return BadRequest(new { success = false });

                // EmployerId comes from the form, and usp_employee_basic_details_action writes
                // it to EmployeeCode on insert and on update, so it is checked like any other
                // client-supplied employer id; an existing employee is checked as well.
                if (!string.IsNullOrWhiteSpace(employeeBasicDetails.EmployerId)
                    && (!long.TryParse(employeeBasicDetails.EmployerId, out long employerId)
                        || !await IsEmployerPermittedAsync(employerId)))
                    return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "You do not have access to that employer." });

                if (employeeBasicDetails.EmployeeID != "0"
                    && await CheckEmployeeSaveAccessAsync(employeeBasicDetails) is { } denied)
                    return denied;

                await _employeeService.InsertOrUpdateEmployeeBasicDetails(employeeBasicDetails);

                if (employeeBasicDetails.EmployeeID != "0")
                {
                    // Not "0", so CheckEmployeeSaveAccessAsync above has parsed it as a real employee id.
                    var model = await GetUpdatedEmployeeViewModel(employeeBasicDetails.EmployeeID!);
                    return PartialView("_EmployeeBasicInfo", model);
                }
                else
                {
                    return Json(new { success = true, message = "Employee added successfully." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(EmployeedetailSave), nameof(EmployeeController), $"Failed to save employee. EmployeeID: {employeeBasicDetails?.EmployeeID}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = ex.InnerException?.Message ?? ex.Message });
            }
        }

        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeHireSave([FromBody] EmployeeBasicDetails employee)
        {
            try
            {
                if (await CheckEmployeeSaveAccessAsync(employee) is { } denied) return denied;
                await _employeeService.InsertOrUpdateemployeeHireDetails(employee.ToHireDataTable(),employee.EmployeeID!);
                var model = await GetUpdatedEmployeeViewModel(employee.EmployeeID!, refreshFlags: true);
                return PartialView("_HireInfo", model);
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(EmployeeHireSave)); }
        }

        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeStatusSave([FromBody] EmployeeBasicDetails employee)
        {
            try
            {
                if (await CheckEmployeeSaveAccessAsync(employee) is { } denied) return denied;
                await _employeeService.InsertOrUpdateemployeestatus(employee.ToStatusDataTable(), employee.EmployeeID!);
                var model = await GetUpdatedEmployeeViewModel(employee.EmployeeID!, refreshFlags: true);
                return PartialView("_Status", model);
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(EmployeeStatusSave)); }
        }

        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeePayrollSave([FromBody] EmployeeBasicDetails employee)
        {
            try
            {
                if (await CheckEmployeeSaveAccessAsync(employee) is { } denied) return denied;
                await _employeeService.InsertOrUpdateemployeepayrollInfo(employee.ToPayrollDataTable(), employee.EmployeeID!);
                var model = await GetUpdatedEmployeeViewModel(employee.EmployeeID!, refreshFlags: true);
                return PartialView("_Payroll", model);
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(EmployeePayrollSave)); }
        }

        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeMedicalSave([FromBody] EmployeeBasicDetails employee)
        {
            try
            {
                if (await CheckEmployeeSaveAccessAsync(employee) is { } denied) return denied;
                await _employeeService.InsertOrUpdateemployeemedical(employee.ToMedicalDataTable(), employee.EmployeeID!);
                var model = await GetUpdatedEmployeeViewModel(employee.EmployeeID!, refreshFlags: true);
                return PartialView("_Medical", model);
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(EmployeeMedicalSave)); }
        }

        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeCobraSave([FromBody] EmployeeBasicDetails employee)
        {
            try
            {
                if (await CheckEmployeeSaveAccessAsync(employee) is { } denied) return denied;
                await _employeeService.InsertOrUpdatecobra(employee.ToCobraDataTable(), employee.EmployeeID!);
                var model = await GetUpdatedEmployeeViewModel(employee.EmployeeID!, refreshFlags: true);
                return PartialView("_Cobra", model);
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(EmployeeCobraSave)); }
        }

        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeUnionSave([FromBody] EmployeeBasicDetails employee)
        {
            try
            {
                if (await CheckEmployeeSaveAccessAsync(employee) is { } denied) return denied;
                await _employeeService.InsertOrUpdateunion(employee.ToUnionDataTable(), employee.EmployeeID!);
                var model = await GetUpdatedEmployeeViewModel(employee.EmployeeID!, refreshFlags: true);
                return PartialView("_Union", model);
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(EmployeeUnionSave)); }
        }

        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeRetireeSave([FromBody] EmployeeBasicDetails employee)
        {
            try
            {
                if (await CheckEmployeeSaveAccessAsync(employee) is { } denied) return denied;
                await _employeeService.InsertOrUpdateretiree(employee.ToRetireeDataTable(), employee.EmployeeID!);
                var model = await GetUpdatedEmployeeViewModel(employee.EmployeeID!, refreshFlags: true);
                return PartialView("_Retiree", model);
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(EmployeeRetireeSave)); }
        }

        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeDependentSave([FromBody] EmployeeBasicDetails employee)
        {
            try
            {
                if (await CheckEmployeeSaveAccessAsync(employee) is { } denied) return denied;
                await _employeeService.InsertOrUpdateemployeeDependentDetails(employee.ToDependentDataTable(), employee.EmployeeID!);
                var model = await GetUpdatedEmployeeViewModel(employee.EmployeeID!, refreshFlags: true);
                return PartialView("_Dependents", model);
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(EmployeeDependentSave)); }
        }

        // Renamed from SaveMonthlyCodes to align with earlier front-end instructions
        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveEmployeeCodes([FromBody] EmployeeCode model)
        {
            try
            {
                if (model == null || model.EmployeeCodeID == 0) return BadRequest(new { success = false });
                if (await CheckEmployeeAccessAsync(model.EmployeeId) is { } denied) return denied;
                // The second argument is who made the change: it is written to the code
                // audit log as ChangedByUserId.
                await _acaLogicService.UpdateManualCodesAsync(model, GetCurrentUserId());
                var vm = await GetUpdatedEmployeeViewModel(model.EmployeeId.ToString(), refreshFlags: true);
                return PartialView("_EmployeeCodes", vm);
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(SaveEmployeeCodes)); }
        }

        // Renamed from UpdateCoveredIndividualCoverage to align with URL defined in the partial view
        [AuthorizePermission("Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveCoveredIndividuals([FromBody] CoveredIndividualModel model)
        {
            try
            {
                if (model == null) return BadRequest(new { success = false, message = "Invalid data payload." });

                // sp_UpdateCoveredIndividualCoverage updates by the CoveredIndividualCode id
                // alone, so scope is decided by the employee that row belongs to, and the
                // EmployeeId sent with it (used for the redraw) has to be that employee.
                var ownerId = await _employeeService.GetEmployeeIdForCoveredIndividualCodeAsync(model.Id);
                if (ownerId == null) return NotFound(new { success = false, message = "Covered individual not found." });
                if (await CheckEmployeeAccessAsync(ownerId.Value) is { } denied) return denied;
                if (model.EmployeeId != ownerId.Value.ToString())
                    return BadRequest(new { success = false, message = "Covered individual does not belong to this employee." });

                if (model.Birthday == DateTime.MinValue) model.Birthday = null;
                if (model.CoverageStartDate == DateTime.MinValue) model.CoverageStartDate = null;
                if (model.CoverageEndDate == DateTime.MinValue) model.CoverageEndDate = null;

                bool isSuccess = await _employeeService.UpdateCoveredIndividualCoverageAsync(model);

                if (isSuccess)
                {
                    var vm = await GetUpdatedEmployeeViewModel(model.EmployeeId, refreshFlags: true);
                    return PartialView("_CoveredIndividuals", vm);
                }

                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "No records were updated. Please check the ID." });
            }
            catch (Exception ex)
            {
                return HandleSaveError(ex, nameof(SaveCoveredIndividuals));
            }
        }

        // ===============================================
        // DROPDOWNS & FILTERS
        // ===============================================

        // Emp_ID here is a comma-separated list of EMPLOYER ids.
        [HttpGet]
        [RequireEmployerScope("Emp_ID")]
        public async Task<IActionResult> drp_plan(string? Emp_ID)
        {
            try
            {
                var ids = (Emp_ID ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(s => int.TryParse(s, out _))
                    .ToList();

                if (ids.Count == 0)
                    return Json(await _employeeService.GetAll_Plan_by_employer_Async(int.Parse(GetCurrentEmployerId() ?? "0")));

                return Json(await _employeeService.GetAll_Plan_by_employers_Async(string.Join(",", ids)));
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(drp_plan)); }
        }

        // Emp_ID here is an EMPLOYER id.
        [HttpGet]
        [RequireEmployerScope("Emp_ID")]
        public async Task<IActionResult> drp_employer(int? Emp_ID)
        {
            try
            {
                int employerId = Emp_ID > 0 ? Emp_ID.Value : int.Parse(GetCurrentEmployerId() ?? "0");
                // sp_EmployerList with no employer lists every employer.
                if (employerId <= 0) return Json(new List<EmployerLookupDto>());
                return Json(await _employeeService.GetAll_Employer_by_employer_Async(employerId));
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(drp_employer)); }
        }

        // ===============================================
        // ACA METRICS & DETAILS
        // ===============================================

        [HttpGet]
        public async Task<IActionResult> Details(int id, int year)
        {
            try
            {
                if (id <= 0 || year <= 0) return BadRequest("Invalid ID or Year.");
                if (await CheckEmployeeAccessAsync(id, asJson: false) is { } denied) return denied;

                // The 1095-C page shows the employee record itself (hire / termination dates,
                // status, coverage elected), which the tab-view details do not carry.
                var employee = await _employeeService.GetEmployeeByIdAsync(id);
                if (employee == null) return NotFound();

                var employer = await _employerService.GetEmployerDetailsByIdAsync(employee.EmployerId?.ToString() ?? "");
                var codes = await _acaLogicService.GetCodesForEmployeeAsync(id, year);

                return View(_builder.Build1095ViewModel(employee, employer, codes, year));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Details), nameof(EmployeeController), "Error loading details view");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error loading details.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeRisk(int id, int year)
        {
            try
            {
                if (id <= 0 || year <= 0) return BadRequest("Invalid input values.");
                if (await CheckEmployeeAccessAsync(id) is { } denied) return denied;
                var riskData = await _acaLogicService.GetAtRiskEmployeesAsync(0, year);
                var empRisk = System.Linq.Enumerable.FirstOrDefault(riskData, e => e.EmployeeId == id);
                return Json(new { isAtRisk = empRisk != null, penalty = empRisk?.EstimatedPenalty ?? 0, badMonths = empRisk?.BadMonthCount ?? 0 });
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(GetEmployeeRisk)); }
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeRiskData(int id, int year)
        {
            try
            {
                if (id <= 0 || year <= 0) return BadRequest("Invalid input data.");
                if (await CheckEmployeeAccessAsync(id) is { } denied) return denied;
                var riskData = await _acaLogicService.GetAtRiskEmployeesAsync(0, year);
                var empRisk = System.Linq.Enumerable.FirstOrDefault(riskData, x => x.EmployeeId == id);
                return Json(new { isAtRisk = empRisk != null, badMonthCount = empRisk?.BadMonthCount ?? 0, estimatedPenalty = empRisk?.EstimatedPenalty ?? 0 });
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(GetEmployeeRiskData)); }
        }

        // ===============================================
        // QUICK FILTER STATS & FLAGGED EXPORT
        // ===============================================

        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> GetEmployeeQuickStats(EmployeeFilterRequest filter)
        {
            try
            {
                if (!int.TryParse(GetCurrentFilingYear(), out int filingYearInt))
                    filingYearInt = DateTime.Now.Year;

                if (!await NarrowFilterEmployersAsync(filter))
                    return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "You do not have access to that employer." });

                var stats = await _employeeService.GetEmployeeQuickStatsAsync(
                    GetCurrentEmployerId(), filingYearInt, filter);

                return Json(stats);
            }
            catch (Exception ex) { return HandleSaveError(ex, nameof(GetEmployeeQuickStats)); }
        }

        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> ExportFlaggedEmployees(string? employerIds)
        {
            try
            {
                if (!int.TryParse(GetCurrentFilingYear(), out int filingYearInt))
                    filingYearInt = DateTime.Now.Year;

                // sp_FlaggedEmployees_Export treats @EmployerIDs and @Employer_ID as
                // ALTERNATIVES, not as a pair to intersect: when the list is present
                // the session employer is ignored entirely. So a client-supplied list
                // has to be narrowed to the caller's own entitlement before it goes
                // anywhere near the procedure — the export carries unmasked SSNs.
                var requested = ParseEmployerIds(employerIds);
                var permitted = await FilterPermittedEmployerIdsAsync(requested);

                if (requested.Count > 0 && permitted.Count < requested.Count)
                {
                    _logger.LogError(new UnauthorizedAccessException("Employer scope denied"),
                        nameof(ExportFlaggedEmployees), nameof(EmployeeController),
                        $"User {GetCurrentUserId()} requested {requested.Count} employer(s) for the flagged " +
                        $"export but is entitled to {permitted.Count}. Dropped: " +
                        string.Join(",", requested.Except(permitted)));
                }

                // An explicit list that survives filtering empty means every id was
                // out of scope. Fall back to the session employer rather than to the
                // procedure's "no list supplied" branch, which would silently widen
                // the export to the session employer plus its affiliates anyway.
                if (requested.Count > 0 && permitted.Count == 0)
                    return Forbid();

                var bytes = await _employeeService.GenerateFlaggedEmployeesExportAsync(
                    GetCurrentEmployerId(), permitted, filingYearInt);

                string fileName = $"Flagged_Employees_{GetCurrentEmployerName()}_{filingYearInt}.xlsx".Replace(' ', '_');
                return File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(ExportFlaggedEmployees), nameof(EmployeeController), "Error generating flagged employee export");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error generating flagged employee export.");
            }
        }

        private static List<long> ParseEmployerIds(string? csv)
        {
            return (csv ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => long.TryParse(s, out _))
                .Select(long.Parse)
                .ToList();
        }

        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> ExportAffordability()
        {
            try
            {
                var employerId = GetCurrentEmployerId();
                if (string.IsNullOrWhiteSpace(employerId) || employerId == "0")
                    return RedirectToSelectEmployer("the affordability export");

                string filingYear = GetCurrentFilingYear();
                if (!int.TryParse(employerId, out int employerIdInt) || !int.TryParse(filingYear, out int filingYearInt))
                    return BadRequest("Invalid employer or filing year.");

                var bytes = await _acaLogicService.GenerateAffordabilityExportAsync(employerIdInt, filingYearInt);

                string fileName = $"Affordability_{GetCurrentEmployerName()}_{filingYear}.xlsx".Replace(' ', '_');
                return File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(ExportAffordability), nameof(EmployeeController), "Error generating affordability export");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error generating affordability export.");
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetEmployeeYears()
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
                _logger.LogError(ex, nameof(GetEmployeeYears), nameof(EmployeeController), "Loading employee filing years.");
                return Json(new { current = GetCurrentFilingYear(), years = new List<int>() });
            }
        }

        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> GetEmployeeCompareDetails(string employeeId, string filingYear)
        {
            try
            {
                // The comparison target is looked up inside the session employer, but the
                // source employee's SSN drives the match, so the source is checked too.
                if (!int.TryParse(employeeId, out int sourceId)) return BadRequest("Employee ID is required.");
                if (await CheckEmployeeAccessAsync(sourceId) is { } denied) return denied;

                var targetId = await _employeeService.GetEmployeeIdForCompareAsync(
                    employeeId, filingYear, GetCurrentEmployerId(), GetCurrentUserIdInt());
                if (targetId == null)
                {
                    var mappedEmp = await _employerService.GetEmployerIdForYearAsync(GetCurrentEmployerId(), filingYear);
                    if (!string.IsNullOrEmpty(mappedEmp))
                        targetId = await _employeeService.GetEmployeeIdForCompareAsync(
                            employeeId, filingYear, mappedEmp, GetCurrentUserIdInt());
                }
                if (targetId == null) return Content("");
                var model = await GetEmployeeViewModelForYear(targetId.Value.ToString(), filingYear);
                return PartialView("EmployeeDetails", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployeeCompareDetails), nameof(EmployeeController), $"employeeId:{employeeId}, filingYear:{filingYear}");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error loading comparison details.");
            }
        }

        private async Task<EmployeeListViewModel> GetEmployeeViewModelForYear(string employeeId, string filingYear)
        {
            var employeeDetails = await _employeeService.GetEmployeeBasicDetails(employeeId, filingYear);
            var model = _builder.BuildDetailsViewModel(employeeDetails);
            _builder.PopulateViewBag(ViewBag, filingYear);
            ViewBag.PlanDropdown = new SelectList(await _employeeService.GetAll_Plan_by_employer_Async(int.Parse(GetCurrentEmployerId() ?? "0")));
            ViewBag.CountryDropdown = await GetAllCountriesAsync();
            ViewBag.States = await GetAllStatesAsync();
            if (int.TryParse(employeeId, out int empIdInt) && int.TryParse(filingYear, out int fyInt))
                model.EmployeeFlags = await _employeeService.GetEmployeeFlagDetailsAsync(empIdInt, fyInt);
            return model;
        }
        // ===============================================
        // PRIVATE HELPERS
        // ===============================================

        /// <summary>
        /// Scope check for the tab saves. Checking EmployeeID alone is not enough: every
        /// child row carries its own EmployeeCodeId, and the procedures match and insert
        /// on that, so each one must belong to the same employee.
        /// </summary>
        private async Task<IActionResult?> CheckEmployeeSaveAccessAsync(EmployeeBasicDetails? employee)
        {
            if (employee == null || !int.TryParse(employee.EmployeeID, out int employeeId) || employeeId <= 0)
                return BadRequest(new { success = false, message = "Invalid employee." });

            if (await CheckEmployeeAccessAsync(employeeId) is { } denied) return denied;

            var sentCodeIds = (employee.EmployeeHireDetail ?? new()).Select(h => (int?)h.EmployeeCodeId)
                .Concat((employee.EmployeeStatus ?? new()).Select(s => (int?)s.EmployeeCodeId))
                .Concat((employee.EmployeePayrollDetails ?? new()).Select(p => (int?)p.EmployeeCodeId))
                .Concat((employee.EmployeeEnrollment ?? new()).Select(e => (int?)e.EmployeeCodeId))
                .Concat((employee.CoveredIndividuals ?? new()).Select(c => (int?)c.EmployeeCodeId))
                .Where(id => id > 0)
                .Select(id => id!.Value)
                .ToHashSet();

            if (sentCodeIds.Count == 0) return null;

            var ownCodeIds = await _employeeService.GetEmployeeCodeIdsAsync(employeeId);
            if (!sentCodeIds.IsSubsetOf(ownCodeIds))
            {
                _logger.LogError(new UnauthorizedAccessException("Employee scope denied"),
                    nameof(CheckEmployeeSaveAccessAsync), nameof(EmployeeController),
                    $"User {GetCurrentUserId()} saved employee {employeeId} with EmployeeCodeId(s) " +
                    $"{string.Join(",", sentCodeIds.Except(ownCodeIds))} that belong to another employee.");
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "You do not have access to this employee." });
            }

            return null;
        }

        /// <summary>
        /// sp_EmployeeList_Filter and sp_Employee_QuickStats use @EmployerIDs INSTEAD of
        /// the session employer when it is present (as sp_FlaggedEmployees_Export does),
        /// so a client-supplied list is narrowed before it reaches them. Affiliates of the
        /// session employer are what drp_employer offers the filter, so they count as in
        /// scope alongside the permitted set. False when ids were asked for and none survive.
        /// </summary>
        private async Task<bool> NarrowFilterEmployersAsync(EmployeeFilterRequest filter)
        {
            if (filter.Employer_IDs == null || filter.Employer_IDs.Count == 0) return true;

            var inScope = await GetPermittedEmployerIdsAsync();
            if (int.TryParse(GetCurrentEmployerId(), out int sessionEmployerId) && sessionEmployerId > 0)
                inScope.UnionWith((await _employeeService.GetAll_Employer_by_employer_Async(sessionEmployerId)).Select(e => (long)e.Id));

            filter.Employer_IDs = filter.Employer_IDs.Where(inScope.Contains).Distinct().ToList();
            return filter.Employer_IDs.Count > 0;
        }

        private IActionResult HandleSaveError(Exception ex, string methodName)
        {
            _logger.LogError(ex, methodName, nameof(EmployeeController), $"Error in {methodName}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred." });
        }

        private int? GetCurrentUserIdInt()
        {
            var claim = User.FindFirstValue("UserId");
            return int.TryParse(claim, out int parsedId) ? parsedId : null;
        }
    }
}