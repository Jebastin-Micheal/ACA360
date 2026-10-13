using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Extensions;
using ACA360.Core.Models;
using ACA360.Core.ViewModels;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.InternalTeam)]
    public class FlagsController : BaseController
    {
        private readonly IFlagRepository _flagRepo;
        private readonly ILoggerService _logger;
        private readonly IExportService _exportService;
        private readonly IEmployeeService _employeeService;

        public FlagsController(IFlagRepository flagRepo, ILoggerService logger, IExportService exportService, IEmployeeService employeeService)
        {
            _flagRepo = flagRepo;
            _logger = logger;
            _exportService = exportService;
            _employeeService = employeeService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var flags = await _flagRepo.GetAllFlagsAsync();
                return View(flags);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Index), "FlagsController", "An error occurred while retrieving the list of flags.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the flags.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetFlagList(string search, string sortCol = "FlagCode", string sortOrder = "ASC", int page = 1, int pageSize = 10)
        {
            try
            {
                var data = await _flagRepo.GetFlagListAsync(search, sortCol, sortOrder, page, pageSize);
                int totalPages = pageSize > 0 ? (int)Math.Ceiling((double)data.TotalCount / pageSize) : 0;

                var model = new FlagListViewModel
                {
                    Flags = data.List,
                    SortColumn = sortCol,
                    SortOrder = sortOrder,
                    Metadata = new PaginationViewEntity
                    {
                        TotalItems = data.TotalCount,
                        CurrentPage = page,
                        PageSize = pageSize,
                        TotalPages = totalPages,
                        TotalCount = data.TotalCount,
                        PageNumber = page
                    }
                };

                return PartialView("_FlagsListPartial", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetFlagList), "FlagsController", $"An error occurred while retrieving the flag list. Search: '{search}', Page: {page}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while fetching the flag list.");
            }
        }

        /// <summary>
        /// Generic export endpoint - mirrors the same pattern any other controller (Employees, Users, etc.)
        /// should follow: fetch the FULL filtered dataset (no paging), map it to the generic
        /// ExportRequest shape, hand it to IExportService, return the right content type.
        ///
        /// format: csv | excel | pdf | print
        /// search/sortCol/sortOrder: same filters used by the grid, so export matches what the user is looking at.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Export(string format, string search, string sortCol = "FlagCode", string sortOrder = "ASC", string title = "System Flags")
        {
            try
            {
                var data = await _flagRepo.GetFlagListAsync(search, sortCol, sortOrder, 1, int.MaxValue);

                return HandleExport(
            _exportService,
            format,
            title: title,
            fileName: "SystemFlags",
            columns: new List<ExportColumn>
            {
                new ExportColumn("FlagCode", "Code"),
                new ExportColumn("FlagName", "Name"),
                new ExportColumn("LogicType", "Type"),
                new ExportColumn("Category", "Category"),
                new ExportColumn("Severity", "Severity"),
                new ExportColumn("Status", "Status")
            },
            rows: data.List.Select(f => new Dictionary<string, object?>
            {
                ["FlagCode"] = f.FlagCode,
                ["FlagName"] = f.FlagName,
                ["LogicType"] = f.LogicType,
                ["Category"] = f.Category,
                ["Severity"] = f.Severity,
                ["Status"] = f.IsActive ? "Active" : "Inactive"
            }).ToList()
        );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Export), "FlagsController", $"An error occurred while exporting flags. Format: '{format}'.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while exporting the flags.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Manage(int? id)
        {
            try
            {
                var model = new FlagDefinitionViewModel();

                if (id.HasValue && id.Value > 0)
                {
                    var entity = await _flagRepo.GetFlagByIdAsync(id.Value);
                    if (entity == null) return NotFound();

                    model.FlagId = entity.FlagId;
                    model.FlagCode = entity.FlagCode ?? string.Empty;
                    model.FlagName = entity.FlagName ?? string.Empty;
                    model.Description = entity.Description ?? string.Empty;
                    model.Category = entity.Category ?? string.Empty;
                    model.Severity = entity.Severity ?? string.Empty;
                    model.LogicType = entity.LogicType ?? string.Empty;
                    model.TargetTable = entity.TargetTable ?? string.Empty;
                    model.TargetColumn = entity.TargetColumn ?? string.Empty;
                    model.Operator = entity.Operator ?? string.Empty;
                    model.ComparisonValue = entity.ComparisonValue ?? string.Empty;
                    model.Connector = entity.Connector ?? string.Empty;
                    model.SecondaryColumn = entity.SecondaryColumn ?? string.Empty;
                    model.SecondaryOperator = entity.SecondaryOperator ?? string.Empty;
                    model.SecondaryValue = entity.SecondaryValue ?? string.Empty;
                    model.CustomSqlPredicate = entity.CustomSqlPredicate ?? string.Empty;
                    model.RemediationSuggestion = entity.RemediationSuggestion ?? string.Empty;
                    model.IsSystemFlag = entity.IsSystemFlag;
                    model.IsActive = entity.IsActive;
                    // TODO: [FLAG-FIX DB] load the Fix-button routing into the edit form
                    model.TargetTab = entity.TargetTab ?? string.Empty;
                    model.TargetField = entity.TargetField ?? string.Empty;
                }

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Manage), "FlagsController", $"An error occurred while loading the flag management view for Flag ID: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the flag details.");
            }
        }

        [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(FlagDefinitionViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { success = false, message = "Invalid data submitted. Please check your inputs." });
                }

                var entity = new FlagDefinition
                {
                    FlagId = model.FlagId,
                    FlagCode = model.FlagCode,
                    FlagName = model.FlagName,
                    Category = model.Category,
                    Severity = model.Severity,
                    LogicType = model.LogicType,
                    TargetTable = model.TargetTable,
                    TargetColumn = model.TargetColumn,
                    Operator = model.Operator,
                    ComparisonValue = model.ComparisonValue,
                    Connector = model.Connector,
                    SecondaryColumn = model.SecondaryColumn,
                    SecondaryOperator = model.SecondaryOperator,
                    SecondaryValue = model.SecondaryValue,
                    CustomSqlPredicate = model.CustomSqlPredicate,
                    RemediationSuggestion = model.RemediationSuggestion,
                    // TODO: [FLAG-FIX DB] persist the Fix-button routing
                    TargetTab = string.IsNullOrWhiteSpace(model.TargetTab) ? null : model.TargetTab.Trim(),
                    TargetField = string.IsNullOrWhiteSpace(model.TargetField) ? null : model.TargetField.Trim(),
                    IsActive = model.IsActive
                };

                await _flagRepo.SaveFlagAsync(entity);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Save), "FlagsController", $"An error occurred while saving the flag configuration (FlagCode: {model?.FlagCode}).");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while saving the flag." });
            }
        }

        /// <summary>
        /// Returns active flag definitions as {value, text} pairs for dropdown binding (e.g. the Employee list Flag filter).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Options()
        {
            try
            {
                var flags = await _flagRepo.GetAllFlagsAsync();
                return Json(flags
                    .Where(f => f.IsActive && f.FlagId > 0)
                    .Select(f => new { value = f.FlagId, text = f.FlagName, severity = f.Severity }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Options), "FlagsController", "An error occurred while retrieving flag options.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the flag options.");
            }
        }
        // Flag Fix Grid

        //[HttpGet]
        //public async Task<IActionResult> FlaggedGrid(string? employerIds, int filingYear)
        //{
        //    try
        //    {
        //        // We no longer split the string. Pass the raw "1,5,4,6,2,3" directly.
        //        var gridData = await _flagRepo.GetFlaggedEmployeeGridAsync(employerIds, filingYear);

        //        ViewBag.FilingYear = filingYear;
        //        // Safely handle nulls if the DB returns nothing
        //        ViewBag.FlagDefs = gridData.FlagDefs ?? new List<FlagDefinition>();
        //        var model = gridData.Employees?.ToList() ?? new List<FlaggedEmployeeGrid>();

        //        return PartialView("_FlagFixGrid", model);
        //    }
        //    catch (Exception ex)
        //    {
        //        // This will send the exact crash reason back to your browser console
        //        return StatusCode(500, $"Server Error: {ex.Message}");
        //    }
        //}

        private static JsonResult FlagGridJson(object value) => new JsonResult(value,
     new JsonSerializerOptions { PropertyNamingPolicy = null, DictionaryKeyPolicy = null });

        /// <summary>
        /// The employer list for the flag-grid procedures, narrowed to what this request may
        /// reach. The procedures read a blank list as "every employer", so only roles that see
        /// every employer (and are not viewing as someone) keep that; everyone else gets their
        /// own employers. Null result = refuse (asked only for employers out of scope, or none
        /// permitted at all).
        /// </summary>
        private async Task<(bool allowed, string? employerIds)> ScopeGridEmployersAsync(string? requested)
        {
            var asked = (requested ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var id) ? id : 0L)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (asked.Count == 0)
            {
                if (IsUnrestrictedEmployerRole()) return (true, null);   // all employers
                var own = await GetPermittedEmployerIdsAsync();
                return own.Count == 0 ? (false, null) : (true, string.Join(",", own));
            }

            var permitted = await FilterPermittedEmployerIdsAsync(asked);
            return permitted.Count == 0 ? (false, null) : (true, string.Join(",", permitted));
        }

        private IActionResult NoEmployerAccess() =>
            StatusCode(StatusCodes.Status403Forbidden, new { error = "You do not have access to these employers." });

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult FlaggedGrid(string? employerIds, int filingYear)
        {
            if (filingYear < 1000 || filingYear > 9999)
                return BadRequest(new { message = "Select a valid filing year before opening the grid." });
            ViewBag.FilingYear = filingYear;
            ViewBag.EmployerIds = employerIds;
            // Shell only: no employee queries, flag recalculation, or ViewBag.FlagDefs enumeration.
            return PartialView("_FlagFixGrid", new List<FlaggedEmployeeGrid>());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetFlaggedGridData(
            string? employerIds, int filingYear, CancellationToken cancellationToken)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            int draw = int.TryParse(form["draw"], out var parsedDraw) ? Math.Max(0, parsedDraw) : 0;
            if (!int.TryParse(form["start"], out int skip) || skip < 0 ||
                !int.TryParse(form["length"], out int take) || !new[] { 10, 25, 50, 100 }.Contains(take) ||
                filingYear < 1000 || filingYear > 9999)
                return BadRequest(new { error = "Invalid page or filing year." });
            string? search = form["search[value]"].FirstOrDefault()?.Trim();
            string? flag = form["flagFilter"].FirstOrDefault()?.Trim();
            string? severity = form["severityFilter"].FirstOrDefault()?.Trim().ToLowerInvariant();
            var tab = form["tab"].FirstOrDefault()?.Trim().ToLowerInvariant() ?? "employee";
            if (!new[] { "employee", "hire", "status", "payroll", "enrollment", "dependent" }.Contains(tab))
                return BadRequest(new { error = "Invalid detail tab." });
            if (string.IsNullOrEmpty(search)) search = null;
            if (string.IsNullOrEmpty(flag) || flag == "0.0") flag = null;
            if (string.IsNullOrEmpty(severity)) severity = null;
            if ((search?.Length ?? 0) > 100 || (flag?.Length ?? 0) > 128 ||
                (severity != null && !new[] { "critical", "warning", "info", "editable", "edited" }.Contains(severity)))
                return BadRequest(new { error = "Invalid grid filter." });
            bool includeDefs = !bool.TryParse(form["includeFlagDefs"], out bool include) || include;
            string? editedCodes = form["editedEmployeeCodeIds"].FirstOrDefault();
            string? editedEmployees = form["editedEmployeeIds"].FirstOrDefault();
            var (allowed, scopedEmployers) = await ScopeGridEmployersAsync(employerIds);
            if (!allowed) return NoEmployerAccess();
            try
            {
                var result = await _flagRepo.GetFlaggedEmployeeGridAsync(scopedEmployers, filingYear,
                    skip, take, search, flag, severity, includeDefs,
                    string.IsNullOrEmpty(editedCodes) ? null : editedCodes,
                    string.IsNullOrEmpty(editedEmployees) ? null : editedEmployees, cancellationToken, tab);
                // Preserve lower-case envelope and PascalCase DTO properties for this endpoint only.
                return FlagGridJson(new
                {
                    draw,
                    tab,
                    recordsTotal = result.TotalRecords,
                    recordsFiltered = result.FilteredRecords,
                    data = result.Employees,
                    flagDefs = includeDefs ? result.FlagDefs : null
                });
            }
            catch (SqlException ex) when (ex.Number == 50001)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // Use the application's existing ILogger if available. No SQL details in the response.
                System.Diagnostics.Trace.TraceError("Flag grid GET failed: {0}", ex);
                return StatusCode(500, new { error = "Unable to load flagged employees. Check the application log." });
            }
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetFlaggedGridFlagCounts(
            string? employerIds, int filingYear, CancellationToken cancellationToken)
        {
            if (filingYear < 1000 || filingYear > 9999)
                return BadRequest(new { error = "Invalid filing year." });
            var (allowed, scopedEmployers) = await ScopeGridEmployersAsync(employerIds);
            if (!allowed) return NoEmployerAccess();
            try
            {
                var flagDefs = await _flagRepo.GetFlaggedGridFlagCountsAsync(
                    scopedEmployers, filingYear, cancellationToken);
                return FlagGridJson(new { flagDefs });
            }
            catch (SqlException ex) when (ex.Number == 50001)
            { return BadRequest(new { error = ex.Message }); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Flag grid counts failed: {0}", ex);
                return StatusCode(500, new { error = "Unable to load flag counts. Retry the count request." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveFlaggedGrid(
            [FromBody] SaveGridRequest? request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid || request?.Edits == null || request.Edits.Count == 0 ||
                request.Edits.Count > 1000 || request.FilingYear < 1000 || request.FilingYear > 9999 ||
                request.Edits.Any(e => e == null || e.EmployeeId <= 0 || e.EmployeeCodeId <= 0 || e.Changes == null || e.Changes.Count == 0))
                return BadRequest(new { success = false, message = "Supply a valid year, employee/child IDs and changed fields." });
            // Fixing flags edits employee records (SSN, name, address, e-mail, birthday, ...):
            // the same permission as editing an employee, and only employees this user may reach.
            if (!User.HasPermission("Employee", "Edit"))
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "You do not have permission to edit employees." });
            if (!await AreEmployeesPermittedAsync(request.Edits.Select(e => e.EmployeeId)))
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "You do not have access to one or more of these employees." });
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Save sparse edits once and audit them through the existing service.
                // Flag reevaluation is reported separately by the service.
                var result = await _employeeService.BulkFixFlaggedEmployeesAsync(
                    request.Edits, request.FilingYear, cancellationToken);
                return FlagGridJson(new
                {
                    success = result.Failed == 0,
                    saved = result.Saved,
                    failed = result.Failed,
                    savedCells = result.SavedCells,
                    auditFailures = result.AuditFailures,
                    flagRefreshRequired = result.FlagRefreshRequired,
                    warnings = result.Warnings
                });
            }
            catch (ArgumentException ex) { return BadRequest(new { success = false, message = ex.Message }); }
            catch (SqlException ex) when (ex.Number == 50001)
            { return BadRequest(new { success = false, message = ex.Message }); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Flag grid SAVE failed: {0}", ex);
                // A service can fail AFTER the repository commits. Never falsely promise rollback here.
                return StatusCode(500, new
                {
                    success = false,
                    message = "Save could not be confirmed. Your edits are retained. Check the application log and current data before retrying."
                });
            }
            finally
            {
                System.Diagnostics.Trace.TraceInformation("Flag grid total save/service time: {0} ms.", timer.ElapsedMilliseconds);
            }
        }

    }
}
