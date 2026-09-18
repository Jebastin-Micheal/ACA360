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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Threading.Tasks;

[Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
public class RoleController : BaseController
{
    private readonly IRoleService _roleService;
    private readonly ILoggerService _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IExportService _exportService;

    public RoleController(IRoleService roleService, ILoggerService logger, IWebHostEnvironment environment, IExportService exportService)
    {
        _roleService = roleService;
        _logger = logger;
        _environment = environment;
        _exportService = exportService;
    }

    #region Roles

    /// <summary>
    /// Retrieves the main index view for the Roles section, including a paginated and sortable list of roles.
    /// </summary>
    [AuthorizePermission("Index")]
    public async Task<IActionResult> Index(int pageIndex = 1, string search = "", int sortColumn = 0, string sortOrder = "asc")
    {
        try
        {
            // Set up pagination parameters for the service query based on user inputs
            PaginationEntity paginationEntity = new PaginationEntity
            {
                PageIndex = pageIndex,
                PageSize = 10,
                Search = search,
                SortColumn = sortColumn,
                SortOrder = sortOrder
            };

            // Fetch the role list and associated pagination metadata from the service
            var (roles, metadata) = await _roleService.GetRoleList(paginationEntity);

            // Construct the view model to pass data securely to the view
            var viewModel = new RoleViewModel
            {
                Roles = roles,
                Metadata = metadata,
                Search = search,
                SortColumn = sortColumn,
                SortOrder = sortOrder
            };

            return View(viewModel);
        }
        catch (Exception ex)
        {
            // Log the exception details and return a standard 500 status code
            _logger.LogError(ex, nameof(Index), nameof(RoleController), $"Error loading Roles list for pageIndex: {pageIndex}, search: {search}");
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the Roles list.");
        }
    }

    /// <summary>
    /// Retrieves a partial view containing the list of roles based on pagination and search criteria.
    /// Primarily used to refresh the UI via AJAX datatable updates.
    /// </summary>
    [AuthorizePermission("Index")]
    public async Task<IActionResult> RoleListPartial(int pageIndex, string search, int sortColumn, string sortOrder, int PageSize)
    {
        try
        {
            // Construct the pagination entity required for data retrieval
            PaginationEntity paginationEntity = new PaginationEntity
            {
                PageIndex = pageIndex,
                PageSize = PageSize,
                Search = search,
                SortColumn = sortColumn,
                SortOrder = sortOrder
            };

            // Retrieve paginated role data and metadata
            var (roles, metadata) = await _roleService.GetRoleList(paginationEntity);

            // Prepare the view model for partial view rendering
            var viewModel = new RoleViewModel
            {
                Roles = roles,
                Metadata = metadata,
                Search = search,
                SortColumn = sortColumn,
                SortOrder = sortOrder
            };

            return PartialView("_RoleListPartial", viewModel);
        }
        catch (Exception ex)
        {
            // Log the failure to load the partial list and safely return a 500 server error
            _logger.LogError(ex, nameof(RoleListPartial), nameof(RoleController), $"Error loading Role list partial for pageIndex: {pageIndex}");
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the role list.");
        }
    }

    /// <summary>
    /// Retrieves specific role details by ID to populate the add/edit role modal form.
    /// </summary>
    public async Task<IActionResult> GetRole_by_ID(int? id)
    {
        try
        {
            RoleFormDataDto dto = null;

            // Fetch existing role data if a valid ID parameter is provided
            if (id.HasValue)
            {
                dto = await _roleService.GetRoleFormData(id.Value);
            }

            // Construct the view model, providing an empty Role object if no data was found or if creating new
            var model = new RoleFormViewModel
            {
                Role = dto?.Role ?? new Role(),
            };

            return PartialView("_RoleFormModal", model);
        }
        catch (Exception ex)
        {
            // Log the exception and return a standard 500 internal server error
            _logger.LogError(ex, nameof(GetRole_by_ID), nameof(RoleController), $"Error fetching Role form for id: {id}");
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while fetching the role details.");
        }
    }

    /// <summary>
    /// Returns a partial view with an empty role form model to facilitate creating a new role.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Add_New()
    {
        try
        {
            RoleFormDataDto dto = null;

            // Create a blank view model for the new role creation modal
            var model = new RoleFormViewModel
            {
                Role = dto?.Role ?? new Role(),
            };

            return PartialView("_RoleFormModal", model);
        }
        catch (Exception ex)
        {
            // Log the failure to generate the new role form and return a 500 error
            _logger.LogError(ex, nameof(Add_New), nameof(RoleController), "Error generating the new Role form.");
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while opening the new role form.");
        }
    }

    /// <summary>
    /// Processes the submitted form to save a newly created or updated role to the system.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddOrUpdateMenu(RoleFormViewModel model)
    {
        try
        {
            // Validate the incoming view model payload structurally
            if (model == null || model.Role == null)
            {
                return BadRequest(new { success = false, message = "Invalid input data. Role information is required." });
            }

            // Execute the service layer method to persist the role changes
            _roleService.AddOrUpdateRole(model.Role);

            return Json(new { success = true, message = "Role saved successfully." });
        }
        catch (Exception ex)
        {
            // Log the data persistence error and return a formatted 500 JSON response for frontend consumption
            _logger.LogError(ex, nameof(AddOrUpdateMenu), nameof(RoleController), "Error saving role information.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while saving the role." });
        }
    }

    /// <summary>
    /// Deletes a specific role from the system based on its unique identifier.
    /// </summary>
    [AuthorizePermission("Index")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(int id)
    {
        try
        {
            // Input validation: ensure a valid positive integer ID is provided
            if (id <= 0)
            {
                return BadRequest(new { success = false, message = "A valid role ID must be provided." });
            }

            // Perform the deletion operation via the repository/service
            _roleService.DeleteRole(id);

            return Json(new { success = true, message = "Role deleted successfully." });
        }
        catch (Exception ex)
        {
            // Log the deletion failure and respond with a 500 internal server error
            _logger.LogError(ex, nameof(Delete), nameof(RoleController), $"Error deleting Role with ID: {id}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while deleting the role." });
        }
    }

    /// <summary>
    /// Exports the full Role list in the requested format (print / csv / excel / pdf).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Export(string format = "excel", string search = "", string title = "Role List")
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

            var (roles, _) = await _roleService.GetRoleList(paginationEntity);

            return HandleExport(
                _exportService,
                format,
                title: title,
                fileName: "RoleList",
                columns: new List<ExportColumn>
                {
                    new ExportColumn("RoleName",    "Role Name"),
                    new ExportColumn("Description", "Description"),
                    new ExportColumn("IsActive",    "Active")
                },
                rows: roles.Select(r => new Dictionary<string, object>
                {
                    ["RoleName"] = r.RoleName ?? "",
                    ["Description"] = r.Description ?? "",
                    ["IsActive"] = r.IsActive ? "Yes" : "No"
                }).ToList()
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, nameof(Export), nameof(RoleController), $"Error exporting roles. Format: '{format}'.");
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while exporting.");
        }
    }

    #endregion
}