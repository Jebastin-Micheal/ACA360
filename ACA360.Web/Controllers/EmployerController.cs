using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.Core.Attributes;
using ACA360.Core.Constants;
using ACA360.Core.Extensions;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Implementations;
using ACA360.Web.Builders;
using ACA360.Web.Extensions;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Linq;
using System.Threading.Tasks;
using ACA360.Web.Filters;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," +
                       UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," +
                       UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," +
                       UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class EmployerController : BaseController
    {
        private readonly IEmployerService _employerService;
        private readonly ILoggerService _logger;
        private readonly IAnomalyDetectionService _anomalyDetectionService;
        private readonly EmployerViewModelBuilder _builder;
        private readonly IViewAsService _viewAsService;
        private readonly IDashboardService _dashboardService;
        private readonly IUserPreferenceService _userPreferenceService;

        public EmployerController(
            IEmployerService employerService, 
            ILoggerService logger,
            IAnomalyDetectionService anomalyDetectionService,
            EmployerViewModelBuilder builder, IViewAsService viewAsService, IDashboardService dashboardService, IUserPreferenceService userPreferenceService)
        {
            _employerService = employerService;
            _logger = logger;
            _anomalyDetectionService = anomalyDetectionService;
            _builder = builder;
            _viewAsService= viewAsService;
            _dashboardService = dashboardService;
            _userPreferenceService = userPreferenceService;

        }

        // ===================================================================
        // 1. MAIN EMPLOYER LIST & DASHBOARD
        // ===================================================================

        [AuthorizePermission("Index")]
        public async Task<IActionResult> IndexAsync(int pageIndex = 1, string search = "", int sortColumn = 0, string sortOrder = "asc", string TypeFilter = "")
        {
            var employerId = GetCurrentEmployerId();
            if (string.IsNullOrWhiteSpace(employerId) || employerId == "0")
                return RedirectToAction("Index", "SelectEmployer");

            try
            {
                var userId = GetCurrentUserId();
                string viewMode = await _userPreferenceService.GetViewModeAsync(userId, "employer") ?? "Table";
                ViewBag.ModuleKey = "employer";
                var pagination = new PaginationEntity { PageIndex = pageIndex, PageSize = 10, SortColumn = sortColumn, SortOrder = sortOrder, fillingYear = GetCurrentFilingYear() };
                var (employers, metadata) = await _employerService.GetEmployersAsync(employerId, pagination, "all");

                ViewBag.ViewMode = viewMode;
                return View(new EmployerListViewModel
                {
                    Employers = employers, Metadata = metadata,
                    Search = search, SortColumn = sortColumn, SortOrder = sortOrder, ViewMode = viewMode
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(IndexAsync), nameof(EmployerController), $"pageIndex:{pageIndex}, search:{search}");
                return View("Error");
            }
        }

        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> Dashboard(int id, int year = 0)
        {
            try
            {
                var ctx = CurrentContext;                       // Pack 3 Part 2, section 8
                var role = ctx.EffectiveRole;
                var scopeUserId = ctx.ScopeUserId;

                if (year <= 0) int.TryParse(GetCurrentFilingYear(), out year);
                if (year <= 0) year = DateTime.Now.Year;

                // Permission first, and only permission. The full permitted set —
                // not filtered by year.
                var permitted = await _viewAsService.GetPermittedEmployersAsync(ctx);

                if (ctx.Mode == ImpersonationMode.Employer) id = ctx.TargetId;
                if (id <= 0) int.TryParse(GetCurrentEmployerId(), out id);
                if (id <= 0) int.TryParse(permitted.FirstOrDefault()?.Id, out id);

                var idText = id.ToString();

                // 403 means "you may not see this employer". Whether it has a row for
                // the chosen year is answered below with an empty state — conflating
                // the two turns an ordinary year switch into an access denial.
                if (id > 0 && !permitted.Any(e => e.Id == idText))
                {
                    _logger.LogError(
                        new UnauthorizedAccessException("Employer scope denied"),
                        nameof(Dashboard), nameof(EmployerController),
                        $"User {ctx.ActorUserId} (effective {role}) requested employer {id}.");
                    return Forbid();
                }

                // Employer rows are per filing year, so the picker only offers the ones
                // that exist in the selected year.
                var forYear = permitted
                    .Where(e => string.IsNullOrEmpty(e.FilingYear) || e.FilingYear == year.ToString())
                    .ToList();

                var vm = new EmployerDashboardPageViewModel
                {
                    Context = ctx,
                    FilingYear = year,
                    AvailableYears = await GetAllFilingYearsAsync(),
                    SelectableEmployers = forYear.Any() ? forYear : permitted,

                    // Null when the selected employer has no row for this year. The view
                    // renders "No employer record for plan year N" and the picker above
                    // still lists the employers that do exist, so recovery is one click.
                    Employer = forYear.FirstOrDefault(e => e.Id == idText)
                };

                vm.ViewAs = await _viewAsService.GetOptionsAsync(ctx.ActorUserId, ctx.ActorRole);

                if (vm.ShowPortfolioBand)
                {
                    vm.Portfolio = await _dashboardService.GetPortfolioCountsAsync(scopeUserId, year);
                    //vm.ProcessRows = await _dashboardService.GetPortfolioProcessStepsAsync(scopeUserId, year);
                    // Search and sort are client-side, so send the whole book rather
                    // than the worst 25 — otherwise the search only ever sees the top
                    // of the list and quietly misses the rest.
                    vm.ProcessRows = await _dashboardService.GetPortfolioProcessStepsAsync(scopeUserId, year, 500);
                }

                if (vm.Employer != null && id > 0)
                {
                    vm.Employer360 = await _dashboardService.GetEmployerDashboardParityAsync(
                                         id, year, ctx.ActorUserId);
                    vm.Plans = await _dashboardService.GetPlanSummaryAsync(id, year);

                    // The 360 view supplies the ALE trend, the coverage map and the
                    // workforce split — the three things the parity DTO does not carry.
                    var e360 = await _employerService.GetEmployer360Async(id, year);
                    if (e360 != null)
                    {
                        vm.AleTrend = e360.AleTrend ?? new List<AleTrendMonthDto>();

                        //// MonthlyCoverage is Dictionary<int,string> of "Covered" / other.
                        //var cov = new int[12];
                        //if (e360.MonthlyCoverage != null)
                        //{
                        //    for (int m = 1; m <= 12; m++)
                        //    {
                        //        e360.MonthlyCoverage.TryGetValue(m, out var status);
                        //        cov[m - 1] = string.Equals(status, "Covered",
                        //            StringComparison.OrdinalIgnoreCase) ? 100 : 0;
                        //    }
                        //}
                        //vm.CoveragePercent = cov;
                        var pen = await _dashboardService.GetPenaltySummaryAsync(id, year);
                        if (pen != null)
                        {
                            vm.PenaltyTypeA = (decimal)pen.TypeAPenalty;
                            vm.PenaltyTypeB = (decimal)pen.TypeBPenalty;
                            vm.PenaltyExposure = (decimal)pen.TotalPenalty;
                            vm.PenaltyTypeATriggered = (bool)pen.TypeATriggered;
                            vm.PenaltyChargeableFTEs = (int)pen.ChargeableFTEs;
                            vm.PenaltyApplicableMonths = (int)pen.ApplicableMonths;
                            vm.PenaltyTypeBViolations = (int)pen.TypeBViolations;
                            vm.PenaltyCalculatedDate = pen.CalculatedDate;
                            vm.PenaltyType = vm.PenaltyLabel;
                        }
                        vm.CoveragePercent = await _dashboardService.GetCoverageMapAsync(id, year);
                        vm.WorkforceFullTime = e360.FullTimeCount;
                        vm.WorkforcePartTime = e360.PartTimeCount;
                        vm.WorkforceVariable = e360.VariableHoursCount;
                        vm.WorkforceCobra = e360.COBRACount;
                    }

                    SetCurrentEmployerId(id.ToString());
                }

                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Dashboard), nameof(EmployerController),
                    $"Failed to load dashboard for employer {id}, year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred while loading the dashboard.");
            }
        }

        // ===================================================================
        // 2. PARTIAL VIEWS FOR TABS & MODALS
        // ===================================================================

        [AuthorizePermission("Index")]
        public async Task<IActionResult> EmployerListPartial([FromQuery] PaginationEntity paginationEntity, string viewMode = "Table")
        {
            try
            {
                if (paginationEntity.PageSize <= 0) paginationEntity.PageSize = 10;
                if (paginationEntity.PageIndex <= 0) paginationEntity.PageIndex = 1;
                paginationEntity.fillingYear = GetCurrentFilingYear();

                var (employers, metadata) = await _employerService.GetEmployersAsync(GetCurrentEmployerId(), paginationEntity, paginationEntity.SearchFields ?? "all");

                var viewModel = new EmployerListViewModel
                {
                    Employers = employers, Metadata = metadata,
                    Search = paginationEntity.Search, SortColumn = paginationEntity.SortColumn,
                    SortOrder = paginationEntity.SortOrder, SearchFields = paginationEntity.SearchFields ?? "all",
                    ViewMode = viewMode
                };

                return viewMode == "Split"
                    ? PartialView("_EmployerListPartial_am", viewModel)
                    : PartialView("_EmployerListPartial", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(EmployerListPartial), nameof(EmployerController), $"pageIndex:{paginationEntity.PageIndex}");
                return View("Error");
            }
        }

        [AuthorizePermission("Index")]
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetCommonDetails(string EmployerId, string filingYear)
        {
            try
            {
                var employer = await _employerService.GetCommonEmployerDetailsAsync(EmployerId, filingYear);
                if (employer == null) return PartialView("_EmployerCommonDetailsPartial", null);

                var info = !string.IsNullOrEmpty(employer.Id) ? await _employerService.GetEmployerInfoAsync(employer.Id) : null;
                var dropdowns = info != null ? await _employerService.GetEmployerDropdownDataAsync(employer.Id, info.FirmId, info.BrokerId, false) : new EmployerDropdownDataModel();
                var billing   = info != null ? await _employerService.GetEmployerDropdownDataAsync(employer.Id, info.FirmId, info.BrokerId, true) : new EmployerDropdownDataModel();
                ViewBag.IsAffiliate = (await _employerService.GetPrimaryEmployerIdAsync(EmployerId)).HasValue;
                return PartialView("_EmployerCommonDetailsPartial", _builder.BuildDetailsViewModel(employer, info, dropdowns, billing));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetCommonDetails), nameof(EmployerController), $"EmployerId:{EmployerId}");
                return StatusCode(500, "An error occurred while loading employer details.");
            }
        }

        // Split-screen comparison — compare-pane detail: resolves the same business
        // (by EIN) in another filing year, since each (employer, year) is a distinct id.
        [AuthorizePermission("Index")]
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetCompareDetails(string EmployerId, string filingYear)
        {
            try
            {
                var employer = await _employerService.GetEmployerDetailsForCompareAsync(EmployerId, filingYear);
                if (employer == null) return PartialView("_EmployerCommonDetailsPartial", null);

                var info = !string.IsNullOrEmpty(employer.Id) ? await _employerService.GetEmployerInfoAsync(employer.Id) : null;
                var dropdowns = info != null ? await _employerService.GetEmployerDropdownDataAsync(employer.Id, info.FirmId, info.BrokerId, false) : new EmployerDropdownDataModel();
                var billing = info != null ? await _employerService.GetEmployerDropdownDataAsync(employer.Id, info.FirmId, info.BrokerId, true) : new EmployerDropdownDataModel();
                ViewBag.IsAffiliate = (await _employerService.GetPrimaryEmployerIdAsync(EmployerId)).HasValue;
                return PartialView("_EmployerCommonDetailsPartial", _builder.BuildDetailsViewModel(employer, info, dropdowns, billing));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetCompareDetails), nameof(EmployerController), $"EmployerId:{EmployerId}, filingYear:{filingYear}");
                return StatusCode(500, "An error occurred while loading comparison details.");
            }
        }

        // Split-screen comparison — year list for the filing-year comparison picker.
        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> GetFilingYears()
        {
            try
            {
                var years = await GetAllFilingYearsAsync();
                var list = years
                    .Select(y => y.FilingYear ?? y.Year)
                    .Where(y => y > 0)
                    .Distinct()
                    .OrderByDescending(y => y)
                    .ToList();
                return Json(new { current = GetCurrentFilingYear(), years = list });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetFilingYears), nameof(EmployerController), "Loading filing years for comparison picker.");
                return Json(new { current = GetCurrentFilingYear(), years = new List<int>() });
            }
        }

        // Split-screen comparison — full ordered employer-id list for the current
        // filter/sort (all pages), so the detail Prev/Next nav walks the whole filtered
        // list, not just the current page. Probe the count with PageSize=1, then fetch
        // exactly that many (never int.MaxValue — sp_EmployerList's paging math can't take it).
        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> GetFilteredEmployerIds([FromQuery] PaginationEntity paginationEntity)
        {
            try
            {
                var empId = GetCurrentEmployerId();
                var searchFields = paginationEntity.SearchFields ?? "all";
                var year = GetCurrentFilingYear();

                PaginationEntity Build(int pageSize) => new PaginationEntity
                {
                    PageIndex = 1,
                    PageSize = pageSize,
                    Search = paginationEntity.Search,
                    SortColumn = paginationEntity.SortColumn,
                    SortOrder = paginationEntity.SortOrder ?? "asc",
                    TypeFilter = paginationEntity.TypeFilter,
                    SearchFields = searchFields,
                    fillingYear = year
                };

                var (_, meta) = await _employerService.GetEmployersAsync(empId, Build(1), searchFields);
                var total = meta?.TotalItems ?? 0;
                if (total <= 0) return Json(new { filingYear = year, ids = new List<string>() });

                var (employers, _) = await _employerService.GetEmployersAsync(empId, Build(total), searchFields);
                var ids = employers
                    .Select(e => e.Id)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();

                return Json(new { filingYear = year, ids });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetFilteredEmployerIds), nameof(EmployerController), "Building whole-list employer id navigation.");
                return Json(new { filingYear = GetCurrentFilingYear(), ids = new List<string>() });
            }
        }


        [AuthorizePermission("Index")]
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> LoadEmployerInfo(string employerId)
        {
            try
            {
                var info = await _employerService.GetEmployerInfoAsync(employerId);
                if (info == null) return PartialView("_EmployerInfoPartial", new EmployerInfoMainDisplayViewModel());
                var companyId = await _employerService.GetPrimaryEmployerIdAsync(employerId);
                var primaryId = companyId.HasValue ? companyId.Value.ToString() : employerId;

                // Affiliates share the parent company's tracker info (the read resolves it
                // via companyId), so editing here would not persist — make it read-only and
                // tell the user. companyId.HasValue == this employer is an affiliate.
                ViewBag.IsAffiliate = companyId.HasValue;

                var dropdowns = await _employerService.GetEmployerDropdownDataAsync(employerId, info.FirmId, info.BrokerId, info.IsBilling);
                var contacts = await _employerService.GetEmployerDropdownDataAsync(primaryId, info.FirmId, info.BrokerId, false);
                var billing = await _employerService.GetEmployerDropdownDataAsync(primaryId, info.FirmId, info.BrokerId, true);

                return PartialView("_EmployerInfoPartial", _builder.BuildInfoViewModel(info, dropdowns, contacts, billing));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(LoadEmployerInfo), nameof(EmployerController), $"employerId:{employerId}");
                return StatusCode(500, "An error occurred while loading employer information.");
            }
        }
        [AuthorizePermission("Index")]
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> EmployerServiceInfo(string employerId, string planYear)
        {
            try
            {
                var serviceList = await _employerService.GetServiceList(employerId, planYear);

                // Same as ER Info: affiliates inherit services from the parent, so edits
                // here won't stick — surface a notice and lock the tab.
                var svcParentId = await _employerService.GetPrimaryEmployerIdAsync(employerId);
                ViewBag.IsAffiliate = svcParentId.HasValue;

                return PartialView("_ServiceInfoPartial", new EmployerServiceInfoViewModel
                {
                    EmployerId = employerId,
                    PlanYear = planYear,
                    ServiceList = serviceList,
                    SelectedService = null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(EmployerServiceInfo), nameof(EmployerController), $"employerId:{employerId}, planYear:{planYear}");
                return StatusCode(500, "An error occurred while loading employer service information.");
            }
        }


        [HttpGet]
        public async Task<IActionResult> Employer1094Tab(string aca_employerId)
        {
            try
            {
                var employer = await _employerService.GetEmployer1094DetailsAsync(aca_employerId);
                return PartialView("_Employer1094TabPartial", _builder.Build1094ViewModel(employer));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Employer1094Tab), nameof(EmployerController), $"aca_employerId:{aca_employerId}");
                return StatusCode(500, "An error occurred while loading 1094 tab.");
            }
        }

        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> EmployerAuditTab(int employerId)
        {
            try
            {
                var auditLog = await _employerService.GetEmployerAuditLogAsync(employerId);
                return PartialView("_EmployerAuditTabPartial", auditLog);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(EmployerAuditTab), nameof(EmployerController), $"employerId:{employerId}");
                return StatusCode(500, "An error occurred while loading audit log.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> EmployerOtherDetailsPartial(string aca_employerId)
        {
            try
            {
                var importantInfo = await _employerService.GetEmployerImportantInfoAsync(aca_employerId);

                var model = new EmployerDetailsViewModel
                {
                    Employer_otherdetails = new Employer()
                    {
                        ImportantInfo = importantInfo ?? new EmployerImportantInfo()
                    },
                    ACA_EmployerId = int.Parse(aca_employerId)
                };

                return PartialView("_EmployerImportantInfoPartial", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(EmployerOtherDetailsPartial), nameof(EmployerController), $"employerId:{aca_employerId}");
                return StatusCode(500, ex.ToString());
            }
        }

        // ===================================================================
        // 3. SEARCH & LOOKUP
        // ===================================================================

        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> SearchEmployersAsync(string term, int pageSize = 10)
        {
            try
            {
                var yearString = GetCurrentFilingYear();
                int year = !string.IsNullOrEmpty(yearString) ? int.Parse(yearString) : 0;
                var results = await _employerService.SearchEmployersAsync(term, pageSize, year);
                return Json(results.Select(e => new { id = e.Id, name = e.Name }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SearchEmployersAsync), nameof(EmployerController), $"term:{term}");
                return StatusCode(500, new { success = false, message = "An error occurred during employer search." });
            }
        }

        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> GetEmployerByIdAsync(string id)
        {
            try
            {
                var employer = await _employerService.GetEmployerByIdAsync(id);
                if (employer == null) return NotFound();
                return Json(new { id = employer.Id, name = employer.Name });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerByIdAsync), nameof(EmployerController), $"id:{id}");
                return StatusCode(500, new { success = false, message = "An error occurred while retrieving the employer." });
            }
        }

        // ===================================================================
        // 4. SERVICE DETAIL
        // ===================================================================

        [AuthorizePermission("Index")]
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> LoadServiceDetail(string employerId, string planYear, string serviceId)
        {
            try
            {
                var auditUsers = await _employerService.GetAuditByDropdownAsync();
                var fteSteps = await _employerService.GetProcessesAsync("FTE Process Step");
                var steps1095 = await _employerService.GetWebProcessesAsync("1095");
                var stateFilingSteps = await _employerService.GetProcessesAsync("State Filing Process Step");
                var serviceDetail = await _employerService.GetServiceDetail(employerId, planYear, serviceId);

                if (serviceDetail != null)
                {
                    _builder.EnrichServiceDetail(serviceDetail, auditUsers, fteSteps, steps1095, stateFilingSteps);

                    // 1095
                    if (!string.IsNullOrEmpty(serviceDetail.ImplementationProcess) &&
                        !serviceDetail._implementation.Any(p => p.Name == serviceDetail.ImplementationProcess))
                    {
                        serviceDetail._implementation.Insert(0, new ImplementationProcess
                        {
                            Id = "0",
                            Name = serviceDetail.ImplementationProcess
                        });
                    }
                }

                return PartialView("_ServiceDetailPartial", new EmployerServiceInfoViewModel
                {
                    EmployerId = employerId,
                    PlanYear = planYear,
                    SelectedService = serviceDetail
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(LoadServiceDetail), nameof(EmployerController), $"employerId:{employerId}, planYear:{planYear}, serviceId:{serviceId}");
                return StatusCode(500, "An error occurred while loading service details.");
            }
        }

        // ===================================================================
        // 5. ADD / DELETE OPERATIONS
        // ===================================================================

        [AuthorizePermission("Index")]
        [HttpGet]
        public async Task<IActionResult> AddEmployerCommon()
        {
            try
            {
                var filingYear = GetCurrentFilingYear();
                var dropdowns = await _employerService.GetEmployerDropdownDataAsync("0", null, null, true);
                var services = await _employerService.GetServiceDropdownList();
                var auditUsers = await _employerService.GetAuditByDropdownAsync();
                var fteSteps = await _employerService.GetProcessesAsync("FTE Process Step");
                var steps1095 = await _employerService.GetWebProcessesAsync("1095");
                var stateFilingSteps = await _employerService.GetProcessesAsync("State Filing Process Step");

                return PartialView("_AddEmployerModal",
                    _builder.BuildAddEmployerViewModel(filingYear, dropdowns, services, auditUsers, fteSteps, steps1095, stateFilingSteps));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddEmployerCommon), nameof(EmployerController), "Error loading Add Employer modal");
                return StatusCode(500, "An error occurred while loading the Add Employer form.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> AddEmployerCommon([FromForm] AddEmployerViewModel model)
        {
            try
            {
                var companyId = HttpContext.Session.GetString("CompanyId");
                var (employerId, trackerEmployerId) = await _employerService.AddEmployerCommonAsync(model.Employer, companyId);
                return Json(new { success = true, employerId, trackerEmployerId, message = "Employer added successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddEmployerCommon), nameof(EmployerController), "Error adding employer");
                return Json(new { success = false, message = "Error occurred while adding employer." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> DeleteEmployer(int id)
        {
            try
            {
                await _employerService.DeleteEmployerAsync(id);
                return Json(new { success = true, message = "Employer deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(DeleteEmployer), nameof(EmployerController), $"id:{id}");
                return Json(new { success = false, message = "Error occurred while deleting." });
            }
        }

        // ===================================================================
        // 6. DATA QUALITY
        // ===================================================================

        [HttpGet]
        public async Task<IActionResult> RunAnomalyCheck(int id, int year)
        {
            try
            {
                var scorecard = await _anomalyDetectionService.AnalyzeEmployerDataAsync(id, year);
                return PartialView("_DataQualityScorecard", scorecard);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(RunAnomalyCheck), nameof(EmployerController), $"id:{id}, year:{year}");
                return StatusCode(500, "An error occurred while running the anomaly check.");
            }
        }

        // ===================================================================
        // 7. UPDATE OPERATIONS
        // ===================================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> UpdateEmployerCommon([FromForm] EmployerDetailsViewModel model)
        {
            try
            {
                bool isUpdated = await _employerService.UpdateEmployerCommonAsync(model.ToDomainModel());
                return Json(isUpdated
                    ? new { success = true, message = "Employer information updated successfully." }
                    : new { success = false, message = "No changes were saved. Please check if the record exists." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(UpdateEmployerCommon), nameof(EmployerController), $"ACA_EmployerId:{model?.ACA_EmployerId}");
                return Json(new { success = false, message = ex.InnerException?.Message ?? ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> SaveEmployer1094Tab([FromForm] AddEmployerViewModel model)
        {
            if (model == null || model._1094View == null || string.IsNullOrEmpty(model._1094View.EmployerId))
                return BadRequest("Employer ID missing or invalid.");
            try
            {
                await _employerService.SaveEmployer1094DetailsAsync(model.To1094DomainModel());
                return Json(new { success = true, message = "Form 1094 details saved successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SaveEmployer1094Tab), nameof(EmployerController), $"EmployerId:{model._1094View.EmployerId}");
                return Json(new { success = false, message = "Error saving data: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> SaveEmployerImportantInfo([FromForm] EmployerImportantInfo model)
        {
            try
            {
                bool saved = await _employerService.SaveEmployerImportantInfoAsync(model);
                return Json(saved
                    ? new { success = true,  message = "Other details saved successfully." }
                    : new { success = false, message = "No changes were saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SaveEmployerImportantInfo), nameof(EmployerController), $"EmployerId:{model?.EmployerId}");
                return Json(new { success = false, message = "Error saving data." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> SaveEmployerTrackerTab([FromForm(Name = "employer_trackerinfo")] EmployerInfoModel model)
        {
            try
            {
                int.TryParse(GetCurrentUserId(), out int currentUserId);
                int result = await _employerService.AddEmployerInfo_TrackerAsync(model, currentUserId);
                return Json(result > 0
                    ? new { success = true,  message = "Tracker info saved successfully." }
                    : new { success = false, message = "No changes were saved." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SaveEmployerTrackerTab), nameof(EmployerController), $"EmployerId:{(model != null ? model.EmployerId : "")}");
                return Json(new { success = false, message = "Error saving tracker data." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> UpdateEmployerService([FromBody] ServiceDetail model)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
                var updateResult = await _employerService.UpdateEmployerServiceAsync(model, userId);
                return Json(updateResult.Success
                    ? new { success = true,  message = updateResult.Message }
                    : new { success = false, message = updateResult.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(UpdateEmployerService), nameof(EmployerController), $"ServiceId:{model?.EmployerServiceId}");
                return StatusCode(500, new { success = false, message = "An unexpected system error occurred while saving." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> AddEmployerService([FromForm(Name = "SelectedService")] ServiceDetail model)
        {
            if (!ModelState.IsValid)
                return Json(new { success = false, message = "Invalid data submitted." });
            try
            {
                bool isUpdate = int.TryParse(model.ServiceId, out int parsedId) && parsedId > 0;
                int serviceId = isUpdate ? parsedId : await _employerService.AddEmployerServiceAsync(model);

                return serviceId > 0 || isUpdate
                    ? Json(new { success = true, isUpdate, id = serviceId, status = model.Status, message = isUpdate ? "Service updated successfully!" : "Service added successfully!" })
                    : Json(new { success = false, message = "Database save failed." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddEmployerService), nameof(EmployerController), $"EmployerId:{model?.EmployerId}");
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> AddAffiliate(AffiliateViewModel webModel)
        {
            try
            {
                webModel.FilingYear = Convert.ToInt32(GetCurrentFilingYear());
                int newId = await _employerService.AddAffiliateAsync(webModel.ToDto());
                return newId > 0
                    ? Json(new
                    {
                        success = true,
                        affiliateId = newId,
                        name = webModel.AffiliateName,
                        message = "Affiliate added successfully."
                    })
                    : Json(new { success = false, message = "Database save failed." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddAffiliate), nameof(EmployerController),
                                 $"EmployerId:{webModel?.EmployerId}");
                return StatusCode(500, new { success = false, message = "An error occurred." });
            }
        }
        // EDIT
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> EditAffiliate(AffiliateViewModel webModel)
        {
            try
            {
                await _employerService.SaveAffiliateAsync(webModel.ToDto());
                return Json(new
                {
                    success = true,
                    name = webModel.AffiliateName,
                    message = "Affiliate updated successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(EditAffiliate), nameof(EmployerController),
                                 $"AffiliateId:{webModel?.AffiliateId}");
                return StatusCode(500, new { success = false, message = "An error occurred." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAffiliate(int id)
        {
            try
            {
                var item = await _employerService.GetAffiliateByIdAsync(id);
                if (item is null)
                    return Json(new { success = false, message = "Affiliate not found." });

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        affiliateId = item.AffiliateId,       // ← now correct
                        parentEmployerId = item.ParentEmployerId,
                        affiliateName = item.AffiliateName,
                        ein = item.EIN,
                        phone = item.Phone,
                        email = item.Email,
                        address = item.Address,
                        city = item.City,
                        state = item.State,
                        zip = item.Zip,
                        numberOfEmployees = item.NumberOfEmployees
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAffiliate), nameof(EmployerController), $"Id:{id}");
                return StatusCode(500, new { success = false, message = "An error occurred." });
            }
        }

        // DELETE
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> DeleteAffiliate(int id)
        {
            try
            {
                bool deleted = await _employerService.DeleteAffiliateAsync(id);
                return deleted
                    ? Json(new { success = true, message = "Affiliate removed successfully." })
                    : Json(new { success = false, message = "Cannot delete the primary employer." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(DeleteAffiliate), nameof(EmployerController), $"Id:{id}");
                return StatusCode(500, new { success = false, message = "An error occurred." });
            }
        }



        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetPrimaryData(int employerId)
        {
            try
            {
                var item = await _employerService.GetPrimaryEmployerDataAsync(employerId);
                if (item is null)
                    return Json(new { success = false, message = "Primary employer not found." });

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        affiliateName = item.AffiliateName,
                        ein = item.EIN,
                        address = item.Address,
                        city = item.City,
                        state = item.State,
                        zip = item.Zip,
                        phone = item.Phone,
                        email = item.Email,
                        numberOfEmployees = item.NumberOfEmployees
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetPrimaryData), nameof(EmployerController),
                                 $"EmployerId:{employerId}");
                return StatusCode(500, new { success = false, message = "An error occurred." });
            }
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> AddContact(ContactViewModel webModel)
        {
            try
            {
                int contactId = await _employerService.AddContactAsync(webModel.ToDto());
                return contactId > 0
                    ? Json(new { success = true, contactId, name = webModel.Name, message = "Contact added successfully." })
                    : Json(new { success = false, message = "Database save failed." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddContact), nameof(EmployerController), $"EmployerId:{webModel?.EmployerId}");
                return StatusCode(500, new { success = false, message = "An error occurred while adding the contact." });
            }
        }

        // ===================================================================
        // 8. DROPDOWN HELPERS
        // ===================================================================

        [HttpGet]
        public async Task<IActionResult> CalculateFollowUpDate(string processName, string baseDateStr, int categoryId)
        {
            if (string.IsNullOrWhiteSpace(processName)) return Json(new { success = false });
            try
            {
                int days = await _employerService.GetFollowUpDaysAsync(processName, categoryId);
                DateTime baseDate = DateTime.TryParse(baseDateStr, out var parsed) ? parsed : DateTime.Today;
                return Json(new { success = true, nextDate = baseDate.AddBusinessDays(days).ToString("yyyy-MM-dd") });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(CalculateFollowUpDate), nameof(EmployerController), $"processName:{processName}, categoryId:{categoryId}");
                return StatusCode(500, new { success = false, message = "An error occurred while calculating the follow-up date." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetProcessHistory(int employerServiceId, string categoryName)
        {
            if (employerServiceId <= 0 || string.IsNullOrWhiteSpace(categoryName))
                return Json(new System.Collections.Generic.List<ProcessHistoryDto>());
            try
            {
                return Json(await _employerService.GetProcessHistoryAsync(employerServiceId, categoryName));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetProcessHistory), nameof(EmployerController), $"serviceId:{employerServiceId}");
                return StatusCode(500, "Failed to load history data.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetBrokersByFirm(int firmId)
        {
            try
            {
                var data = await _employerService.GetEmployerDropdownDataAsync("0", firmId, null, false);
                return Json(data.Brokers.Select(x => new { value = x.Key, text = x.Value }).OrderBy(x => x.text));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetBrokersByFirm), nameof(EmployerController), $"firmId:{firmId}");
                return StatusCode(500, new { success = false, message = "An error occurred while fetching brokers." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetBrokerContactDetails(int brokerId)
        {
            try
            {
                var data = await _employerService.GetEmployerDropdownDataAsync("0", null, brokerId, false);
                return data.BrokerEntityDetails != null
                    ? Json(new { found = true, phone = data.BrokerEntityDetails.Phone, email = data.BrokerEntityDetails.Email })
                    : Json(new { found = false });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetBrokerContactDetails), nameof(EmployerController), $"brokerId:{brokerId}");
                return StatusCode(500, new { success = false, message = "An error occurred while fetching broker contact details." });
            }
        }

        // ===================================================================
        // 9. PORTFOLIO & AGGREGATE DATA
        // ===================================================================

        [HttpGet]
        [AuthorizePermission("Index")]
        [RequireEmployerScope]
        public async Task<IActionResult> GetAggregatedMembers(int employerId)
        {
            try
            {
                return Json(await _employerService.GetAggregatedMembersAsync(employerId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAggregatedMembers), nameof(EmployerController), $"employerId:{employerId}");
                return StatusCode(500, new { success = false, message = "Error loading aggregated members." });
            }
        }

        [HttpGet]
        [AuthorizePermission("Index")]
        [RequireEmployerScope]
        public async Task<IActionResult> GetEmployer1094BDetails(string employerId)
        {
            try
            {
                return Json(await _employerService.GetEmployer1094BDetailsAsync(employerId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployer1094BDetails), nameof(EmployerController), $"employerId:{employerId}");
                return StatusCode(500, new { success = false, message = "Error loading 1094B details." });
            }
        }

        [HttpGet]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> GetAmClientPortfolio(string userId)
        {
            try
            {
                return Json(await _employerService.GetAmClientPortfolioAsync(userId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAmClientPortfolio), nameof(EmployerController), $"userId:{userId}");
                return StatusCode(500, new { success = false, message = "Error loading client portfolio." });
            }
        }

        [HttpGet]
        [AuthorizePermission("Index")]
        public async Task<IActionResult> GetBrokerClientPortfolio(string userId)
        {
            try
            {
                return Json(await _employerService.GetBrokerClientPortfolioAsync(userId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetBrokerClientPortfolio), nameof(EmployerController), $"userId:{userId}");
                return StatusCode(500, new { success = false, message = "Error loading client portfolio." });
            }
        }
    }
}
