using ACA360.Core.Constants;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using ACA360.Web.Filters;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class IntegrationController : Controller
    {
        private readonly IApiClientService _apiService;
        private readonly ILoggerService _logger;

        public IntegrationController(IApiClientService apiService, ILoggerService logger)
        {
            _apiService = apiService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves and renders the API Integration Dashboard for a specific employer.
        /// Displays all existing API keys/clients associated with the given employer context.
        /// </summary>
        /// <param name="id">The unique identifier of the Employer.</param>
        /// <returns>A view displaying the list of active API clients.</returns>
        [HttpGet]
        public async Task<IActionResult> Index(int id)
        {
            try
            {
                // Validate employer ID; redirect to the application home if invalid or missing.
                // Note: In a production scenario, authorization filters should also verify the current user has access to this EmployerId.
                if (id == 0)
                {
                    return RedirectToAction("Index", "Home");
                }

                // Fetch the list of configured API clients for the employer from the service layer
                var keys = await _apiService.GetClientsForEmployerAsync(id);

                // Pass the employer ID via ViewBag so it can be utilized in subsequent UI actions (e.g., creating a new key)
                ViewBag.EmployerId = id;

                return View(keys);
            }
            catch (Exception ex)
            {
                // Log the exception details for diagnostics and return a safe 500 error to the client
                _logger.LogError(ex, nameof(Index), "IntegrationController", $"An error occurred while loading the Integration Dashboard for Employer ID {id}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the integration dashboard.");
            }
        }

        /// <summary>
        /// Generates a new API key/client credential set for the specified employer.
        /// Typically invoked via AJAX so the newly generated secret can be displayed to the user securely.
        /// </summary>
        /// <param name="employerId">The ID of the employer requesting the new key.</param>
        /// <param name="name">A friendly descriptive name for the API key.</param>
        /// <returns>A JSON response containing the new Client ID and Client Secret.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope]
        public async Task<IActionResult> CreateKey(int employerId, string name)
        {
            try
            {
                // Validation: Ensure the key name is provided and not entirely whitespace
                if (string.IsNullOrWhiteSpace(name))
                {
                    return BadRequest(new { success = false, message = "Key Name is required." });
                }

                // Generate the new client credentials using the integration service
                var result = await _apiService.GenerateNewClientAsync(employerId, name);

                // Return a JSON payload containing the sensitive secret so the UI modal can display it to the user exactly once
                return Json(new { success = true, clientId = result.ClientId, secret = result.ClientSecret });
            }
            catch (Exception ex)
            {
                // Log the creation failure securely without exposing stack traces to the AJAX client
                _logger.LogError(ex, nameof(CreateKey), "IntegrationController", $"An error occurred while creating a new API key for Employer ID {employerId}.");

                // Return a 500 error code with a JSON payload structure expected by the AJAX handler
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while generating the API key." });
            }
        }

        /// <summary>
        /// Revokes and invalidates an existing API key, permanently preventing further access via that credential set.
        /// </summary>
        /// <param name="clientId">The unique GUID identifier of the API client to revoke.</param>
        /// <param name="employerId">The ID of the associated employer.</param>
        /// <returns>A redirection to the Integration Dashboard to refresh the list.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope]
        public async Task<IActionResult> RevokeKey(Guid clientId, int employerId)
        {
            try
            {
                // Input validation: Ensure valid IDs are passed before attempting revocation
                if (clientId == Guid.Empty || employerId <= 0)
                {
                    return BadRequest("Valid Client ID and Employer ID are required to revoke an API key.");
                }

                // Execute the revocation operation in the service layer to disable the API client
                await _apiService.RevokeClientAsync(clientId, employerId);

                // Redirect back to the index dashboard view to reflect the updated state
                return RedirectToAction("Index", new { id = employerId });
            }
            catch (Exception ex)
            {
                // Log the revocation failure, as this is a sensitive security operation
                _logger.LogError(ex, nameof(RevokeKey), "IntegrationController", $"An error occurred while revoking API key {clientId} for Employer ID {employerId}.");

                // Return a generic 500 Internal Server Error status
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while revoking the API key.");
            }
        }
    }
}