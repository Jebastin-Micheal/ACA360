using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class UISettingsController : Controller
    {
        private readonly IUISettingsService _uiSettingsService;
        private readonly ILoggerService _logger;

        public UISettingsController(IUISettingsService uiSettingsService, ILoggerService logger)
        {
            _uiSettingsService = uiSettingsService;
            _logger = logger;
        }

        /// <summary>
        /// Saves the UI settings for the currently authenticated user or system context.
        /// </summary>
        /// <param name="settings">The UI settings payload submitted by the client.</param>
        /// <returns>An Ok response upon successful save, or an appropriate error status.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Save([FromBody] UISettingsModel settings)
        {
            try
            {
                // Validate incoming payload to ensure data integrity
                if (settings == null)
                {
                    return BadRequest("Settings payload cannot be null.");
                }

                // Determine the active user's identity; default to "system" if unauthenticated or missing
                var user = User.Identity?.Name ?? "system";

                // Execute the persistence logic through the service layer
                _uiSettingsService.SaveSettings(settings, user);

                return Ok();
            }
            catch (Exception ex)
            {
                // Log the exception securely for troubleshooting
                _logger.LogError(ex, nameof(Save), nameof(UISettingsController), "An error occurred while saving UI settings.");

                // Return a generic 500 Internal Server Error to prevent leaking sensitive application details
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while saving UI settings.");
            }
        }

        /// <summary>
        /// Retrieves the active UI settings from the data store.
        /// </summary>
        /// <returns>A JSON representation of the current UI settings.</returns>
        [HttpGet]
        public async Task<IActionResult> GetAsync()
        {
            try
            {
                // Fetch the active UI configuration asynchronously
                var settings = await _uiSettingsService.GetSettings();

                // Return the formatted data to the requesting client
                return Json(settings);
            }
            catch (Exception ex)
            {
                // Log the exception related to fetching settings
                _logger.LogError(ex, nameof(GetAsync), nameof(UISettingsController), "An error occurred while retrieving UI settings.");

                // Respond with a 500 Server Error to gracefully notify the client
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while retrieving UI settings.");
            }
        }
    }
}