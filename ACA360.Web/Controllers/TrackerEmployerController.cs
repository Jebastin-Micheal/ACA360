using ACA360.Core.Constants;
using ACA360.Core.Models.Tracker;
using ACA360.Core.Models.Dashboard;
using ACA360.Core.ViewModels;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.CodeAnalysis;
using NPOI.SS.Formula.Functions;
using System.Threading.Tasks;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;

using System.Drawing;
using System.Security.Claims;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Web.Filters;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class TrackerEmployerController : BaseController
    {
        private readonly ITrackerEmployerRepository _repo;
        private readonly IPartnerRepository _partnerRepo;
        private readonly IServiceRepository _serviceRepo;
        private readonly IEmployerService _employerRepo;
        private readonly ILoggerService _logger;
        private readonly IDashboardService _dashboardService;
        public TrackerEmployerController(
            ITrackerEmployerRepository repo,
            IPartnerRepository partnerRepo,
            IServiceRepository serviceRepo,
            IEmployerService employerRepo,
            ILoggerService logger,
            IDashboardService dashboardService)
        {
            _repo = repo;
            _partnerRepo = partnerRepo;
            _serviceRepo = serviceRepo;
            _employerRepo = employerRepo;
            _logger = logger;
            _dashboardService = dashboardService;
        }


        /// <summary>
        /// Retrieves the main index view for the Tracker Employer module.
        /// </summary>
        /// <returns>The primary index view.</returns>
        public IActionResult Index()
        {
            try
            {
                // Render the main dashboard view for tracker employers
                return View();
            }
            catch (Exception ex)
            {
                // Log and gracefully fail if view rendering throws an unexpected exception
                _logger.LogError(ex, nameof(Index), nameof(TrackerEmployerController), "An error occurred while loading the Tracker Employer Index view.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the page.");
            }
        }

        /// <summary>
        /// Retrieves a paginated and filtered list of tracker employers.
        /// </summary>
        /// <param name="filter">The filter criteria for pagination, sorting, and searching.</param>
        /// <returns>A partial view containing the list of employers.</returns>
        [HttpGet]
        public async Task<IActionResult> GetList(EmployerFilterModel filter)
        {
            try
            {
                // Validate incoming filter parameters
                if (filter == null)
                {
                    return BadRequest("Invalid filter parameters provided.");
                }

                // Retrieve data based on the provided filter
                var data = await _repo.GetListAsync(filter);

                // Construct the view model for the partial view
                var model = new PartnerListViewModel<TrackerEmployerItem>
                {
                    Items = data.Item1,
                    TotalItems = data.Item2,
                    CurrentPage = filter.Page,
                    PageSize = filter.PageSize,
                    SortColumn = filter.SortColumn,
                    SortOrder = filter.SortOrder
                };

                return PartialView("_EmployerList", model);
            }
            catch (Exception ex)
            {
                // Log and handle data retrieval failures safely
                _logger.LogError(ex, nameof(GetList), nameof(TrackerEmployerController), "An error occurred while fetching the employer list.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the employer list.");
            }
        }

        /// <summary>
        /// Retrieves the filter options to populate sidebar dropdowns.
        /// </summary>
        /// <returns>A JSON object containing firm options.</returns>
        [HttpGet]
        public async Task<IActionResult> GetFilterOptions()
        {
            try
            {

                var dd = await _repo.GetDropdownDataAsync(0);

                return Json(new
                {
                    firms = dd.Firms.Select(x => new
                    {
                        firmId = x.Id,
                        firmName = x.Name
                    }),
                    brokers = dd.Brokers.Select(x => new
                    {
                        id = x.Id,
                        name = x.Name
                    }),
                    accountManagers = dd.AccountManagers.Select(x => new
                    {
                        id = x.Id,
                        name = x.Name
                    }),
                    dataAnalysts = dd.DataAnalysts.Select(x => new
                    {
                        id = x.Id,
                        name = x.Name
                    }),
                    salesReps = dd.SalesReps.Select(x => new
                    {
                        id = x.Id,
                        name = x.Name
                    }),
                    industries = dd.Industries.Select(x => new
                    {
                        id = x.Id,
                        name = x.Name
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetFilterOptions), nameof(TrackerEmployerController),
                    "An error occurred while fetching filter options.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { success = false, message = "An error occurred while fetching filter options." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMainEmployers()
        {
            try
            {
                var list = await _repo.GetMainEmployersForDropdownAsync();
                return Json(list.Select(x => new { value = x.EmployerId, text = x.AffiliateName }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetMainEmployers),
                    nameof(TrackerEmployerController), "Error fetching main employers.");
                return StatusCode(500, new { success = false });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAffiliatesByMain(int mainEmployerId)
        {
            try
            {
                var list = await _repo.GetAffiliatesForDropdownAsync(mainEmployerId);
                return Json(list.Select(x => new { value = x.EmployerId, text = x.AffiliateName }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAffiliatesByMain),
                    nameof(TrackerEmployerController), $"mainEmployerId:{mainEmployerId}");
                return StatusCode(500, new { success = false });
            }
        }

        /// <summary>
        /// Retrieves the modal form for creating or editing an employer.
        /// Populates required UI dropdown lists based on existing lookup data.
        /// </summary>
        /// <param name="id">The employer ID to edit, or 0 for a new employer.</param>
        /// <returns>A partial view containing the populated form.</returns>
        [HttpGet]
        public async Task<IActionResult> Form(int id)
        {
            try
            {
                // Fetch all structural dropdown options needed for the employer form
                // var dropdowns = await _employerRepo.GetEmployerDropdownDataAsync(null, null, null, false);
                var dropdowns = await _repo.GetDropdownDataAsync(id);
                ViewBag.Firms = new SelectList(dropdowns.Firms, "Id", "Name");
                ViewBag.Brokers = new SelectList(dropdowns.Brokers, "Id", "Name");
                ViewBag.AcctManagers = new SelectList(dropdowns.AccountManagers, "Id", "Name");
                ViewBag.DataAnalysts = new SelectList(dropdowns.DataAnalysts, "Id", "Name");
                ViewBag.SalesReps = new SelectList(dropdowns.SalesReps, "Id", "Name");
                ViewBag.Industries = new SelectList(dropdowns.Industries, "Id", "Name");
                ViewBag.DataTypes = new SelectList(dropdowns.DataTypes, "Id", "Name");
                ViewBag.States = new SelectList(dropdowns.StatesACA.Select(x => new { x.Code, Display = $"{x.Code} - {x.State}" }), "Code", "Display");

                ViewBag.BandingTypes = new SelectList(new[] { "Age Banded", "Salary Banded", "Composite Rate" });
                ViewBag.ComplexityLevels = new SelectList(new[] { "Standard", "Medium", "High", "Complex" });
                ViewBag.Communications = new SelectList(new[] { "Weekly Calls", "Monthly Calls", "Email Only" });
                ViewBag.PlanTerminations = new SelectList(new[] { "End of Month", "Date of Event", "Immediate" });
                ViewBag.DataFrequencies = new SelectList(new[] { "Weekly", "Bi-Weekly", "Monthly", "Quarterly", "Initial/Final File", "Other" });
                // Differentiate logic: provide a blank model for new inserts vs fetching an existing record
                if (id == 0)
                {
                    return PartialView("_EmployerForm", new TrackerEmployerItem
                    {
                        Filingyear = GetCurrentFilingYear().ToString()
                    });
                }

                // Fetch existing employer entity
                var model = await _repo.GetByIdAsync(id);
                if (string.IsNullOrEmpty(model.Filingyear))
                {
                    model.Filingyear = GetCurrentFilingYear().ToString();
                }
                ViewBag.Contacts = new SelectList(dropdowns.GeneralContacts, "Id", "Name", model.ACAContactId);
                // Surface an existing portal login so the edit form shows the
                // read-only username + "Change Password" branch.
                var portal = await _repo.GetPortalUserByEmployerAsync(id);
                if (portal != null)
                {
                    model.HasPortalLogin = true;
                    model.PortalUserName = portal.Value.UserName;
                }
                return PartialView("_EmployerForm", model);
            }
            catch (Exception ex)
            {
                // Log form rendering failures and gracefully return a server error
                _logger.LogError(ex, nameof(Form), nameof(TrackerEmployerController), $"An error occurred while loading the employer form for ID {id}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the form.");
            }
        }

        /// <summary>
        /// Validates and saves a new or existing tracker employer record.
        /// </summary>
        /// <param name="model">The employer item bound from the submitted form.</param>
        /// <returns>A JSON response containing the success status and the saved entity's ID.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(TrackerEmployerItem model)
        {
            ModelState.Remove("FirmName");
            try
            {
                // Remove FirmName from ModelState as it's display-only
                ModelState.Remove("FirmName");



                // Assign the current session filing year if not provided by the payload
                if (string.IsNullOrEmpty(model.Filingyear))
                {
                    model.Filingyear = GetCurrentFilingYear().ToString();
                }
                if (DataMasker.IsMaskedPlaceholder(model.EIN))
                {
                    var existing = await _repo.GetByIdAsync(model.EmployerId);
                    model.EIN = existing?.EIN;
                }
                // Persist the employer data via the repository
                int.TryParse(GetCurrentUserId(), out int currentUserId);
                var savedId = await _repo.SaveAsync(model);
                // Persist the portal login (tbl_User) — non-fatal: a login failure
                // must not undo an otherwise successful employer save.
                try
                {
                    await SavePortalLoginIfRequestedAsync(model, savedId);
                }
                catch (Exception pex)
                {
                    _logger.LogError(pex, nameof(Save), nameof(TrackerEmployerController), $"Employer {savedId} saved, but portal login persistence failed.");
                }
                return Json(new { success = true, employerId = savedId });
            }
            catch (Exception ex)
            {
                // Log validation or database errors during the save operation
                _logger.LogError(ex, nameof(Save), nameof(TrackerEmployerController), "An error occurred while saving the tracker employer.");
                return Json(new { success = false, message = ex.InnerException?.Message ?? ex.Message });
            }
        }
        /// <summary>
        /// Creates or updates the employer's portal login (tbl_User) based on the
        /// form choices. Create flow honours the "Don't create a portal login" opt-out;
        /// edit flow updates the password only when "Change Password" is ticked.
        /// </summary>
        private async Task SavePortalLoginIfRequestedAsync(TrackerEmployerItem model, int employerId)
        {
            if (employerId <= 0) return;

            var existing = await _repo.GetPortalUserByEmployerAsync(employerId);

            if (existing == null)
            {
                // No login yet — create only when the user opted in and supplied credentials.
                if (!model.CreatePortalLogin) return;
                if (string.IsNullOrWhiteSpace(model.PortalUserName) || string.IsNullOrWhiteSpace(model.PortalPassword)) return;

                await _repo.SavePortalLoginAsync(employerId, model.PortalUserName.Trim(), model.PortalPassword, 0);
            }
            else
            {
                // Existing login — keep the username current; change the password only on request.
                var userName = string.IsNullOrWhiteSpace(model.PortalUserName)
                    ? existing.Value.UserName
                    : model.PortalUserName.Trim();

                string newPassword = (model.ChangePortalPassword && !string.IsNullOrWhiteSpace(model.PortalPassword))
                    ? model.PortalPassword
                    : null;

                await _repo.SavePortalLoginAsync(employerId, userName, newPassword, existing.Value.UserId);
            }
        }
        /// <summary>
        /// Deletes an employer record by ID.
        /// </summary>
        /// <param name="id">The unique identifier of the employer.</param>
        /// <returns>A JSON response confirming the deletion.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                // Prevent invalid delete commands
                if (id <= 0)
                {
                    return BadRequest(new { success = false, message = "A valid Employer ID is required." });
                }

                // Perform the deletion operation
                await _repo.DeleteAsync(id);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log and handle deletion failures, such as dependency constraints
                _logger.LogError(ex, nameof(Delete), nameof(TrackerEmployerController), $"An error occurred while deleting the employer (ID: {id}).");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while deleting the employer." });
            }
        }

        /// <summary>
        /// Retrieves the list of services associated with a specific employer.
        /// </summary>
        /// <param name="employerId">The associated employer ID.</param>
        /// <returns>A partial view rendering the service list.</returns>
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetServices(int employerId)
        {
            try
            {
                string PlanYear = GetCurrentFilingYear().ToString();
                // Fetch and render the services specific to the requested employer
                var services = await _repo.GetServicesAsync(employerId, PlanYear);
                return PartialView("_ServiceList", services);
            }
            catch (Exception ex)
            {
                // Safely log and decline to render the partial view if an error occurs
                _logger.LogError(ex, nameof(GetServices), nameof(TrackerEmployerController), $"An error occurred while fetching services for Employer ID {employerId}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the services list.");
            }
        }

        /// <summary>
        /// Retrieves the form for adding or editing an employer service.
        /// </summary>
        /// <param name="employerId">The employer ID the service belongs to.</param>
        /// <param name="id">The specific service ID, or 0 for a new entry.</param>
        /// <returns>A partial view containing the service form.</returns>
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> ServiceForm(int employerId, int id = 0)
        {
            try
            {
                string PlanYear = GetCurrentFilingYear().ToString();
                // Load general service dropdown options
                var services = await _repo.GetServiceDropdownAsync();
                EmployerServiceItem model;

                // Branching logic to either fetch existing or instantiate new model
                if (id > 0)
                {
                    model = await _repo.GetServiceByIdAsync(id, PlanYear);
                }
                else
                {
                    model = new EmployerServiceItem { EmployerId = employerId };
                }
                if (int.TryParse(PlanYear, out int sessionPlanYear))
                {
                    model.PlanYear = sessionPlanYear;
                }
                // Initialize standard selection lists for UI binding
                ViewBag.Services = new SelectList(services, "ServiceId", "ServiceName", model.ServiceId);
                ViewBag.Statuses = new SelectList(new[] { "Opportunity", "Sold", "Lost" });
                ViewBag.Penalties = new SelectList(new[] { "4980H(A)", "4980H(B)", "4980H(A) & 4980H(B)", " N / A" });
                ViewBag.SafeHarbors = new SelectList(new[] { "F - W2", "G - Federal Poverty Line", "H - Rate of Pay", "Combination of Safe Harbors" });
                ViewBag.ProcessSteps = new SelectList(new[] { "Received Sold Case", "Data Collection", "Data Validation", "Form Generation", "Mailed" });
                ViewBag.Months = new SelectList(System.Globalization.DateTimeFormatInfo.CurrentInfo.MonthNames.Take(12));
                ViewBag.Payers = new SelectList(new[] { "Employer", "Firm", "Firm/Employer" });

                // Static list of auditing users for selection
                ViewBag.AuditUsers = new SelectList(new[]
                {
                    "Michelle Barki", "Christy Dalton", "Michael Bracken", "Ivy Vinson",
                    "Brittany Davis", "Marita Worthington", "Anna Saunders", "Crystal Dowell",
                    "Fadi Safar", "Karam Safar", "Nichole Trahan", "Victoria Amhrein",
                    "Maria Parks", "Charles Batton", "Other"
                });

                return PartialView("_ServiceForm", model);
            }
            catch (Exception ex)
            {
                // Log rendering exceptions
                _logger.LogError(ex, nameof(ServiceForm), nameof(TrackerEmployerController), $"An error occurred while loading the service form for Employer ID {employerId}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the service form.");
            }
        }

        /// <summary>
        /// Saves a new or updated employer service entity.
        /// </summary>
        /// <param name="model">The service object containing user inputs.</param>
        /// <returns>A JSON response confirming success.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveService(EmployerServiceItem model)
        {
            try
            {
                string PlanYear = GetCurrentFilingYear();
                // Execute the persistence logic
                var savedId = await _repo.SaveServiceAsync(model, PlanYear);
                return Json(new { success = true, id = savedId });
            }
            catch (Exception ex)
            {
                // Log and respond properly to database insertion failures
                _logger.LogError(ex, nameof(SaveService), nameof(TrackerEmployerController), "An error occurred while saving the service.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while saving the service." });
            }
        }

        /// <summary>
        /// Deletes a specific service record from an employer.
        /// </summary>
        /// <param name="id">The specific service identifier.</param>
        /// <returns>A JSON response verifying the deletion.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteService(int id)
        {
            try
            {
                // Ensure ID is valid before triggering delete
                if (id <= 0)
                {
                    return BadRequest(new { success = false, message = "A valid Service ID is required." });
                }

                // Process deletion in the repository layer
                await _repo.DeleteServiceAsync(id);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log and return a system error if deletion is blocked or fails
                _logger.LogError(ex, nameof(DeleteService), nameof(TrackerEmployerController), $"An error occurred while deleting the service (ID: {id}).");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while deleting the service." });
            }
        }

        /// <summary>
        /// Generates the employer-specific details dashboard, including all connected entity relationships.
        /// </summary>
        /// <param name="id">The employer ID to load into the dashboard.</param>
        /// <returns>The fully hydrated Employer Dashboard view.</returns>
        [HttpGet]
        public async Task<IActionResult> Dashboard(int id)
        {
            try
            {
                // If no ID is provided, route the user back to the primary index
                if (id == 0)
                {
                    return RedirectToAction("Index");
                }

                // Attempt to fetch the employer details; throw a 404 if it does not exist
                var employer = await _repo.GetByIdAsync(id);
                if (employer == null)
                {
                    return NotFound("The requested employer could not be found.");
                }

                // Retrieve all complex relational dropdown data via a single stored procedure request for efficiency
                var dd = await _repo.GetDropdownDataAsync(employer.EmployerId, employer.BrokerId, int.TryParse(employer.FirmId, out int fId) ? fId : (int?)null);






                // Populate ViewBags with relational configurations for the UI tabs
                ViewBag.Firms = new SelectList(dd.Firms, "Id", "Name", employer.FirmId);
                ViewBag.Industries = new SelectList(dd.Industries, "Id", "Name", employer.IndustryId);
                ViewBag.DataTypes = new SelectList(dd.DataTypes, "Id", "Name", employer.DataTypeId);
                ViewBag.States = new SelectList(dd.StatesACA.Select(x => new { x.Code, Display = $"{x.Code} - {x.State}" }), "Code", "Display");

                ViewBag.Contacts = new SelectList(dd.GeneralContacts, "Id", "Name", employer.ACAContactId);
                ViewBag.BillingContacts = new SelectList(dd.BillingContacts, "Id", "Name", employer.BillingContactId);
                ViewBag.Brokers = new SelectList(dd.Brokers, "Id", "Name", employer.BrokerId);
                ViewBag.BrokerContacts = new SelectList(dd.BrokerContacts, "Id", "Name", employer.BrokerContactId);

                ViewBag.BrokerPhone = dd.BrokerPhone;
                ViewBag.BrokerEmail = dd.BrokerEmail;

                ViewBag.AcctManagers = new SelectList(dd.AccountManagers, "Id", "Name", employer.AcctManagerId);
                ViewBag.SalesReps = new SelectList(dd.SalesReps, "Id", "Name", employer.SalesRepId);
                // Handle case where Data Analyst is stored as a name (string) instead of ID
                if (!employer.DataAnalystId.HasValue && !string.IsNullOrEmpty(employer.DataAnalyst))
                {
                    var da = dd.DataAnalysts.FirstOrDefault(x => x.Name.Equals(employer.DataAnalyst, StringComparison.OrdinalIgnoreCase));
                    if (da != null) employer.DataAnalystId = da.Id;
                }
                ViewBag.DataAnalysts = new SelectList(dd.DataAnalysts, "Id", "Name", employer.DataAnalystId);
                ViewBag.DataAnalystName = employer.DataAnalyst; // ← add this
                ViewBag.BandingTypes = new SelectList(new[] { "Monthly", "Age", "Salary", "Hourly", "Years of Service", "Monthly with Percent", "Multiple Bandings" }, employer.BandingType);
                ViewBag.FundingTypes = new SelectList(new[] { "Self-Funded", "Fully-Insured", "Mixed: Self-Funded & Fully-Insured" }, employer.FundingType);
                ViewBag.ComplexityLevels = new SelectList(new[] { "Low", "Standard", "Medium", "High", "Extreme" }, employer.LevelOfComplexity);
                ViewBag.Communications = new SelectList(new[] { "High", "Standard", "Low", "None" }, employer.Communication);

                ViewBag.PlanTerminations = new SelectList(new[] {
             "End of the month of termination", "Date of termination", "End of the month following termination",
             "15th of each month", "15th and 30th of each month", "Combination", "Other"
         }, employer.PlanTermination);

                ViewBag.DataLoadOptions = new SelectList(new[] { "Update", "Overwrite", "Changes Only", "N/A" }, employer.DataLoads);
                ViewBag.FormTypes = new SelectList(new[] { "1095-B", "1095-C", "Mixed:1095-B/1095-C" }, employer.FormType);
                ViewBag.DataFrequencies = new SelectList(new[] { "Weekly", "Bi-Weekly", "Monthly", "Quarterly", "Initial/Final File", "Other" }, employer.DataFrequency);

                // --- Additional ViewBags for Service Panel Edit Mode ---
                ViewBag.Penalties = new SelectList(new[] { "4980H(A)", "4980H(B)", "4980H(A) & 4980H(B)", " N / A" });
                ViewBag.SafeHarbors = new SelectList(new[] { "F - W2", "G - Federal Poverty Line", "H - Rate of Pay", "Combination of Safe Harbors" });
                ViewBag.ProcessSteps = new SelectList(new[] { "Received Sold Case", "Data Collection", "Data Validation", "Form Generation", "Mailed" });
                ViewBag.Payers = new SelectList(new[] { "Employer", "Firm", "Firm/Employer" });
                ViewBag.AuditUsers = new SelectList(new[]
                {
             "Michelle Barki", "Christy Dalton", "Michael Bracken", "Ivy Vinson",
             "Brittany Davis", "Marita Worthington", "Anna Saunders", "Crystal Dowell",
             "Fadi Safar", "Karam Safar", "Nichole Trahan", "Victoria Amhrein",
             "Maria Parks", "Charles Batton", "Other"
         });

                ViewBag.FTESteps = new SelectList(new[] { "Not Started", "Data Collection", "Data Validation", "Analysis", "Completed" });
                ViewBag.StateSteps = new SelectList(new[] { "Not Started", "Data Collection", "Data Validation", "Filing in Progress", "Completed" });

                ViewBag.BrokerInvolvements = new SelectList(new[] {
             new { Value = "High",   Text = "High - Include on all communication" },
             new { Value = "Medium", Text = "Medium - Include on implementation and follow-ups" },
             new { Value = "Low",    Text = "Low - Include on implementation and non-responsive requests" },
             new { Value = "None",   Text = "None - Do not include unless major issue" }
         }, "Value", "Text", employer.BrokerInvolvement);






                ViewBag.StatesACA = new SelectList(dd.StatesACA.Select(x => new { x.Code, Display = $"{x.Code} - {x.State}" }), "Code", "Display");
                ViewBag.CountriesACA = new SelectList(dd.CountriesACA, "Id", "Country");

                // Provide serialized complex objects for client-side JavaScript interaction
                ViewBag.ContactsJson = dd.GeneralContacts;
                ViewBag.BillingContactsJson = dd.BillingContacts;
                ViewBag.BrokerContactsJson = dd.BrokerContacts;
                // -- MODEL HYDRATION ------------------------------------------
                // Ensure view-mode labels are populated from dropdown data if missing
                if (string.IsNullOrEmpty(employer.FirmName) && !string.IsNullOrEmpty(employer.FirmId))
                {
                    if (int.TryParse(employer.FirmId, out int fId2))
                    {
                        employer.FirmName = dd.Firms.FirstOrDefault(x => x.Id == fId2)?.Name;
                    }
                }

                if (employer.ACAContactId.HasValue)
                {
                    var primary = dd.GeneralContacts.FirstOrDefault(x => x.Id == employer.ACAContactId);
                    if (primary != null)
                    {
                        if (string.IsNullOrEmpty(employer.ContactName)) employer.ContactName = primary.Name;
                        if (string.IsNullOrEmpty(employer.Phone)) employer.Phone = primary.Phone;
                        if (string.IsNullOrEmpty(employer.Email)) employer.Email = primary.Email;
                        if (string.IsNullOrEmpty(employer.ConnectUser)) employer.ConnectUser = primary.Connect_User;

                    }
                }

                if (employer.BillingContactId.HasValue)
                {
                    var billing = dd.BillingContacts.FirstOrDefault(x => x.Id == employer.BillingContactId);
                    if (billing != null)
                    {
                        if (string.IsNullOrEmpty(employer.BillingName)) employer.BillingName = billing.Name;
                        if (string.IsNullOrEmpty(employer.BillingPhone)) employer.BillingPhone = billing.Phone;
                        if (string.IsNullOrEmpty(employer.BillingEmail)) employer.BillingEmail = billing.Email;
                    }
                }

                // -- BROKER / CONTACT HYDRATION -------------------------------------------
                if (employer.BrokerContactId.HasValue)
                {
                    var bContact = dd.BrokerContacts.FirstOrDefault(x => x.Id == employer.BrokerContactId);
                    if (bContact != null)
                    {
                        employer.BrokerContactName = bContact.Name;
                        // Priority: Use individual contact details if available
                        if (!string.IsNullOrEmpty(bContact.Phone)) employer.BrokerPhone = bContact.Phone;
                        if (!string.IsNullOrEmpty(bContact.Email)) employer.BrokerEmail = bContact.Email;
                    }
                    else
                    {
                        // Fallback lookup by ID
                        var bc = await _repo.GetBrokerContactByIdAsync(employer.BrokerContactId.Value);
                        if (bc != null)
                        {
                            employer.BrokerContactName = bc.name;
                            if (!string.IsNullOrEmpty(bc.phone)) employer.BrokerPhone = bc.phone;
                            if (!string.IsNullOrEmpty(bc.email)) employer.BrokerEmail = bc.email;
                        }
                    }
                }

                if (employer.BrokerId.HasValue && string.IsNullOrEmpty(employer.BrokerName))
                {
                    employer.BrokerName = dd.Brokers.FirstOrDefault(x => x.Id == employer.BrokerId)?.Name;
                }

                if (employer.IndustryId.HasValue)
                {
                    employer.IndustryName = dd.Industries.FirstOrDefault(x => x.Id == employer.IndustryId)?.Name;
                }

                if (employer.DataTypeId.HasValue)
                {
                    employer.DataTypeName = dd.DataTypes.FirstOrDefault(x => x.Id == employer.DataTypeId)?.Name;
                }

                // If DataAnalyst string is a numeric ID, replace it with the name for display
                if (!string.IsNullOrEmpty(employer.DataAnalyst) && int.TryParse(employer.DataAnalyst, out int daId2))
                {
                    var daObj = dd.DataAnalysts.FirstOrDefault(x => x.Id == daId2);
                    if (daObj != null) employer.DataAnalyst = daObj.Name;
                }
                // -------------------------------------------------------------

                ViewBag.SessionFilingYear = GetCurrentFilingYear().ToString();

                //return PartialView("_Dashboard", employer);
                return View("Dashboard", employer);
            }
            catch (Exception ex)
            {
                // Wrap dashboard construction failures in a generic server response
                _logger.LogError(ex, nameof(Dashboard), nameof(TrackerEmployerController), $"An error occurred while loading the dashboard for Employer ID {id}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the dashboard.");
            }
        }

        /// <summary>
        /// Retrieves the cascaded list of brokers associated with a specific firm.
        /// </summary>
        /// <param name="firmId">The selected firm ID.</param>
        /// <returns>A JSON array of brokers.</returns>
        [HttpGet]
        public async Task<IActionResult> GetBrokersForFirm(int firmId)
        {
            try
            {
                // Utilize the generic dropdown service specifically tuned for the requested Firm
                var dd = await _repo.GetDropdownDataAsync(0, null, firmId);
                return Json(dd.Brokers);
            }
            catch (Exception ex)
            {
                // Handle and notify AJAX calls about failures resolving the cascading dropdown
                _logger.LogError(ex, nameof(GetBrokersForFirm), nameof(TrackerEmployerController), $"An error occurred while fetching brokers for Firm ID {firmId}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while loading brokers." });
            }
        }

        /// <summary>
        /// Retrieves the cascaded list of broker contacts based on the currently selected broker.
        /// </summary>
        /// <param name="employerId">The active employer ID.</param>
        /// <param name="brokerId">The target broker ID.</param>
        /// <param name="firmId">The optional firm scope.</param>
        /// <returns>A JSON array of broker contacts.</returns>
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetBrokerContacts(int employerId, int brokerId, int? firmId)
        {
            try
            {
                // Query contextual broker contact relationships
                var dd = await _repo.GetDropdownDataAsync(employerId, brokerId, firmId);
                return Json(dd.BrokerContacts);
            }
            catch (Exception ex)
            {
                // Send an error indication if the dependent query fails
                _logger.LogError(ex, nameof(GetBrokerContacts), nameof(TrackerEmployerController), $"An error occurred while fetching broker contacts for Broker ID {brokerId}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while loading broker contacts." });
            }
        }

        /// <summary>
        /// Fetches the complete list of affiliate employers linked to a parent employer.
        /// </summary>
        /// <param name="employerId">The master employer ID.</param>
        /// <returns>A JSON array of affiliate data records.</returns>
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetAffiliates(int employerId)
        {
            try
            {
                // Lookup and return all linked affiliates
                var list = await _repo.GetAffiliatesAsync(employerId);
                return Json(list);
            }
            catch (Exception ex)
            {
                // Capture failures during relational mapping lookups
                _logger.LogError(ex, nameof(GetAffiliates), nameof(TrackerEmployerController), $"An error occurred while fetching affiliates for Employer ID {employerId}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while loading affiliates." });
            }
        }

        /// <summary>
        /// Retrieves a single affiliate record to populate edit modals.
        /// </summary>
        /// <param name="id">The affiliate entity ID.</param>
        /// <returns>A JSON object representing the affiliate.</returns>
        [HttpGet]
        public async Task<IActionResult> GetAffiliateById(int id)
        {
            try
            {
                // Prevent querying invalid IDs
                if (id <= 0)
                {
                    return BadRequest(new { success = false, message = "A valid ID is required." });
                }

                var item = await _repo.GetAffiliateByIdAsync(id);
                if (item == null)
                {
                    return NotFound(new { success = false, message = "The requested affiliate could not be found." });
                }

                return Json(item);
            }
            catch (Exception ex)
            {
                // Handle missing or inaccessible database items
                _logger.LogError(ex, nameof(GetAffiliateById), nameof(TrackerEmployerController), $"An error occurred while fetching affiliate ID {id}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while retrieving the affiliate." });
            }
        }

        /// <summary>
        /// Adds a new or updates an existing affiliate employer entity.
        /// </summary>
        /// <param name="model">The affiliate details submitted by the client.</param>
        /// <returns>A JSON object indicating success or failure.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAffiliate(AffiliateEmployerItem model)
        {
            try
            {
                // Check business rule: an affiliate requires a valid name
                if (string.IsNullOrWhiteSpace(model.AffiliateName))
                {
                    return BadRequest(new { success = false, message = "Affiliate name is a required field." });
                }

                model.Filingyear = GetCurrentFilingYear();
                // Process save logic inside repository
                await _repo.SaveAffiliateAsync(model);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log issues preventing data saving
                _logger.LogError(ex, nameof(SaveAffiliate), nameof(TrackerEmployerController), "An error occurred while saving the affiliate.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while saving the affiliate." });
            }
        }

        /// <summary>
        /// Soft-deletes a specified affiliate record.
        /// Rejects attempts to delete the primary parent employer account.
        /// </summary>
        /// <param name="id">The ID of the affiliate to remove.</param>
        /// <returns>A JSON success marker or an error payload if deletion is forbidden.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAffiliate(int id)
        {
            try
            {
                // Ensure a valid affiliate target exists
                if (id <= 0)
                {
                    return BadRequest(new { success = false, message = "Invalid ID." });
                }

                // Execute removal, capturing response flag indicating whether the entity was valid for deletion
                var deleted = await _repo.DeleteAffiliateAsync(id);
                if (!deleted)
                {
                    // Respond with a 400 Bad Request to indicate an operation constraint
                    return BadRequest(new { success = false, message = "The primary employer record cannot be removed from here." });
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Prevent application crashes upon unexpected SQL failures or concurrent locking scenarios
                _logger.LogError(ex, nameof(DeleteAffiliate), nameof(TrackerEmployerController), $"An error occurred while deleting the affiliate (ID: {id}).");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while deleting the affiliate." });
            }
        }

        /// <summary>
        /// Retrieves the collection of historical notes recorded for a given employer.
        /// </summary>
        /// <param name="employerId">The identifier of the target employer.</param>
        /// <returns>A JSON array of note entities.</returns>
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetNotes(int employerId)
        {
            try
            {
                // Fetch the activity log notes linked to this account
                var notes = await _repo.GetNotesAsync(employerId);
                return Json(notes);
            }
            catch (Exception ex)
            {
                // Safely log and decline serving data if the repository encounters issues
                _logger.LogError(ex, nameof(GetNotes), nameof(TrackerEmployerController), $"An error occurred while fetching notes for Employer ID {employerId}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while loading notes." });
            }
        }

        /// <summary>
        /// Persists a new note log or updates an existing one for the employer.
        /// Attaches the acting user's tracking ID internally.
        /// </summary>
        /// <param name="model">The note metadata submitted via the form interface.</param>
        /// <returns>A JSON success flag if successfully processed.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveNote(EmployerNoteItem model)
        {
            try
            {
                // Evaluate input constraints
                if (model == null)
                {
                    return BadRequest(new { success = false, message = "Invalid note data." });
                }
                if (string.IsNullOrWhiteSpace(model.Category))
                {
                    return BadRequest(new { success = false, message = "Note Category is required." });
                }
                if (string.IsNullOrWhiteSpace(model.NoteText))
                {
                    return BadRequest(new { success = false, message = "Note Content is required." });
                }

                // Discover the identity of the user submitting the note for auditing
                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int currentUserId = int.TryParse(userIdClaim, out int uid) ? uid : 0;

                // Transmit the verified record to the database
                await _repo.SaveNoteAsync(model, currentUserId);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Log operational failures during text storage
                _logger.LogError(ex, nameof(SaveNote), nameof(TrackerEmployerController), "An error occurred while saving the note.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while saving the note." });
            }
        }

        /// <summary>
        /// Clears an existing note record permanently from the employer's history.
        /// </summary>
        /// <param name="id">The unique note identifier.</param>
        /// <param name="employerId">The context employer.</param>
        /// <returns>A confirmation of successful deletion.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope]
        public async Task<IActionResult> DeleteNote(int id, int employerId)
        {
            try
            {
                // Validate payload keys
                if (id <= 0 || employerId <= 0)
                {
                    return BadRequest(new { success = false, message = "Valid ID values are required." });
                }

                // Force physical deletion from tracking table
                await _repo.DeleteNoteAsync(id, employerId);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Handle any relational integrity constraints or SQL problems
                _logger.LogError(ex, nameof(DeleteNote), nameof(TrackerEmployerController), $"An error occurred while deleting the note (ID: {id}).");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while deleting the note." });
            }
        }

        /// <summary>
        /// Supplies raw JSON tracking data representing active services for client-side evaluation.
        /// </summary>
        /// <param name="employerId">The employer to scope.</param>
        /// <returns>A raw JSON array describing the assigned services.</returns>
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetServicesJson(int employerId)
        {
            try
            {
                string PlanYear = GetCurrentFilingYear().ToString();
                var data = await _repo.GetServicesAsync(employerId, PlanYear);
                return Json(data);
            }
            catch (Exception ex)
            {
                // Shield application code from raw serialization errors
                _logger.LogError(ex, nameof(GetServicesJson), nameof(TrackerEmployerController), $"An error occurred while fetching services JSON for Employer ID {employerId}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while loading services data." });
            }
        }
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetServicesGridJson(int employerId)
        {
            string PlanYear = GetCurrentFilingYear().ToString();
            var data = await _repo.GetServicesGridAsync(employerId, PlanYear);
            return Json(data);
        }

        [HttpGet]
        public async Task<IActionResult> GetServicePanel(int id, bool edit = false, bool isModal = false)
        {
            try
            {
                string PlanYear = GetCurrentFilingYear().ToString();
                var model = await _repo.GetServiceByIdAsync(id, PlanYear);
                if (model == null) return NotFound();

                await PopulateServicePanelViewBagAsync();
                ViewBag.EditMode = edit;
                ViewBag.IsModal = isModal;
                return PartialView("_ServicePanel", model);
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> GetProcessHistory(int employerServiceId, int categoryId)
        {
            if (employerServiceId <= 0 || categoryId <= 0) return Json(new List<object>());
            try
            {
                var history = await _repo.GetProcessHistoryAsync(employerServiceId, categoryId);
                return Json(history);
            }
            catch (Exception) { return StatusCode(500, "Failed to load history data."); }
        }

        private async Task PopulateServicePanelViewBagAsync()
        {
            ViewBag.Penalties = new SelectList(new[] { "4980H(A)", "4980H(B)", "4980H(A) & 4980H(B)", " N / A" });
            ViewBag.SafeHarbors = new SelectList(new[] { "F - W2", "G - Federal Poverty Line", "H - Rate of Pay", "Combination of Safe Harbors" });

            var process1095Step = await _employerRepo.GetProcessesAsync("1095");
            var fteProcessStep = await _employerRepo.GetProcessesAsync("FTE Process Step");
            var stateFilingProcessStep = await _employerRepo.GetProcessesAsync("State Filing Process Step");
            var auditUsers = await _employerRepo.GetAuditByDropdownAsync();

            ViewBag.ProcessSteps = new SelectList(process1095Step ?? new List<ACA360.Core.Models.Process_steps>(), "ProcessName", "ProcessName");
            ViewBag.FTESteps = new SelectList(fteProcessStep ?? new List<ACA360.Core.Models.Process_steps>(), "ProcessName", "ProcessName");
            ViewBag.StateSteps = new SelectList(stateFilingProcessStep ?? new List<ACA360.Core.Models.Process_steps>(), "ProcessName", "ProcessName");
            ViewBag.AuditUsers = new SelectList(auditUsers ?? new List<ACA360.Core.Models.AuditUser>(), "Name", "Name");

            ViewBag.Payers = new SelectList(new[] { "Employer", "Firm", "Firm/Employer" });
            ViewBag.Months = new SelectList(
                System.Globalization.DateTimeFormatInfo.CurrentInfo.MonthNames.Take(12).Select((m, i) => new SelectListItem { Value = (i + 1).ToString(), Text = m }),
                "Value", "Text"
            );
            ViewBag.Statuses = new SelectList(new[] { "Opportunity", "Sold", "Lost" });
        }

        [HttpPost]

        public async Task<IActionResult> SaveContact(TrackerContactDto dto)

        {

            try

            {

                var id = await _repo.SaveContactAsync(dto);

                return Json(new { success = true, id });

            }

            catch (Exception ex)

            {

                return Json(new { success = false, message = ex.Message });

            }

        }

        [HttpPost]

        public async Task<IActionResult> SaveBrokerContact(int id, int brokerId, string name, string phone, string email, string acaRoles, string acaUsername, string address, string address2, string city, string state, string zip)

        {

            try

            {

                dynamic obj = new System.Dynamic.ExpandoObject();

                obj.Id = id; obj.BrokerId = brokerId; obj.Name = name; obj.Phone = phone;

                obj.Email = email; obj.AcaRoles = acaRoles; obj.AcaUsername = acaUsername;

                obj.Address = address; obj.Address2 = address2; obj.City = city; obj.State = state; obj.Zip = zip;

                var resultId = await _repo.SaveBrokerContactAsync(obj); return Json(new { success = true, id = resultId });

            }

            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }

        }

        [HttpGet]
        public async Task<IActionResult> GetStatesJson()
        {
            try
            {
                var dd = await _repo.GetDropdownDataAsync(0);
                var states = dd.StatesACA.Select(x => new { value = x.Code, text = $"{x.Code} - {x.State}" });
                return Json(states);
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }
        [HttpPost]
        public async Task<IActionResult> SaveLookup(string type, string name)
        {
            try
            {
                var id = await _repo.SaveLookupAsync(type, name);
                return Json(new { success = true, id });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> GetContactById(int id)
        {
            try
            {
                var c = await _repo.GetContactByIdAsync(id);
                return Json(new
                {
                    contactId = c.Id,
                    name = c.Name,
                    phone = c.Phone,
                    email = c.Email,
                    acaUsername = c.AcaUsername,
                    acaRoles = c.AcaRoles,
                    address1 = c.Address1,
                    address2 = c.Address2,
                    city = c.City,
                    state = c.State,
                    zip = c.Zip,
                    isBilling = c.IsBilling
                });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }
        [HttpGet]
        public async Task<IActionResult> GetDashboardJson(int id)
        {
            try
            {
                var employer = await _repo.GetByIdAsync(id);
                var dd = await _repo.GetDropdownDataAsync(
                    employer.EmployerId,
                    employer.BrokerId,
                    int.TryParse(employer.FirmId, out int fId) ? fId : (int?)null);

                var acaContact = dd.GeneralContacts.FirstOrDefault(x => x.Id == employer.ACAContactId);
                var billingContact = dd.BillingContacts.FirstOrDefault(x => x.Id == employer.BillingContactId);
                bool canViewFullEIN = User.CanViewFullPii();
                return Json(new
                {
                    success = true,
                    employerName = employer.EmployerName ?? "—",
                    ein = (canViewFullEIN ? employer.EIN : DataMasker.MaskEIN(employer.EIN)) ?? "—",
                    planYear = employer.PlanYear ?? "—",
                    firmName = dd.Firms.FirstOrDefault(x => x.Id.ToString() == employer.FirmId)?.Name ?? "—",
                    address = employer.Address ?? "—",
                    address2 = employer.Address2 ?? "—",
                    city = employer.City ?? "—",
                    state = employer.State ?? "—",
                    zip = employer.Zip ?? "—",
                    phone = employer.Phone ?? "—",
                    email = employer.Email ?? "—",
                    contactName = acaContact?.Name ?? "—",
                    billingName = billingContact?.Name ?? "—",
                    billingPhone = billingContact?.Phone ?? "—",
                    billingEmail = billingContact?.Email ?? "—",
                    acaContactName = acaContact?.Name ?? "—",
                    acaRole = employer.ACARole ?? "—",
                    formType = employer.FormType ?? "—",
                    numberOf1095Cs = employer.NumberOf1095Cs?.ToString() ?? "—",
                    waitingPeriod = employer.WaitingPeriod ?? "—",
                    bandingType = employer.BandingType ?? "—",
                    fundingType = employer.FundingType ?? "—",
                    levelOfComplexity = employer.LevelOfComplexity ?? "—",
                    communication = employer.Communication ?? "—",
                    planTermination = employer.PlanTermination ?? "—",
                    dataLoads = employer.DataLoads ?? "—",
                    dataFrequency = employer.DataFrequency ?? "—",
                    vendor = employer.Vendor ?? "—",
                    acctManagerName = dd.AccountManagers.FirstOrDefault(x => x.Id == employer.AcctManagerId)?.Name ?? "—",
                    salesRepName = dd.SalesReps.FirstOrDefault(x => x.Id == employer.SalesRepId)?.Name ?? "—",
                    dataAnalyst = dd.DataAnalysts.FirstOrDefault(x => x.Id == employer.DataAnalystId)?.Name ?? employer.DataAnalyst ?? "—",
                    brokerName = dd.Brokers.FirstOrDefault(x => x.Id == employer.BrokerId)?.Name ?? "—",
                    brokerPhone = employer.BrokerPhone ?? "—",
                    brokerEmail = employer.BrokerEmail ?? "—",
                    // ── Checkbox flags ──
                    isPriority = employer.IsPriority == true,
                    conditionalOffer = employer.ConditionalOffer == true,
                    sFTP = employer.SFTP == true,
                    annualPO = employer.AnnualPO == true,
                    annualCOI = employer.AnnualCOI == true,
                    noAutoRenewal = employer.NoAutoRenewal == true,
                    hasSpecialMailing = employer.HasSpecialMailing == true,
                    specialPaymentDates = employer.SpecialPaymentDates == true

                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }




        [HttpPost]
        public async Task<IActionResult> DeleteContact(int id)
        {
            try { await _repo.DeleteContactAsync(id); return Json(new { success = true }); }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> GetBrokerContactById(int id)
        {
            try { return Json(await _repo.GetBrokerContactByIdAsync(id)); }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteBrokerContact(int id)
        {
            try { await _repo.DeleteBrokerContactAsync(id); return Json(new { success = true }); }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> GetFirmInfo(int id)
        {
            try { return Json(await _repo.GetFirmByIdAsync(id)); }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> GetBrokerInfo(int id)
        {
            try { return Json(await _repo.GetBrokerByIdAsync(id)); }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // --- Receipt IDs ---
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetReceiptIds(int employerServiceId, int employerId = 0)
        {
            try { return Json(await _repo.GetReceiptIdsAsync(employerServiceId, employerId)); }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> SaveReceiptId(ReceiptIdItem model)
        {
            try
            {
                string PlanYear = GetCurrentFilingYear().ToString();
                if (string.IsNullOrWhiteSpace(model.ReceiptIdValue)) return Json(new { success = false, message = "Receipt ID is required." });
                var svc = await _repo.GetServiceByIdAsync(model.EmployerServiceId, PlanYear);
                if (svc != null) { model.ServiceId = svc.ServiceId; model.PlanYear = svc.PlanYear.ToString(); }
                var idStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int uid = int.TryParse(idStr, out int u) ? u : 0;
                var res = await _repo.SaveReceiptIdAsync(model, uid);
                return Json(new { success = true, receiptEntryId = res });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteReceiptId(int id)
        {
            try { await _repo.DeleteReceiptIdAsync(id); return Json(new { success = true }); }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // --- Export ---
        [HttpGet]
        public async Task<IActionResult> ExportMasterExcel()
        {
            try
            {
                string year = GetCurrentFilingYear().ToString();
                var data = await _repo.GetMasterExportDataAsync(year);
                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("Master List");
                    string[] heads = { "Employer", "EIN", "Address", "City", "State", "Zip", "Contact", "Phone", "Email", "Firm", "Funding", "Form", "Year", "Complexity", "AM", "Analyst", "Sales", "Service", "Status", "Svc Year", "Receipt ID", "Notes" };
                    for (int i = 0; i < heads.Length; i++) { ws.Cells[1, i + 1].Value = heads[i]; ws.Cells[1, i + 1].Style.Font.Bold = true; ws.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid; ws.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(105, 108, 255)); ws.Cells[1, i + 1].Style.Font.Color.SetColor(System.Drawing.Color.White); }
                    int row = 2;
                    foreach (var d in data) { ws.Cells[row, 1].Value = d.EmployerName; ws.Cells[row, 2].Value = d.EIN; ws.Cells[row, 3].Value = d.Address; ws.Cells[row, 4].Value = d.City; ws.Cells[row, 5].Value = d.State; ws.Cells[row, 6].Value = d.Zip; ws.Cells[row, 7].Value = d.ContactName; ws.Cells[row, 8].Value = d.Phone; ws.Cells[row, 9].Value = d.Email; ws.Cells[row, 10].Value = d.FirmName; ws.Cells[row, 11].Value = d.FundingType; ws.Cells[row, 12].Value = d.FormType; ws.Cells[row, 13].Value = d.PlanYear; ws.Cells[row, 14].Value = d.LevelOfComplexity; ws.Cells[row, 15].Value = d.AccountManager; ws.Cells[row, 16].Value = d.DataAnalyst; ws.Cells[row, 17].Value = d.SalesRep; ws.Cells[row, 18].Value = d.ServiceName; ws.Cells[row, 19].Value = d.ServiceStatus; ws.Cells[row, 20].Value = d.ServicePlanYear; ws.Cells[row, 21].Value = d.ReceiptID; ws.Cells[row, 22].Value = d.ServiceNotes; row++; }
                    ws.Cells.AutoFitColumns();
                    var ms = new MemoryStream(); pkg.SaveAs(ms); ms.Position = 0;
                    // Set cookie to signal JS that download is starting
                    Response.Cookies.Append("fileDownload", "true", new CookieOptions { Path = "/", HttpOnly = false });
                    return File(ms, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Master_Export_{year}_{DateTime.Now:yyyyMMdd}.xlsx");
                }
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpGet]
        public async Task<IActionResult> ExportEmployerExcel(int id)
        {
            try
            {
                if (id == 0) return BadRequest("Invalid Employer ID");

                // Fetch all data
                string year = GetCurrentFilingYear().ToString();
                var emp = await _repo.GetByIdAsync(id);
                if (emp == null) return NotFound();

                var svcs = await _repo.GetServicesGridAsync(id, year); // Use grid version for complete data
                var affs = await _repo.GetAffiliatesAsync(id);
                var nts = await _repo.GetNotesAsync(id);
                var dd = await _repo.GetDropdownDataAsync(id, emp.BrokerId, int.TryParse(emp.FirmId, out int fId) ? fId : (int?)null);

                using (var pkg = new ExcelPackage())
                {
                    // ---------------------------------------------------------------
                    // SHEET 1: COVER PAGE & SUMMARY
                    // ---------------------------------------------------------------
                    // ? CORRECT - includes dd parameter
                    BuildCoverSheet(pkg, emp, svcs, affs, nts, dd);

                    // ---------------------------------------------------------------
                    // SHEET 2: EMPLOYER PROFILE (DETAILED)
                    // ---------------------------------------------------------------
                    BuildEmployerProfileSheet(pkg, emp, dd);

                    // ---------------------------------------------------------------
                    // SHEET 3: SERVICES (COMPREHENSIVE)
                    // ---------------------------------------------------------------
                    if (svcs.Any())
                    {
                        BuildServicesSheet(pkg, svcs);
                    }

                    // ---------------------------------------------------------------
                    // SHEET 4: AFFILIATES
                    // ---------------------------------------------------------------
                    if (affs.Any())
                    {
                        BuildAffiliatesSheet(pkg, affs);
                    }

                    // ---------------------------------------------------------------
                    // SHEET 5: NOTES
                    // ---------------------------------------------------------------
                    if (nts.Any())
                    {
                        BuildNotesSheet(pkg, nts);
                    }

                    // Save and return
                    // ? FIXED CODE (Return byte array instead)
                    var fileName = $"Dashboard_{emp.EmployerName?.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    // Signal JS to hide loader
                    Response.Cookies.Append("fileDownload", "true", new CookieOptions { Path = "/", HttpOnly = false });
                    var bytes = pkg.GetAsByteArray();

                    return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
            catch (Exception ex)
            {
                // Log the error
                return StatusCode(500, new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        // -----------------------------------------------------------------------------------
        // SHEET BUILDER METHODS
        // -----------------------------------------------------------------------------------

        private void BuildCoverSheet(ExcelPackage pkg, TrackerEmployerItem emp,
    IEnumerable<EmployerServiceItem> svcs,
    IEnumerable<AffiliateEmployerItem> affs,
    IEnumerable<EmployerNoteItem> nts,
    EmployerDropdownDataModel dd) // FIXED: Added dd parameter
        {
            var ws = pkg.Workbook.Worksheets.Add("Dashboard Summary");

            // --- TITLE SECTION ---
            ws.Cells["A1:F1"].Merge = true;
            ws.Cells["A1"].Value = "EMPLOYER DASHBOARD REPORT";
            ws.Cells["A1"].Style.Font.Size = 24;
            ws.Cells["A1"].Style.Font.Bold = true;
            ws.Cells["A1"].Style.Font.Color.SetColor(Color.White);
            ws.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(68, 84, 106));
            ws.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            ws.Cells["A1"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            ws.Row(1).Height = 40;

            // --- EMPLOYER INFO ---
            int row = 3;
            ws.Cells[row, 1].Value = emp.EmployerName;
            ws.Cells[row, 1].Style.Font.Size = 18;
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Font.Color.SetColor(Color.FromArgb(68, 84, 106));
            row += 2;

            ws.Cells[row, 1].Value = "Generated:";
            ws.Cells[row, 2].Value = DateTime.Now.ToString("MMMM dd, yyyy 'at' h:mm tt");
            ws.Cells[row, 1].Style.Font.Bold = true;
            row++;

            ws.Cells[row, 1].Value = "Filing Year:";
            ws.Cells[row, 2].Value = emp.Filingyear;
            ws.Cells[row, 1].Style.Font.Bold = true;
            row += 2;

            // --- STATISTICS SECTION ---
            ws.Cells[row, 1, row, 6].Merge = true;
            ws.Cells[row, 1].Value = "QUICK STATISTICS";
            ws.Cells[row, 1].Style.Font.Size = 14;
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(220, 230, 241));
            ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            row += 2;

            // Statistics cards
            var stats = new[]
            {
        new { Label = "Total Services", Value = svcs.Count().ToString(), Color = Color.FromArgb(39, 174, 96) },
        new { Label = "Active Services", Value = svcs.Count(s => s.Status == "Active" || s.Status == "Sold").ToString(), Color = Color.FromArgb(41, 128, 185) },
        new { Label = "Affiliates", Value = affs.Count().ToString(), Color = Color.FromArgb(155, 89, 182) },
        new { Label = "Notes", Value = nts.Count().ToString(), Color = Color.FromArgb(230, 126, 34) }
    };

            int col = 1;
            foreach (var stat in stats)
            {
                ws.Cells[row, col].Value = stat.Label;
                ws.Cells[row, col].Style.Font.Size = 10;
                ws.Cells[row, col].Style.Font.Color.SetColor(Color.Gray);
                ws.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                ws.Cells[row + 1, col].Value = stat.Value;
                ws.Cells[row + 1, col].Style.Font.Size = 24;
                ws.Cells[row + 1, col].Style.Font.Bold = true;
                ws.Cells[row + 1, col].Style.Font.Color.SetColor(stat.Color);
                ws.Cells[row + 1, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                col += 2;
            }

            row += 4;

            // --- KEY CONTACTS ---
            ws.Cells[row, 1, row, 6].Merge = true;
            ws.Cells[row, 1].Value = "KEY CONTACTS";
            ws.Cells[row, 1].Style.Font.Size = 14;
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(220, 230, 241));
            row += 2;

            // FIXED: Safely get dropdown text with null checks
            var amName = emp.AcctManagerId.HasValue
                ? dd.AccountManagers?.FirstOrDefault(x => x.Id == emp.AcctManagerId.Value)?.Name
                : null;
            var daName = emp.DataAnalystId.HasValue
                ? dd.DataAnalysts?.FirstOrDefault(x => x.Id == emp.DataAnalystId.Value)?.Name
                : null;
            var brokerName = emp.BrokerId.HasValue
                ? dd.Brokers?.FirstOrDefault(x => x.Id == emp.BrokerId.Value)?.Name
                : null;

            var contacts = new[]
            {
        ("Primary Contact", emp.ContactName, emp.Phone, emp.Email),
        ("Account Manager", amName, "", ""),
        ("Data Analyst", daName, "", ""),
        ("Broker", brokerName, emp.BrokerPhone, emp.BrokerEmail)
    };

            foreach (var contact in contacts)
            {
                ws.Cells[row, 1].Value = contact.Item1;
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 2].Value = contact.Item2 ?? "�";
                ws.Cells[row, 3].Value = contact.Item3 ?? "�";
                ws.Cells[row, 4].Value = contact.Item4 ?? "�";
                row++;
            }

            // Column widths
            ws.Column(1).Width = 20;
            ws.Column(2).Width = 25;
            ws.Column(3).Width = 20;
            ws.Column(4).Width = 30;
        }

        private void BuildEmployerProfileSheet(ExcelPackage pkg, TrackerEmployerItem emp, EmployerDropdownDataModel dd)
        {
            var ws = pkg.Workbook.Worksheets.Add("Employer Profile");

            int row = 1;

            // --- TITLE ---
            AddSheetHeader(ws, ref row, "EMPLOYER PROFILE", Color.FromArgb(68, 84, 106));
            row++;

            // --- SECTION: IDENTITY ---
            AddSectionHeader(ws, ref row, "PRIMARY IDENTITY", Color.FromArgb(52, 73, 94));
            AddDetailRow(ws, ref row, "Legal Employer Name", emp.EmployerName);
            AddDetailRow(ws, ref row, "EIN", emp.EIN);
            AddDetailRow(ws, ref row, "Plan Year", emp.PlanYear);
            AddDetailRow(ws, ref row, "Filing Year", emp.Filingyear);

            // FIXED: Safe dropdown lookup with null checks
            var firmName = !string.IsNullOrEmpty(emp.FirmId) && int.TryParse(emp.FirmId, out int fId3)
                ? dd.Firms?.FirstOrDefault(x => x.Id == fId3)?.Name
                : null;
            AddDetailRow(ws, ref row, "Parent Firm", firmName);
            row++;

            // --- SECTION: LOCATION ---
            AddSectionHeader(ws, ref row, "LOCATION", Color.FromArgb(52, 73, 94));
            AddDetailRow(ws, ref row, "Street Address", emp.Address);
            AddDetailRow(ws, ref row, "City", emp.City);
            AddDetailRow(ws, ref row, "State", emp.State);
            AddDetailRow(ws, ref row, "Zip Code", emp.Zip);
            row++;

            // --- SECTION: PRIMARY CONTACT ---
            AddSectionHeader(ws, ref row, "PRIMARY CONTACT", Color.FromArgb(52, 73, 94));
            AddDetailRow(ws, ref row, "Contact Name", emp.ContactName);
            AddDetailRow(ws, ref row, "Phone", emp.Phone);
            AddDetailRow(ws, ref row, "Email", emp.Email);
            row++;

            // --- SECTION: BROKER ---
            AddSectionHeader(ws, ref row, "BROKER INFORMATION", Color.FromArgb(52, 73, 94));

            var brokerName = emp.BrokerId.HasValue
                ? dd.Brokers?.FirstOrDefault(x => x.Id == emp.BrokerId.Value)?.Name
                : null;
            var brokerContactName = emp.BrokerContactId.HasValue
                ? dd.BrokerContacts?.FirstOrDefault(x => x.Id == emp.BrokerContactId.Value)?.Name
                : null;

            AddDetailRow(ws, ref row, "Broker", brokerName);
            AddDetailRow(ws, ref row, "Broker Contact", brokerContactName);
            AddDetailRow(ws, ref row, "Broker Phone", emp.BrokerPhone);
            AddDetailRow(ws, ref row, "Broker Email", emp.BrokerEmail);
            AddDetailRow(ws, ref row, "Broker Involvement", emp.BrokerInvolvement);
            AddDetailRow(ws, ref row, "Last Contact Date", emp.BrokerLastContactDate);
            row++;

            // --- SECTION: TEAM ---
            AddSectionHeader(ws, ref row, "TEAM ASSIGNMENTS", Color.FromArgb(52, 73, 94));

            var amName = emp.AcctManagerId.HasValue
                ? dd.AccountManagers?.FirstOrDefault(x => x.Id == emp.AcctManagerId.Value)?.Name
                : null;
            var srName = emp.SalesRepId.HasValue
                ? dd.SalesReps?.FirstOrDefault(x => x.Id == emp.SalesRepId.Value)?.Name
                : null;
            var daName = emp.DataAnalystId.HasValue
                ? dd.DataAnalysts?.FirstOrDefault(x => x.Id == emp.DataAnalystId.Value)?.Name
                : null;
            var industryName = emp.IndustryId.HasValue
                ? dd.Industries?.FirstOrDefault(x => x.Id == emp.IndustryId.Value)?.Name
                : null;
            var dataTypeName = emp.DataTypeId.HasValue
                ? dd.DataTypes?.FirstOrDefault(x => x.Id == emp.DataTypeId.Value)?.Name
                : null;

            AddDetailRow(ws, ref row, "Account Manager", amName);
            AddDetailRow(ws, ref row, "Sales Representative", srName);
            AddDetailRow(ws, ref row, "Data Analyst", daName);
            AddDetailRow(ws, ref row, "Industry", industryName);
            AddDetailRow(ws, ref row, "Data Type", dataTypeName);
            AddDetailRow(ws, ref row, "Vendor", emp.Vendor);
            row++;

            // --- SECTION: CONFIGURATION ---
            AddSectionHeader(ws, ref row, "CONFIGURATION & SETTINGS", Color.FromArgb(52, 73, 94));
            AddDetailRow(ws, ref row, "Funding Type", emp.FundingType);
            AddDetailRow(ws, ref row, "Form Type", emp.FormType);
            AddDetailRow(ws, ref row, "Data Frequency", emp.DataFrequency);
            AddDetailRow(ws, ref row, "Banding Type", emp.BandingType);
            AddDetailRow(ws, ref row, "Waiting Period", emp.WaitingPeriod);
            AddDetailRow(ws, ref row, "Level of Complexity", emp.LevelOfComplexity);
            AddDetailRow(ws, ref row, "Communication", emp.Communication);
            AddDetailRow(ws, ref row, "Plan Termination", emp.PlanTermination);
            AddDetailRow(ws, ref row, "Data Loads", emp.DataLoads);
            AddDetailRow(ws, ref row, "Connect User", emp.ConnectUser);
            row++;

            // --- SECTION: FLAGS ---
            AddSectionHeader(ws, ref row, "FLAGS & OPTIONS", Color.FromArgb(52, 73, 94));
            AddDetailRow(ws, ref row, "Priority Employer", emp.IsPriority ? "Yes" : "No");
            AddDetailRow(ws, ref row, "Special Mailing", emp.HasSpecialMailing ? "Yes" : "No");
            AddDetailRow(ws, ref row, "No Auto Renewal", emp.NoAutoRenewal ? "Yes" : "No");
            AddDetailRow(ws, ref row, "SFTP Enabled", emp.SFTP ? "Yes" : "No");
            AddDetailRow(ws, ref row, "Annual COI", emp.AnnualCOI ? "Yes" : "No");
            AddDetailRow(ws, ref row, "Annual PO", emp.AnnualPO ? "Yes" : "No");
            AddDetailRow(ws, ref row, "Conditional Offer", emp.ConditionalOffer ? "Yes" : "No");
            AddDetailRow(ws, ref row, "Special Payment Dates", emp.SpecialPaymentDates ? "Yes" : "No");

            // Format columns
            ws.Column(1).Width = 28;
            ws.Column(2).Width = 45;

            // Add borders to all cells
            var range = ws.Cells[1, 1, row - 1, 2];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        }

        // -----------------------------------------------------------------------------------
        // COMPREHENSIVE VERSION - ALL SERVICE FIELDS
        // Replace BuildServicesSheet method with this version
        // -----------------------------------------------------------------------------------

        private void BuildServicesSheet(ExcelPackage pkg, IEnumerable<EmployerServiceItem> svcs)
        {
            var ws = pkg.Workbook.Worksheets.Add("Services");

            int row = 1;

            // --- TITLE ---
            ws.Cells[row, 1, row, 25].Merge = true;
            ws.Cells[row, 1].Value = "SERVICES - COMPREHENSIVE TRACKING";
            ws.Cells[row, 1].Style.Font.Size = 16;
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Font.Color.SetColor(Color.White);
            ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(68, 84, 106));
            ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            ws.Row(row).Height = 30;
            row += 2;

            // --- HEADERS ---
            var headers = new[]
            {
        ("Service Name", 25),
        ("Status", 12),
        ("Plan Year", 10),
        
        // Financial
        ("Pricing", 12),
        ("Initial Bill", 12),
        ("Final Bill", 12),
        ("Payer", 12),
        ("SOS Amount", 12),
        
        // Compliance
        ("Potential Penalty", 16),
        ("Safe Harbor", 20),
        ("Required 1/31", 12),
        ("Extension Filed", 12),
        
        // Contract Dates
        ("Proposal Sent", 14),
        ("Proposal Signed", 14),
        ("Contract Sent", 14),
        ("Contract Signed", 14),
        
        // 1095-C Processing
        ("1095-C Step", 18),
        ("Last Update (1095)", 16),
        ("Follow-up (1095)", 16),
        
        // FTE Tracking
        ("FTE Step", 18),
        ("Last Update (FTE)", 16),
        ("Follow-up (FTE)", 16),
        ("Employees Tracked", 16),
        
        // State Filing
        ("State Filing Step", 18),
        ("Last Update (State)", 16),
        ("Follow-up (State)", 16),
        
        // Additional
        ("Initial Data Month", 15),
        ("Renewal Date", 14),
        ("Audit By", 18),
        ("Audit Date", 14),
        ("Forms Mailed", 12),
        ("First 50% Invoice", 14),
        ("Final 50% Invoice", 14),
        
        // Tracking
        ("Receipt ID", 20),
        ("Affiliate", 25),
        ("Notes", 40)
    };

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cells[row, i + 1].Value = headers[i].Item1;
                ws.Cells[row, i + 1].Style.Font.Bold = true;
                ws.Cells[row, i + 1].Style.Font.Color.SetColor(Color.White);
                ws.Cells[row, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                ws.Cells[row, i + 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(105, 108, 255));
                ws.Cells[row, i + 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                ws.Cells[row, i + 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                ws.Cells[row, i + 1].Style.WrapText = true;
                ws.Column(i + 1).Width = headers[i].Item2;
            }
            ws.Row(row).Height = 40;
            row++;

            // --- DATA ROWS ---
            int startDataRow = row;
            foreach (var svc in svcs)
            {
                int col = 1;

                // Basic Info
                ws.Cells[row, col++].Value = svc.ServiceName;
                ws.Cells[row, col++].Value = svc.Status;
                ws.Cells[row, col++].Value = svc.PlanYear;

                // Financial
                ws.Cells[row, col++].Value = svc.Pricing;
                ws.Cells[row, col++].Value = svc.InitialBill;
                ws.Cells[row, col++].Value = svc.FinalBill;
                ws.Cells[row, col++].Value = svc.Payer;
                ws.Cells[row, col++].Value = svc.SOSAmount;

                // Compliance
                ws.Cells[row, col++].Value = svc.PotentialPenalty;
                ws.Cells[row, col++].Value = svc.SafeHarbor;
                ws.Cells[row, col++].Value = svc.Required131 ? "Yes" : "No";
                ws.Cells[row, col++].Value = svc.ExtensionFiled;

                // Contract Dates
                ws.Cells[row, col++].Value = svc.ProposalSentDate;
                ws.Cells[row, col++].Value = svc.ProposalSignedDate;
                ws.Cells[row, col++].Value = svc.ContractSentDate;
                ws.Cells[row, col++].Value = svc.ContractSignedDate;

                // 1095-C Processing
                ws.Cells[row, col++].Value = svc.ProcessStep1095;
                ws.Cells[row, col++].Value = svc.LastUpdated1095;
                ws.Cells[row, col++].Value = svc.NextFollowUp1095;

                // FTE Tracking
                ws.Cells[row, col++].Value = svc.FTETrackingStep;
                ws.Cells[row, col++].Value = svc.LastUpdatedFTE;
                ws.Cells[row, col++].Value = svc.NextFollowUpFTE;
                ws.Cells[row, col++].Value = svc.ActualEmployeesTracked;

                // State Filing
                ws.Cells[row, col++].Value = svc.StateFilingStep;
                ws.Cells[row, col++].Value = svc.LastUpdatedState;
                ws.Cells[row, col++].Value = svc.NextFollowUpState;

                // Additional
                ws.Cells[row, col++].Value = svc.MonthInitialData;
                ws.Cells[row, col++].Value = svc.RenewalDate;
                ws.Cells[row, col++].Value = svc.AuditBy;
                ws.Cells[row, col++].Value = svc.AuditDate;
                ws.Cells[row, col++].Value = svc.FormsMailed;
                ws.Cells[row, col++].Value = svc.FirstInvoiceDate;
                ws.Cells[row, col++].Value = svc.FinalInvoiceDate;

                // Tracking
                ws.Cells[row, col++].Value = svc.ReceiptID;
                ws.Cells[row, col++].Value = svc.AffiliateName;
                ws.Cells[row, col++].Value = svc.Notes;

                // Alternating row colors
                if (row % 2 == 0)
                {
                    ws.Cells[row, 1, row, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[row, 1, row, headers.Length].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                }

                row++;
            }

            // --- FORMAT CURRENCY COLUMNS ---
            // Pricing, Initial Bill, Final Bill, SOS Amount
            var currencyCols = new[] { 4, 5, 6, 8 };
            foreach (var currCol in currencyCols)
            {
                ws.Cells[startDataRow, currCol, row - 1, currCol].Style.Numberformat.Format = "$#,##0.00";
            }

            // First 50% Invoice, Final 50% Invoice (columns 32, 33)
            ws.Cells[startDataRow, 32, row - 1, 32].Style.Numberformat.Format = "$#,##0.00";
            ws.Cells[startDataRow, 33, row - 1, 33].Style.Numberformat.Format = "$#,##0.00";

            // --- FORMAT DATE COLUMNS ---
            var dateCols = new[] {
        13, 14, 15, 16,  // Contract dates
        18, 19,          // 1095-C dates
        21, 22,          // FTE dates
        24, 25,          // State filing dates
        27, 28,          // Renewal, Audit dates
        30, 31           // Invoice dates
    };
            foreach (var dateCol in dateCols)
            {
                ws.Cells[startDataRow, dateCol, row - 1, dateCol].Style.Numberformat.Format = "mm/dd/yyyy";
            }

            // --- ADD BORDERS ---
            var dataRange = ws.Cells[startDataRow - 1, 1, row - 1, headers.Length];
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            // --- FREEZE PANES ---
            // Freeze header row and first 2 columns (Service Name, Status)
            ws.View.FreezePanes(startDataRow, 3);

            // --- AUTO-FILTER ---
            ws.Cells[startDataRow - 1, 1, row - 1, headers.Length].AutoFilter = true;

            // --- CONDITIONAL FORMATTING: Highlight Overdue Follow-ups ---
            // Follow-up (1095) - column 19
            var followUp1095 = ws.Cells[startDataRow, 19, row - 1, 19];
            var rule1 = followUp1095.ConditionalFormatting.AddExpression();
            rule1.Formula = $"AND(NOT(ISBLANK({followUp1095.Start.Address})), {followUp1095.Start.Address} < TODAY())";
            rule1.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rule1.Style.Fill.BackgroundColor.Color = Color.FromArgb(255, 200, 200);
            rule1.Style.Font.Color.Color = Color.DarkRed;
            rule1.Style.Font.Bold = true;

            // Follow-up (FTE) - column 22
            var followUpFTE = ws.Cells[startDataRow, 22, row - 1, 22];
            var rule2 = followUpFTE.ConditionalFormatting.AddExpression();
            rule2.Formula = $"AND(NOT(ISBLANK({followUpFTE.Start.Address})), {followUpFTE.Start.Address} < TODAY())";
            rule2.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rule2.Style.Fill.BackgroundColor.Color = Color.FromArgb(255, 200, 200);
            rule2.Style.Font.Color.Color = Color.DarkRed;
            rule2.Style.Font.Bold = true;

            // Follow-up (State) - column 25
            var followUpState = ws.Cells[startDataRow, 25, row - 1, 25];
            var rule3 = followUpState.ConditionalFormatting.AddExpression();
            rule3.Formula = $"AND(NOT(ISBLANK({followUpState.Start.Address})), {followUpState.Start.Address} < TODAY())";
            rule3.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rule3.Style.Fill.BackgroundColor.Color = Color.FromArgb(255, 200, 200);
            rule3.Style.Font.Color.Color = Color.DarkRed;
            rule3.Style.Font.Bold = true;
        }

        private void BuildAffiliatesSheet(ExcelPackage pkg, IEnumerable<AffiliateEmployerItem> affs)
        {
            var ws = pkg.Workbook.Worksheets.Add("Affiliates");

            int row = 1;

            // --- TITLE ---
            AddSheetHeader(ws, ref row, "AFFILIATE EMPLOYERS", Color.FromArgb(68, 84, 106));
            row++;

            // --- HEADERS ---
            var headers = new[]
            {
        ("Affiliate Name", 30),
        ("EIN", 15),
        ("Address", 35),
        ("City", 20),
        ("State", 8),
        ("Zip", 10)
    };

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cells[row, i + 1].Value = headers[i].Item1;
                ws.Cells[row, i + 1].Style.Font.Bold = true;
                ws.Cells[row, i + 1].Style.Font.Color.SetColor(Color.White);
                ws.Cells[row, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                ws.Cells[row, i + 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(155, 89, 182));
                ws.Cells[row, i + 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                ws.Column(i + 1).Width = headers[i].Item2;
            }
            row++;

            // --- DATA ---
            int startDataRow = row;
            foreach (var aff in affs)
            {
                ws.Cells[row, 1].Value = aff.AffiliateName;
                ws.Cells[row, 2].Value = aff.EIN;
                ws.Cells[row, 3].Value = aff.Address;
                ws.Cells[row, 4].Value = aff.City;
                ws.Cells[row, 5].Value = aff.State;
                ws.Cells[row, 6].Value = aff.Zip;

                // Alternating colors
                if (row % 2 == 0)
                {
                    ws.Cells[row, 1, row, 6].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[row, 1, row, 6].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                }

                row++;
            }

            // Borders
            var dataRange = ws.Cells[startDataRow - 1, 1, row - 1, 6];
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            // Freeze header
            ws.View.FreezePanes(startDataRow, 1);

            // Auto-filter
            ws.Cells[startDataRow - 1, 1, row - 1, 6].AutoFilter = true;
        }

        private void BuildNotesSheet(ExcelPackage pkg, IEnumerable<EmployerNoteItem> nts)
        {
            var ws = pkg.Workbook.Worksheets.Add("Notes");

            int row = 1;

            // --- TITLE ---
            AddSheetHeader(ws, ref row, "NOTES & ACTIVITY LOG", Color.FromArgb(68, 84, 106));
            row++;

            // --- HEADERS ---
            var headers = new[]
            {
        ("Date", 18),
        ("Category", 15),
        ("Note", 60),
        ("Created By", 20)
    };

            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cells[row, i + 1].Value = headers[i].Item1;
                ws.Cells[row, i + 1].Style.Font.Bold = true;
                ws.Cells[row, i + 1].Style.Font.Color.SetColor(Color.White);
                ws.Cells[row, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                ws.Cells[row, i + 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(230, 126, 34));
                ws.Cells[row, i + 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                ws.Column(i + 1).Width = headers[i].Item2;
            }
            row++;

            // --- DATA ---
            int startDataRow = row;
            foreach (var note in nts.OrderByDescending(n => n.CreatedDate))
            {
                ws.Cells[row, 1].Value = note.CreatedDate;
                ws.Cells[row, 1].Style.Numberformat.Format = "mm/dd/yyyy hh:mm AM/PM";

                ws.Cells[row, 2].Value = note.Category;

                ws.Cells[row, 3].Value = note.NoteText;
                ws.Cells[row, 3].Style.WrapText = true;

                ws.Cells[row, 4].Value = note.CreatedByName;

                // Alternating colors
                if (row % 2 == 0)
                {
                    ws.Cells[row, 1, row, 4].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[row, 1, row, 4].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(242, 242, 242));
                }

                row++;
            }

            // Borders
            var dataRange = ws.Cells[startDataRow - 1, 1, row - 1, 4];
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            // Freeze header
            ws.View.FreezePanes(startDataRow, 1);

            // Auto-filter
            ws.Cells[startDataRow - 1, 1, row - 1, 4].AutoFilter = true;
        }

        // -----------------------------------------------------------------------------------
        // HELPER METHODS FOR FORMATTING
        // -----------------------------------------------------------------------------------

        private void AddSheetHeader(ExcelWorksheet ws, ref int row, string title, Color bgColor)
        {
            ws.Cells[row, 1, row, 6].Merge = true;
            ws.Cells[row, 1].Value = title;
            ws.Cells[row, 1].Style.Font.Size = 16;
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Font.Color.SetColor(Color.White);
            ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(bgColor);
            ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            ws.Cells[row, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            ws.Row(row).Height = 30;
            row++;
        }

        private void AddSectionHeader(ExcelWorksheet ws, ref int row, string title, Color bgColor)
        {
            ws.Cells[row, 1, row, 2].Merge = true;
            ws.Cells[row, 1].Value = title;
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Font.Size = 12;
            ws.Cells[row, 1].Style.Font.Color.SetColor(Color.White);
            ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(bgColor);
            ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Row(row).Height = 25;
            row++;
        }

        private void AddDetailRow(ExcelWorksheet ws, ref int row, string label, string value)
        {
            ws.Cells[row, 1].Value = label;
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(236, 240, 241));

            ws.Cells[row, 2].Value = value ?? "�";
            row++;
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployerDashboardData(int id, int year)
        {
            try
            {
                // Validate input data parameters safely
                if (id <= 0 || year <= 0)
                {
                    return Json(new { success = false, message = "Invalid Employer ID or Filing Year provided." });
                }

                // Resolve the current user's id for the per-user Messages count
                int.TryParse(GetCurrentUserId(), out int currentUserId);

                // Call the service layer method deployed in Step 3
                //var dashboardData = await _dashboardService.GetEmployerDashboardParityAsync(id, year, currentUserId);
                var dashboardData = await _dashboardService.GetEmployerDashboardParityAsync(id, year, GetEffectiveScopeUserId());
                // Return a clean serialized JSON result payload for the frontend to ingest
                return Json(new { success = true, data = dashboardData });
            }
            catch (Exception ex)
            {
                // Safeguard against framework crashes by catching internal exceptions cleanly
                return Json(new { success = false, message = "An error occurred retrieving dashboard data: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDashboardLayout(string order, string hidden, string dashboardKey = "employer")
        {
            try
            {
                int.TryParse(GetCurrentUserId(), out int currentUserId);
                if (currentUserId <= 0)
                    return Json(new { success = false, message = "Unable to resolve the current user." });

                // Only known dashboards may be written, so a crafted key cannot
                // create unbounded rows.
                var allowedKeys = new[] { "employer", "broker", "admin", "am", "da" };
                if (!allowedKeys.Contains(dashboardKey))
                    return Json(new { success = false, message = "Unknown dashboard." });

                var layout = new DashboardLayoutDto
                {
                    Order = string.IsNullOrWhiteSpace(order)
                        ? new List<string>()
                        : System.Text.Json.JsonSerializer.Deserialize<List<string>>(order),
                    Hidden = string.IsNullOrWhiteSpace(hidden)
                        ? new List<string>()
                        : System.Text.Json.JsonSerializer.Deserialize<List<string>>(hidden)
                };

                await _dashboardService.SaveDashboardLayoutAsync(currentUserId, layout, dashboardKey);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "An error occurred saving the dashboard layout: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardLayout(string dashboardKey = "employer")
        {
            try
            {
                int.TryParse(GetCurrentUserId(), out int currentUserId);
                if (currentUserId <= 0)
                    return Json(new { success = false, order = new List<string>(), hidden = new List<string>() });

                var layout = await _dashboardService.GetDashboardLayoutAsync(currentUserId, dashboardKey);
                return Json(new { success = true, order = layout?.Order ?? new List<string>(), hidden = layout?.Hidden ?? new List<string>() });
            }
            catch
            {
                return Json(new { success = false, order = new List<string>(), hidden = new List<string>() });
            }
        }
    }
}
