using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Core.Models.Tracker;
using ACA360.Core.ViewModels;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class ServiceController : Controller
    {
        private readonly IServiceRepository _repo;
        private readonly ILoggerService _logger;

        public ServiceController(IServiceRepository repo, ILoggerService logger)
        {
            _repo = repo;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves the main view for the Service tracking module.
        /// </summary>
        /// <returns>The primary Index view.</returns>
        public IActionResult Index()
        {
            try
            {
                // Load the initial view skeleton; grid data is loaded subsequently via AJAX
                return View();
            }
            catch (Exception ex)
            {
                // Log unexpected errors during view initialization
                _logger.LogError(ex, nameof(Index), nameof(ServiceController), "An error occurred while loading the Service index view.");

                // Return a 500 Internal Server Error status
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the page.");
            }
        }

        /// <summary>
        /// Retrieves a paginated, filtered, and sorted list of services for the data grid.
        /// </summary>
        /// <param name="search">Search text to filter services.</param>
        /// <param name="taxYear">Optional tax year filter.</param>
        /// <param name="sortCol">Column to sort by.</param>
        /// <param name="sortOrder">Direction of the sort (ASC/DESC).</param>
        /// <param name="page">Current page number.</param>
        /// <param name="pageSize">Number of records per page.</param>
        /// <returns>A partial view containing the datatable rows.</returns>
        [HttpGet]
        public async Task<IActionResult> GetServiceList(string search, string sortColumn = "ServiceName",
string sortOrder = "ASC", int pageIndex = 1, int pageSize = 10)
        {
            try
            {
                var data = await _repo.GetServiceListAsync(search, sortColumn, sortOrder, pageIndex, pageSize);
                int totalPages = pageSize > 0 ? (int)Math.Ceiling((double)data.TotalCount / pageSize) : 0;
                var model = new ServiceListViewModel
                {
                    Services = data.List,
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
                return PartialView("_ServiceListPartial", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetServiceList), nameof(ServiceController), $"An error occurred while retrieving the service list. Search: '{search}', Page: {pageIndex}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while fetching the service list.");
            }
        }

        /// <summary>
        /// Retrieves the modal form for creating a new service or editing an existing one.
        /// </summary>
        /// <param name="id">The ID of the service to edit, or 0 for a new record.</param>
        /// <returns>A partial view containing the form.</returns>
        [HttpGet]
        public async Task<IActionResult> AddUpdate(int id)
        {
            try
            {
                // If ID is 0, initialize a new service object using the current year as a default
                if (id == 0)
                {
                    return PartialView("_AddUpdateService", new ServiceItem());
                }

                // Fetch the existing service entity from the database for editing
                var service = await _repo.GetServiceByIdAsync(id);
                if (service == null)
                {
                    return NotFound("The requested service could not be found.");
                }

                // Render the partial form view populated with the existing service data
                return PartialView("_AddUpdateService", service);
            }
            catch (Exception ex)
            {
                // Log failures during form data retrieval
                _logger.LogError(ex, nameof(AddUpdate), nameof(ServiceController), $"An error occurred while loading the Add/Update form for Service ID {id}.");

                // Return a 500 server error status
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the form.");
            }
        }

        /// <summary>
        /// Processes the form submission to insert or update a service record in the database.
        /// </summary>
        /// <param name="model">The service object containing user inputs.</param>
        /// <returns>A JSON object indicating success or failure.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(ServiceItem model)
        {
            try
            {
                // Validate the submitted model properties against data annotations
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { success = false, message = "Invalid data submitted. Please check your inputs." });
                }

                // Execute the add/update operation via the repository layer
                await _repo.AddUpdateServiceAsync(model);

                // Return a success JSON payload to the calling script
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log the persistence failure
                _logger.LogError(ex, nameof(Save), nameof(ServiceController), $"An error occurred while saving the service");

                // Return a 500 status code indicating the database transaction failed
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while saving the service." });
            }
        }

        /// <summary>
        /// Removes a specific service record from the system.
        /// </summary>
        /// <param name="id">The unique identifier of the service to delete.</param>
        /// <returns>A JSON response confirming the status of the deletion.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                // Ensure the provided identifier is valid before attempting deletion
                if (id <= 0)
                {
                    return BadRequest(new { success = false, message = "A valid Service ID is required." });
                }

                // Issue the delete command to the repository
                await _repo.DeleteServiceAsync(id);

                // Notify the client of successful deletion
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log the exception if the deletion fails (e.g., due to database locks or constraints)
                _logger.LogError(ex, nameof(Delete), nameof(ServiceController), $"An error occurred while attempting to delete Service ID {id}.");

                // Return a 500 internal server error payload to safely alert the UI
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while deleting the service." });
            }
        }
    }
}