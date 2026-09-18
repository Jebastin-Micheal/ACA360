using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.BusinessLogic.Validators;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class UserAccountsController : BaseController
    {
        private readonly IUserAccountsService _userAccountsService;
        private readonly ILoggerService _logger;
        private readonly IExportService _exportService;
        private readonly ISystemSettingsService _systemSettingsService;

        public UserAccountsController(IUserAccountsService userAccountsService, ILoggerService logger, IExportService exportService, ISystemSettingsService systemSettingsService)
        {
            _userAccountsService = userAccountsService;
            _logger = logger;
            _exportService = exportService;
            _systemSettingsService = systemSettingsService;
        }

        /// <summary>
        /// Retrieves the main index view for the User Accounts dashboard.
        /// Loads a paginated, sortable, and filterable list of user accounts.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(int pageIndex = 1, int pageSize = 10, string search = "", int sortColumn = 0, string sortOrder = "asc", int roleId = 0, int statusId = 0)
        {
            try
            {
                // Prepare pagination details based on request parameters
                PaginationEntity paginationEntity = new PaginationEntity
                {
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder
                };

                // Fetch user accounts, pagination metadata, and role summary statistics from the service
                var (userAccountsList, metadata, roleSummary) = await _userAccountsService.GetUserAccountsList(paginationEntity, roleId, statusId);

                // Construct the view model to bind data to the view
                var viewModel = new UserAccountsViewModel
                {
                    UserAccounts = userAccountsList,
                    Metadata = metadata,
                    RoleSummary = roleSummary,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder
                };

                // Populate required dropdown lists for the UI
                await PopulateDropdownsAsync();

                return View(viewModel);
            }
            catch (Exception ex)
            {
                // Log the exception and return a standard 500 internal server error response
                _logger.LogError(ex, nameof(Index), nameof(UserAccountsController), $"Error loading UserAccounts list for pageIndex: {pageIndex}, search: {search}", HttpContext.Connection.RemoteIpAddress?.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the user accounts index.");
            }
        }

        /// <summary>
        /// Retrieves a partial view of the user accounts list for AJAX datatable updates.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> UserAccountsListPartial(int pageIndex, string search, int sortColumn, string sortOrder, int pageSize, int roleId, int statusId)
        {
            try
            {
                // Map incoming parameters to the pagination entity
                PaginationEntity paginationEntity = new PaginationEntity
                {
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder
                };

                // Fetch the requested page of user accounts
                var (userAccountsList, metadata, roleSummary) = await _userAccountsService.GetUserAccountsList(paginationEntity, roleId, statusId);

                // Construct view model for the partial rendering
                var viewModel = new UserAccountsViewModel
                {
                    UserAccounts = userAccountsList,
                    Metadata = metadata,
                    RoleSummary = roleSummary,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder
                };

                return PartialView("_UserAccountsListPartial", viewModel);
            }
            catch (Exception ex)
            {
                // Log failure and return 500 status code to gracefully fail the AJAX request
                _logger.LogError(ex, nameof(UserAccountsListPartial), nameof(UserAccountsController), $"Error loading UserAccounts list partial for pageIndex: {pageIndex}, search: {search}", HttpContext.Connection.RemoteIpAddress?.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the user accounts list.");
            }
        }

        /// <summary>
        /// Renders a partial view containing a blank form for creating a new user account.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Add_New()
        {
            try
            {
                // Load necessary dropdown options for the form UI
                await PopulateDropdownsAsync();

                ViewBag.PasswordPolicy = await _systemSettingsService.GetSecuritySettingsAsync();

                // Initialize a new user account model
                var newUserAccounts = new UserAccounts { createdon = DateTime.Now };

                return PartialView("_UserAccountsFormPartial", newUserAccounts);
            }
            catch (Exception ex)
            {
                // Log and return 500 on form load failure
                _logger.LogError(ex, nameof(Add_New), nameof(UserAccountsController), "Error loading the new user account form.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while generating the new user account form.");
            }
        }

        /// <summary>
        /// Retrieves an existing user account by its ID to populate the edit form modal.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetUserAccountsById(long id, int? roleId = null)
        {
            try
            {
                // Validate incoming ID
                if (id <= 0)
                {
                    return BadRequest("Invalid User Account Request.");
                }

                // Fetch existing user account data
                var userAccount = await _userAccountsService.GetUserAccountsByIdAsync(id, roleId);
                if (userAccount == null)
                {
                    return NotFound("User Account not found.");
                }

                // Populate required dropdown data before rendering the partial
                await PopulateDropdownsAsync();
                ViewBag.PasswordPolicy = await _systemSettingsService.GetSecuritySettingsAsync();

                return PartialView("_UserAccountsFormPartial", userAccount);
            }
            catch (Exception ex)
            {
                // Log and return 500 internal server error
                _logger.LogError(ex, nameof(GetUserAccountsById), nameof(UserAccountsController), $"Error fetching user account ID: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while fetching the user account details.");
            }
        }

        /// <summary>
        /// Retrieves an existing user account by its ID to populate a read-only view modal.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetUserAccountsByIdView(long id, int? roleId = null)
        {
            try
            {
                // Validate incoming ID
                if (id <= 0)
                {
                    return BadRequest("Invalid User Account Request.");
                }

                // Fetch user account data for viewing
                var userAccount = await _userAccountsService.GetUserAccountsByIdAsync(id, roleId);
                if (userAccount == null)
                {
                    return NotFound("User Account not found.");
                }

                return PartialView("_UserAccountsViewPartial", userAccount);
            }
            catch (Exception ex)
            {
                // Log the exception and return a standard server error
                _logger.LogError(ex, nameof(GetUserAccountsByIdView), nameof(UserAccountsController), $"Error fetching user account view for ID: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while fetching the user account view.");
            }
        }

        /// <summary>
        /// Processes the submission of a new user account and saves it to the database.
        /// Supports handling profile picture file uploads.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserAccounts userAccounts)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                // ★ add — validate the entered/generated password against system policy
                var policy = await _systemSettingsService.GetSecuritySettingsAsync();
                var errors = PasswordPolicyValidator.Validate(userAccounts.UserAccounts_Password, policy);
                if (errors.Any())
                {
                    foreach (var e in errors)
                        ModelState.AddModelError(nameof(userAccounts.UserAccounts_Password), e);
                    return BadRequest(ModelState);
                }

                string profilePictureFileName = null;
                if (userAccounts.ProfilePictureFile != null && userAccounts.ProfilePictureFile.Length > 0)
                {
                    profilePictureFileName = await SaveProfilePictureAsync(userAccounts.ProfilePictureFile);
                }

                await _userAccountsService.AddUserAccountsAsync(userAccounts, profilePictureFileName);
                TempData["SuccessMessage"] = "User account created successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Create), nameof(UserAccountsController), "Error creating new user account.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while creating the user account.");
            }
        }

        /// <summary>
        /// Processes the submission of edits to an existing user account and updates the database.
        /// Supports handling profile picture updates and replacing old files.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UserAccounts userAccounts)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userAccounts.UserAccounts_Password))
                {
                    ModelState.Remove(nameof(userAccounts.UserAccounts_Password));
                }
                else
                {
                    // ★ add — validate only when admin is actually setting/changing the password
                    var policy = await _systemSettingsService.GetSecuritySettingsAsync();
                    var errors = PasswordPolicyValidator.Validate(userAccounts.UserAccounts_Password, policy);
                    foreach (var e in errors)
                        ModelState.AddModelError(nameof(userAccounts.UserAccounts_Password), e);
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                string profilePictureFileName = null;
                var existingUserAccount = await _userAccountsService.GetUserAccountsByIdAsync(userAccounts.UserAccounts_ID, userAccounts.UserAccounts_Role);

                if (userAccounts.ProfilePictureFile != null && userAccounts.ProfilePictureFile.Length > 0)
                {
                    profilePictureFileName = await SaveProfilePictureAsync(userAccounts.ProfilePictureFile);

                    if (existingUserAccount != null && !string.IsNullOrEmpty(existingUserAccount.ProfilePicture))
                    {
                        DeleteProfilePicture(existingUserAccount.ProfilePicture);
                    }
                }

                if (string.IsNullOrWhiteSpace(userAccounts.UserAccounts_Password))
                {
                    userAccounts.UserAccounts_Password = existingUserAccount?.UserAccounts_Password;
                }

                await _userAccountsService.UpdateUserAccountsAsync(userAccounts, profilePictureFileName);

                TempData["SuccessMessage"] = "User account updated successfully!";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Edit), nameof(UserAccountsController), $"Error updating user account ID: {userAccounts?.UserAccounts_ID}");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while updating the user account.");
            }
        }

        /// <summary>
        /// Performs a deletion operation for a specific user account by its ID and Role.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(long id, int roleId)
        {
            try
            {
                // Ensure the provided IDs are valid
                if (id <= 0)
                {
                    return BadRequest(new { success = false, message = "Invalid user account ID." });
                }

                // Execute the deletion through the service layer
                await _userAccountsService.DeleteUserAccountsAsync(id, roleId);

                return Json(new { success = true, message = "User account deleted successfully!" });
            }
            catch (Exception ex)
            {
                // Log deletion error and return structured JSON 500 error
                _logger.LogError(ex, nameof(Delete), nameof(UserAccountsController), $"Error deleting user account ID: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while deleting the user account." });
            }
        }

        // -- Private helpers ---------------------------------------------------

        /// <summary>
        /// Saves a profile picture upload to the local filesystem and returns the unique generated filename.
        /// </summary>
        private async Task<string> SaveProfilePictureAsync(IFormFile file)
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profile-pictures");

            // Create directory if it doesn't exist
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            // Generate unique file name to avoid collisions
            var uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            // Save the file bytes directly to the filesystem
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            return uniqueFileName;
        }

        /// <summary>
        /// Deletes a specified profile picture from the local filesystem to free up storage.
        /// </summary>
        private void DeleteProfilePicture(string fileName)
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profile-pictures", fileName);

            // Prevent exceptions by verifying the file actually exists before deletion
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }

        /// <summary>
        /// Populates ViewBags with lookup options required for dropdowns in the User Account forms.
        /// Refactored to properly await asynchronous data fetching.
        /// </summary>
        private async Task PopulateDropdownsAsync()
        {
            ViewBag.StatusOptions = new List<SelectListItem>
            {
                new SelectListItem { Text = "Active", Value = "1" },
                new SelectListItem { Text = "Inactive", Value = "2" },
                new SelectListItem { Text = "Terminated", Value = "3" }
            };
            var states = await _userAccountsService.GetAllStatesAsync();
            ViewBag.StateOptions = states
             .Select(x => new SelectListItem
             {
                Text = x.StateName,
                Value = x.StateId.ToString()
             }).ToList();

            // F-01: "Enable" is deliberately absent. No second-factor provider is
            // implemented, and setting the flag locked the account out permanently —
            // login redirected to Account/MFA, an action that does not exist, so the
            // password check succeeded, no cookie was issued, and the user could not
            // reach Settings to undo it. An administrator setting this across the
            // estate would have locked out everyone at once.
            // Restore the "Enable" option when the challenge is built.
            ViewBag.MFAOptions = new List<SelectListItem>
            {
                new SelectListItem { Text = "Disable", Value = "0" }
            };

            // Fetch the available roles from the service layer dynamically
            ViewBag.RoleOptions = await _userAccountsService.GetAll_Roles();
        }

        // ============================================================
        // ADDED: Export action - follows the exact same pattern as
        // FlagsController.Export. format = csv | excel | pdf | print
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> Export(string format, string search,
    int sortColumn = 0, string sortOrder = "asc",
    int roleId = 0, int statusId = 0, string title = "User Accounts")
        {
            try
            {
                var paginationEntity = new PaginationEntity
                {
                    PageIndex = 1,
                    PageSize = int.MaxValue,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder
                };

                var (userAccountsList, _, _) = await _userAccountsService
                    .GetUserAccountsList(paginationEntity, roleId, statusId);

                return HandleExport(
                    _exportService,
                    format,
                    title: title,
                    fileName: "UserAccounts",
                    columns: new List<ExportColumn>
                    {
                new ExportColumn("FullName", "Name"),
                new ExportColumn("Email", "Email"),
                new ExportColumn("Role", "Role"),
                new ExportColumn("MFA", "MFA"),
                new ExportColumn("Status", "Status"),
                new ExportColumn("CreatedOn", "Created On")
                    },
                    rows: userAccountsList.Select(u => new Dictionary<string, object>
                    {
                        ["FullName"] = u.UserAccounts_Full_Name,
                        ["Email"] = u.UserAccounts_Email,
                        ["Role"] = u.UserAccounts_Role_Name,
                        ["MFA"] = u.IsMFA == 1 ? "Enabled" : "Disabled",
                        ["Status"] = u.UserAccounts_Status == 1 ? "Active"
                                   : u.UserAccounts_Status == 2 ? "Inactive"
                                   : u.UserAccounts_Status == 3 ? "Terminated"
                                   : "Unknown",
                        ["CreatedOn"] = u.createdon?.ToString("yyyy-MM-dd") ?? ""
                    }).ToList()
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Export), nameof(UserAccountsController),
                    $"Error exporting. Format: '{format}'.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred while exporting.");
            }
        }
    }
}