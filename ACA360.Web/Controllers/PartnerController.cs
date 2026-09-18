using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Core.Models.Tracker;
using ACA360.Core.ViewModels;
using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class PartnerController : Controller
    {
        private readonly IPartnerRepository _repo;                    // Repository handling all partner data access
        private readonly ILogger<PartnerController> _logger;          // Logger for capturing errors and diagnostics

        /// <summary>
        /// Initializes the PartnerController with required repository and logger dependencies.
        /// </summary>
        /// <param name="repo">Repository responsible for firm and broker data operations.</param>
        /// <param name="logger">Logger instance for runtime diagnostics and error reporting.</param>
        public PartnerController(IPartnerRepository repo, ILogger<PartnerController> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // =========================================================
        // 1. MAIN PAGE (Container)
        // =========================================================

        /// <summary>
        /// Renders the main Partner page which hosts the split-screen layout.
        /// Left pane: Firms grid — Right pane: Brokers grid.
        /// GET: /Partner/Index
        /// </summary>
        /// <returns>The Index view acting as the shell container for both panes.</returns>
        public IActionResult Index()
        {
            // This loads the split-screen view (Left: Firms, Right: Brokers)
            return View();
        }

        // =========================================================
        // 2. FIRM ACTIONS (Left Pane)
        // =========================================================

        /// <summary>
        /// Returns a paginated, sortable, and searchable partial view of firms for the left pane grid.
        /// GET: /Partner/GetFirmGrid
        /// </summary>
        /// <param name="search">Optional search term to filter firms by name or other fields.</param>
        /// <param name="sortCol">Column name to sort by (default: "FirmName").</param>
        /// <param name="sortOrder">Sort direction — "ASC" or "DESC" (default: "ASC").</param>
        /// <param name="page">Current page number for pagination (default: 1).</param>
        /// <param name="pageSize">Number of records per page (default: 10).</param>
        /// <returns>A partial view of the firm list, or an empty model on failure to prevent view crash.</returns>
        [HttpGet]
        public async Task<IActionResult> GetFirmGrid(string search, string sortCol = "FirmName", string sortOrder = "ASC", int page = 1, int pageSize = 10)
        {
            try
            {
                // Fetch the paginated firm list from the repository using the provided filters and sort parameters
                var data = await _repo.GetFirmsAsync(search, sortCol, sortOrder, page, pageSize);

                // Map repository result into the view model for the partial view
                var model = new PartnerListViewModel<FirmItem>
                {
                    Items = data.List,
                    TotalItems = data.TotalCount,
                    CurrentPage = page,
                    PageSize = pageSize,
                    SortColumn = sortCol,
                    SortOrder = sortOrder
                };

                return PartialView("_FirmList", model);
            }
            catch (Exception ex)
            {
                // Log the failure with search context; return an empty model to prevent the partial view from crashing
                _logger.LogError(ex, "An unexpected error occurred in GetFirmGrid. Search: {Search}, Page: {Page}", search, page);
                return PartialView("_FirmList", new PartnerListViewModel<FirmItem> { Items = new List<FirmItem>() });
            }
        }

        /// <summary>
        /// Loads the firm add/edit modal form as a partial view.
        /// Fetches an existing firm by ID or initialises a blank model for new entries.
        /// GET: /Partner/FirmForm?id={id}
        /// </summary>
        /// <param name="id">The firm ID to edit, or 0 to create a new firm.</param>
        /// <returns>A partial view containing the firm form, pre-populated with states dropdown data.</returns>
        [HttpGet]
        public async Task<IActionResult> FirmForm(int id)
        {
            try
            {
                // For new firms (id == 0), initialise a blank active model; otherwise fetch the existing record
                FirmItem firm = id == 0
                    ? new FirmItem { IsActive = true }
                    : await _repo.GetFirmByIdAsync(id);

                // Fetch the full list of states to populate the dropdown
                var states = await _repo.GetAllStatesAsync();

                // Build the SelectList ensuring the correct StateId is pre-selected on the form
                // Value: StateId, Text: StateName, SelectedValue: firm.StateId
                ViewBag.States = new SelectList(states, "StateId", "StateName", firm.StateId);

                return PartialView("_FirmForm", firm);
            }
            catch (Exception ex)
            {
                // Log failure with the firm ID for traceability; return a JSON error for graceful modal handling
                _logger.LogError(ex, "An unexpected error occurred in FirmForm. FirmId: {FirmId}", id);
                return Json(new { success = false, message = "Error loading firm form. Please try again." });
            }
        }

        /// <summary>
        /// Saves a new or existing firm record after server-side model validation.
        /// POST: /Partner/SaveFirm
        /// </summary>
        /// <param name="model">The firm data submitted from the modal form.</param>
        /// <returns>JSON indicating success or failure, with validation or exception messages where applicable.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveFirm(FirmItem model)
        {
            try
            {
                // Validate the submitted model state before persisting — surface all field-level errors to the client
                if (!ModelState.IsValid)
                {
                    var errors = string.Join("; ", ModelState.Values
                                                .SelectMany(x => x.Errors)
                                                .Select(x => x.ErrorMessage));
                    return Json(new { success = false, message = "Validation Failed: " + errors });
                }

                // Persist the firm record via the repository (handles both insert and update)
               int id= await _repo.SaveFirmAsync(model);

                return Json(new { success = true, id, name = model.FirmName });
            }
            catch (ArgumentException ex)
            {
                // Handle validation-level failures raised by the repository or domain layer
                _logger.LogWarning(ex, "Validation error in SaveFirm. FirmId: {FirmId}", model?.FirmId);
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                // Log unexpected SQL or system-level failures with the firm context for traceability
                _logger.LogError(ex, "An unexpected error occurred in SaveFirm. FirmId: {FirmId}", model?.FirmId);
                return Json(new { success = false, message = "An unexpected error occurred while saving the firm." });
            }
        }

        /// <summary>
        /// Deletes a firm record by its unique identifier.
        /// POST: /Partner/DeleteFirm
        /// </summary>
        /// <param name="id">The unique identifier of the firm to delete.</param>
        /// <returns>JSON indicating success or failure with an error message if applicable.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFirm(int id)
        {
            try
            {
                // Guard against invalid IDs before hitting the repository
                if (id <= 0)
                    return Json(new { success = false, message = "A valid firm ID is required." });

                // Delegate the delete operation to the repository layer
                await _repo.DeleteFirmAsync(id);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log unexpected failures with the firm ID for traceability
                _logger.LogError(ex, "An unexpected error occurred in DeleteFirm. FirmId: {FirmId}", id);
                return Json(new { success = false, message = "An unexpected error occurred while deleting the firm." });
            }
        }

        // =========================================================
        // 3. BROKER ACTIONS (Right Pane)
        // =========================================================

        /// <summary>
        /// Returns a paginated, sortable, and searchable partial view of brokers for the right pane grid.
        /// Returns an empty list immediately if no firm is selected in the left pane.
        /// GET: /Partner/GetBrokerGrid
        /// </summary>
        /// <param name="search">Optional search term to filter brokers by name or other fields.</param>
        /// <param name="firmId">The parent firm ID to scope broker results — required for a non-empty grid.</param>
        /// <param name="sortCol">Column name to sort by (default: "BrokerName").</param>
        /// <param name="sortOrder">Sort direction — "ASC" or "DESC" (default: "ASC").</param>
        /// <param name="page">Current page number for pagination (default: 1).</param>
        /// <returns>A partial view of the broker list scoped to the selected firm, or an empty model on failure.</returns>
        [HttpGet]
        public async Task<IActionResult> GetBrokerGrid(string search, int? firmId, string sortCol = "BrokerName", string sortOrder = "ASC", int page = 1)
        {
            try
            {
                // Short-circuit: if no firm is selected in the left pane, return an empty broker grid immediately
                if (firmId == null || firmId == 0)
                    return PartialView("_BrokerList", new PartnerListViewModel<BrokerItem> { Items = new List<BrokerItem>() });

                // Fetch the paginated broker list scoped to the selected firm
                var data = await _repo.GetBrokersAsync(search, firmId, sortCol, sortOrder, page, 10);

                // Map repository result into the view model for the partial view
                var model = new PartnerListViewModel<BrokerItem>
                {
                    Items = data.List,
                    TotalItems = data.TotalCount,
                    CurrentPage = page,
                    PageSize = 10,
                    SortColumn = sortCol,
                    SortOrder = sortOrder
                };

                return PartialView("_BrokerList", model);
            }
            catch (Exception ex)
            {
                // Log failure with firm and search context; return an empty model to prevent the partial view from crashing
                _logger.LogError(ex, "An unexpected error occurred in GetBrokerGrid. FirmId: {FirmId}, Search: {Search}, Page: {Page}", firmId, search, page);
                return PartialView("_BrokerList", new PartnerListViewModel<BrokerItem> { Items = new List<BrokerItem>() });
            }
        }

        /// <summary>
        /// Loads the broker add/edit modal form as a partial view.
        /// Pre-populates the firm and state dropdowns, and optionally pre-selects the parent firm.
        /// GET: /Partner/BrokerForm?id={id}&parentFirmId={parentFirmId}
        /// </summary>
        /// <param name="id">The broker ID to edit, or 0 to create a new broker.</param>
        /// <param name="parentFirmId">Optional parent firm ID to pre-select in the firm dropdown when adding a new broker.</param>
        /// <returns>A partial view containing the broker form, or a JSON error response if the form fails to load.</returns>
        [HttpGet]
        public async Task<IActionResult> BrokerForm(int id, int? parentFirmId = null)
        {
            try
            {
                // Fetch the firm dropdown data to populate the firm selector in the modal
                var firms = await _repo.GetFirmDropdownAsync();

                // Fetch the full list of states to populate the state dropdown
                var states = await _repo.GetAllStatesAsync();

                // Pre-select the parent firm in the dropdown when adding a new broker from a selected firm context
                ViewBag.Firms = new SelectList(firms, "FirmId", "FirmName", parentFirmId);

                BrokerItem broker;
                if (id == 0)
                {
                    // Initialise a new broker and pre-assign the parent firm if provided from the UI context
                    broker = new BrokerItem { FirmId = parentFirmId ?? 0, IsActive = true };
                }
                else
                {
                    // Fetch the existing broker record for editing
                    broker = await _repo.GetBrokerByIdAsync(id);
                }

                // Build the state SelectList ensuring the broker's current state is pre-selected
                ViewBag.States = new SelectList(states, "StateId", "StateName", broker.StateId);

                return PartialView("_BrokerForm", broker);
            }
            catch (Exception ex)
            {
                // Log failure with broker and firm context; return a JSON error for graceful modal handling
                _logger.LogError(ex, "An unexpected error occurred in BrokerForm. BrokerId: {BrokerId}, ParentFirmId: {ParentFirmId}", id, parentFirmId);
                return Json(new { success = false, message = "Error loading broker form. Please try again." });
            }
        }

        /// <summary>
        /// Saves a new or existing broker record after server-side model validation.
        /// POST: /Partner/SaveBroker
        /// </summary>
        /// <param name="model">The broker data submitted from the modal form.</param>
        /// <returns>JSON indicating success or failure, with validation or exception messages where applicable.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveBroker(BrokerItem model)
        {
            try
            {
                // Validate the submitted model state before persisting — reject early on any field-level failures
                if (!ModelState.IsValid)
                    return Json(new { success = false, message = "Validation Failed" });

                // Persist the broker record via the repository (handles both insert and update)
               int id= await _repo.SaveBrokerAsync(model);

                return Json(new { success = true, id, name = model.BrokerName });

            }
            catch (ArgumentException ex)
            {
                // Handle validation-level failures raised by the repository or domain layer
                _logger.LogWarning(ex, "Validation error in SaveBroker. BrokerId: {BrokerId}, FirmId: {FirmId}", model?.BrokerId, model?.FirmId);
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                // Log unexpected SQL or system-level failures with broker context for traceability
                _logger.LogError(ex, "An unexpected error occurred in SaveBroker. BrokerId: {BrokerId}, FirmId: {FirmId}", model?.BrokerId, model?.FirmId);
                return Json(new { success = false, message = "An unexpected error occurred while saving the broker." });
            }
        }

        /// <summary>
        /// Deletes a broker record by its unique identifier.
        /// POST: /Partner/DeleteBroker
        /// </summary>
        /// <param name="id">The unique identifier of the broker to delete.</param>
        /// <returns>JSON indicating success or failure with an error message if applicable.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBroker(int id)
        {
            try
            {
                // Guard against invalid IDs before hitting the repository
                if (id <= 0)
                    return Json(new { success = false, message = "A valid broker ID is required." });

                // Delegate the delete operation to the repository layer
                await _repo.DeleteBrokerAsync(id);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log unexpected failures with the broker ID for traceability
                _logger.LogError(ex, "An unexpected error occurred in DeleteBroker. BrokerId: {BrokerId}", id);
                return Json(new { success = false, message = "An unexpected error occurred while deleting the broker." });
            }
        }
    }
}