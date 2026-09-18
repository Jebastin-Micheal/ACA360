using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.Core.Attributes;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Controllers;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Handles all menu management operations including listing, pagination,
/// add/edit form rendering, save, and soft-delete.
/// Restricted to authorised roles only via the class-level Authorize attribute.
/// </summary>
[Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," +
                   UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," +
                   UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," +
                   UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
public class MenuController : BaseController
{
    private readonly IMenuService _menuService;
    private readonly ILoggerService _logger;
    private readonly IExportService _exportService;

    /// <summary>
    /// Initialises the MenuController with the menu service and logger
    /// injected via the DI container.
    /// </summary>
    public MenuController(IMenuService menuService, ILoggerService logger, IExportService exportService)
    {
        _menuService = menuService;
        _logger = logger;
        _exportService = exportService;
    }

    /// <summary>
    /// GET: Renders the main menu management page with server-side pagination,
    /// search, and sort support.
    /// Defaults to page 1 with an ascending sort if no query parameters are provided.
    /// </summary>
    [AuthorizePermission("Index")]
    public async Task<IActionResult> Index(int pageIndex = 1, string search = "", int sortColumn = 0, string sortOrder = "asc")
    {
        try
        {
            // Build the pagination request from the incoming query parameters
            var paginationEntity = new PaginationEntity
            {
                PageIndex = pageIndex,
                PageSize = 10,
                Search = search,
                SortColumn = sortColumn,
                SortOrder = sortOrder
            };

            // Fetch the paged menu list and its accompanying metadata (total count, page count, etc.)
            var (menus, metadata) = await _menuService.GetMenuList(paginationEntity);

            // Compose the view model, carrying pagination state back to the view for re-use in paging controls
            var viewModel = new MenuViewModel
            {
                Menus = menus,
                Metadata = metadata,
                Search = search,
                SortColumn = sortColumn,
                SortOrder = sortOrder
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            // Log with enough context to reproduce the failing query without manual investigation
            _logger.LogError(ex, nameof(Index), nameof(MenuController),
                $"Error loading menu list for pageIndex: {pageIndex}, search: {search}",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return View("Error");
        }
    }

    /// <summary>
    /// GET: Returns the paginated menu list as a partial view for AJAX-driven table refreshes.
    /// Accepts dynamic page size to support per-user page-size preferences in the UI.
    /// </summary>
    [AuthorizePermission("Index")]
    public async Task<IActionResult> MenuListPartial(int pageIndex, string search, int sortColumn, string sortOrder, int PageSize)
    {
        try
        {
            // Build the pagination request; page size is caller-supplied to support dynamic table controls
            var paginationEntity = new PaginationEntity
            {
                PageIndex = pageIndex,
                PageSize = PageSize,
                Search = search,
                SortColumn = sortColumn,
                SortOrder = sortOrder
            };

            // Fetch the paged result set for the partial table re-render
            var (menus, metadata) = await _menuService.GetMenuList(paginationEntity);

            var viewModel = new MenuViewModel
            {
                Menus = menus,
                Metadata = metadata,
                Search = search,
                SortColumn = sortColumn,
                SortOrder = sortOrder
            };

            return PartialView("_MenuListPartial", viewModel);
        }
        catch (Exception ex)
        {
            // Log with page/search context so the failing AJAX request can be traced in logs
            _logger.LogError(ex, nameof(MenuListPartial), nameof(MenuController),
                $"Error loading menu list partial for pageIndex: {pageIndex}, search: {search}",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return View("Error");
        }
    }

    /// <summary>
    /// GET: Loads the Add/Edit modal form for a menu item.
    /// When an ID is supplied the form is pre-populated with the existing menu data;
    /// when omitted a blank form is returned for creating a new menu item.
    /// Returns 400 if the form data cannot be assembled.
    /// </summary>
    public async Task<IActionResult> GetMenuFormAsync(int? id)
    {
        try
        {
            // Step 1: Pre-load dropdown data required by the form regardless of add vs edit mode
            var parentMenus = await _menuService.GetAll_Parent_Menu();
            var menuActions = await _menuService.GetAll_Menu_Action();

            // Step 2: Only fetch the existing menu data when editing (id is present)
            MenuFormDataDto dto = null;
            if (id.HasValue)
            {
                dto = await _menuService.GetMenuFormData(id.Value);
            }

            // Step 3: Compose the form view model with null-safe defaults for the add scenario
            var model = new MenuFormViewModel
            {
                Menu = dto?.Menu ?? new Menu(),
                ParentMenus = parentMenus,
                AvailableActions = menuActions,
                SelectedActionIds = dto?.SelectedActionIds ?? new List<int>()
            };

            return PartialView("_MenuFormModal", model);
        }
        catch (Exception ex)
        {
            // Log the id to pinpoint whether this is an add or edit failure
            _logger.LogError(ex, nameof(GetMenuFormAsync), nameof(MenuController),
                $"Error fetching menu form for id: {id}",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return BadRequest("Invalid Menu Request.");
        }
    }

    /// <summary>
    /// POST: Returns the Add menu modal form pre-populated with empty defaults.
    /// Separated from GetMenuFormAsync to provide a dedicated, permission-controlled
    /// entry point for the "Add New" button in the UI.
    /// Returns 400 if the form scaffolding cannot be assembled.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add_New()
    {
        try
        {
            // Load all dropdown data needed to render a blank add form
            var parentMenus = await _menuService.GetAll_Parent_Menu();
            var menuActions = await _menuService.GetAll_Menu_Action();

            // dto is intentionally null here — this is always a new (empty) menu form
            MenuFormDataDto dto = null;

            var model = new MenuFormViewModel
            {
                Menu = dto?.Menu ?? new Menu(),
                ParentMenus = parentMenus,
                AvailableActions = menuActions,
                SelectedActionIds = dto?.SelectedActionIds ?? new List<int>()
            };

            return PartialView("_MenuFormModal", model);
        }
        catch (Exception ex)
        {
            // Log the failure; a broken add form prevents all new menu creation
            _logger.LogError(ex, nameof(Add_New), nameof(MenuController),
                "Error building the Add New menu form.",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return BadRequest("Invalid Menu Request.");
        }
    }

    /// <summary>
    /// POST: Persists a new or updated menu item along with its associated role and action assignments.
    /// Validates the incoming model before delegating to the service layer.
    /// Returns JSON so the result can be handled by the calling AJAX function without a page reload.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddOrUpdateMenu(MenuFormViewModel model, int roleId)
    {
        try
        {
            // Guard: reject the request early if any required model parts are absent
            if (model == null || model.Menu == null || model.SelectedActionIds == null)
            {
                return BadRequest(new { success = false, message = "Invalid input data." });
            }

            // Delegate the upsert operation — the service handles both insert and update paths
            _menuService.AddOrUpdateMenu(model.Menu, roleId, model.SelectedActionIds);

            return Json(new { success = true, message = "Menu saved successfully." });
        }
        catch (Exception ex)
        {
            // Log with the menu name where available to assist debugging the failed save
            _logger.LogError(ex, nameof(AddOrUpdateMenu), nameof(MenuController),
                $"Error saving menu: {model?.Menu?.MenuName ?? "unknown"}",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(500, new { success = false, message = $"Internal server error: {ex.Message}" });
        }
    }

    /// <summary>
    /// POST: Soft-deletes a menu item by ID, preserving the record in the database
    /// while hiding it from active use.
    /// Returns JSON so the calling AJAX function can update the UI without a page reload.
    /// </summary>
    [AuthorizePermission("Index")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(int id)
    {
        try
        {
            // Perform a soft delete — the record is flagged as deleted, not physically removed
            _menuService.SoftDeleteMenu(id);

            return Json(new { success = true, message = "Menu deleted successfully." });
        }
        catch (Exception ex)
        {
            // Log with the menu ID so the failed soft-delete can be audited
            _logger.LogError(ex, nameof(Delete), nameof(MenuController),
                $"Error soft-deleting menu with id: {id}",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(500, new { success = false, message = $"Error deleting menu: {ex.Message}" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Export(string format = "excel", string search = "", string title = "Menu List")
    {
        try
        {
            var paginationEntity = new PaginationEntity
            {
                PageIndex = 1,
                PageSize = int.MaxValue,
                Search = search,
                SortColumn = 0,
                SortOrder = "asc"
            };

            var (menus, _) = await _menuService.GetMenuList(paginationEntity);

            return HandleExport(
                _exportService,
                format,
                title: title,
                fileName: "MenuList",
                columns: new List<ExportColumn>
                {
                    new ExportColumn("MenuName",     "Menu Name"),
                    new ExportColumn("Controller",   "Controller"),
                    new ExportColumn("DisplayOrder", "Display Order"),
                    new ExportColumn("IsActive",     "Active")
                },
                rows: menus.Select(m => new Dictionary<string, object>
                {
                    ["MenuName"] = m.MenuName ?? "",
                    ["Controller"] = m.Controller ?? "",
                    ["DisplayOrder"] = m.DisplayOrder,
                    ["IsActive"] = m.IsActive ? "Yes" : "No"
                }).ToList()
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, nameof(Export), nameof(MenuController), $"Error exporting menus. Format: '{format}'.");
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while exporting.");
        }
    }
}