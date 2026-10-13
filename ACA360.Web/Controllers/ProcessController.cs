using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Core.Models.Tracker;
using ACA360.Core.ViewModels;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class ProcessController : Controller
    {
        private readonly IProcessRepository _repo;
        private readonly ILoggerService _logger;

        public ProcessController(IProcessRepository repo, ILoggerService logger)
        {
            _repo = repo;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves the main index view for the Process tracking module.
        /// Pre-loads available categories to populate the UI filtering dropdowns.
        /// </summary>
        /// <returns>The default Index view.</returns>
        public async Task<IActionResult> Index()
        {
            try
            {
                // Fetch categories asynchronously to populate the view's select list
                var categories = await _repo.GetCategoriesAsync();
                ViewBag.Categories = new SelectList(categories, "CategoryId", "CategoryName");

                // Render the main tracking view
                return View();
            }
            catch (Exception ex)
            {
                // Log the exception if fetching categories or rendering the view fails
                _logger.LogError(ex, nameof(Index), nameof(ProcessController), "An error occurred while loading the Process tracking Index view.");

                // Return a generic 500 Internal Server Error status code
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the page.");
            }
        }

        /// <summary>
        /// Retrieves a paginated, sorted, and filtered list of processes to populate an AJAX datatable.
        /// </summary>
        /// <returns>A partial view containing the formatted process list.</returns>
        [HttpGet]
        public async Task<IActionResult> GetProcessList(string search, int? categoryId, string sortColumn = "DisplayOrder", string sortOrder = "ASC", int pageIndex = 1, int pageSize = 10)
        {
            try
            {
                var data = await _repo.GetProcessListAsync(search, categoryId, sortColumn, sortOrder, pageIndex, pageSize);
                int totalPages = pageSize > 0 ? (int)Math.Ceiling((double)data.TotalCount / pageSize) : 0;
                var model = new ProcessListViewModel
                {
                    Processes = data.List,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder,
                    Metadata = new PaginationViewEntity
                    {
                        TotalItems = data.TotalCount,
                        CurrentPage = pageIndex,
                        PageSize = pageSize,
                        TotalPages = totalPages,
                        TotalCount = data.TotalCount,
                        PageNumber = pageIndex
                    }
                };
                return PartialView("_ProcessListPartial", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetProcessList), nameof(ProcessController), $"An error occurred while retrieving the process list (Page: {pageIndex}, Search: '{search}').");
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving the process list.");
            }
        }


        /// <summary>
        /// Serves the Add/Update modal form for a specific process.
        /// If the ID is 0, it provides a blank form for creation.
        /// </summary>
        /// <param name="id">The unique identifier of the process to edit, or 0 to create a new one.</param>
        /// <returns>A partial view containing the Add/Update form.</returns>
        [HttpGet]
        public async Task<IActionResult> AddUpdate(int id)
        {
            try
            {
                // Populate the category dropdown options required by the form
                var categories = await _repo.GetCategoriesAsync();
                ViewBag.Categories = new SelectList(categories, "CategoryId", "CategoryName");

                // Branch logic: Provide a clean model for creation operations
                if (id == 0)
                {
                    return PartialView("_AddUpdateProcess", new TrackerProcess());
                }

                // Branch logic: Fetch and provide the existing entity for edit operations
                var process = await _repo.GetProcessByIdAsync(id);
                if (process == null)
                {
                    return NotFound("The requested process could not be found.");
                }

                return PartialView("_AddUpdateProcess", process);
            }
            catch (Exception ex)
            {
                // Log the issue loading the process details or dropdown options
                _logger.LogError(ex, nameof(AddUpdate), nameof(ProcessController), $"An error occurred while loading the Add/Update form for Process ID {id}.");

                // Return a generic 500 error to the client
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the form.");
            }
        }

        /// <summary>
        /// Saves a new or modified process entity back to the underlying repository.
        /// </summary>
        /// <param name="model">The TrackerProcess payload submitted by the client.</param>
        /// <returns>A JSON response confirming success or providing error context.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(TrackerProcess model)
        {
            try
            {
                if (model.IsDoNotDisplay)
                {
                    model.FollowUpDays = 0;
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new { success = false, message = "Invalid data submitted. Please correct your inputs." });
                }

                await _repo.AddUpdateProcessAsync(model);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Save), nameof(ProcessController), $"An error occurred while saving the process.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while saving the process." });
            }
        }

        /// <summary>
        /// Removes a specific process from the system by its identifier.
        /// </summary>
        /// <param name="id">The unique ID of the process to delete.</param>
        /// <returns>A JSON response confirming successful deletion.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                // Prevent invalid deletion attempts
                if (id <= 0)
                {
                    return BadRequest(new { success = false, message = "A valid Process ID is required for deletion." });
                }

                // Issue the deletion command to the database repository
                await _repo.DeleteProcessAsync(id);

                // Notify the frontend of success to trigger a grid refresh
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Handle deletion errors, such as foreign key constraints or missing records
                _logger.LogError(ex, nameof(Delete), nameof(ProcessController), $"An error occurred while attempting to delete Process ID {id}.");

                // Forward a generic 500 failure JSON payload
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while deleting the process." });
            }
        }
    }
}