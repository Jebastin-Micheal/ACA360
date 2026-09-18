using ACA360.Core.Constants;
using ACA360.Logging.Interfaces;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Diagnostics;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.All)]
    public class HomeController : BaseController
    {
        private readonly ILoggerService _logger;

        public HomeController(ILoggerService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Entry point for the application. Evaluates the user's current employer context and routes them appropriately.
        /// </summary>
        /// <returns>A redirect to either the Employer Selection or the main Dashboard.</returns>
        public IActionResult Index()
        {
            try
            {
                if (User.IsInRole(UserRoles.DataAnalyst) || User.IsInRole(UserRoles.SuperAdmin))
                {
                    return RedirectToAction("Index", "Dashboard");
                }

                // Retrieve the currently selected employer ID from the context
                var employerId = GetCurrentEmployerId();

                // If no employer is selected or the context is missing, route to the employer selection view
                if (string.IsNullOrWhiteSpace(employerId) || employerId == "0")
                {
                    return RedirectToAction("Index", "SelectEmployer");
                }
                else if (User.IsInRole(UserRoles.DataAnalyst))
                {
                    return RedirectToAction("Index", "Dashboard");
                }
                else
                {
                    // If an employer is already in context, route directly to the main dashboard
                    return RedirectToAction("Index", "Dashboard");
                }
            }
            catch (Exception ex)
            {
                // Log unexpected issues during initial routing evaluation
                _logger.LogError(ex, nameof(Index), "HomeController", "An error occurred while evaluating the home index routing.");

                // Return a 500 Internal Server Error status
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred during routing.");
            }
        }

        /// <summary>
        /// Global error handler used by the exception handling middleware for unhandled application exceptions (500 errors).
        /// </summary>
        /// <returns>The generic error view with trace identifiers.</returns>
        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            try
            {
                // Set explicit status code context for the view presentation
                ViewBag.StatusCode = 500;

                // Return the standard error view populated with the current trace/activity ID for diagnostics
                return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
            catch (Exception ex)
            {
                // Failsafe logging if the error handler itself crashes
                _logger.LogError(ex, nameof(Error), "HomeController", "An error occurred within the global application error handler.");

                // Return a raw 500 status to prevent recursive error handling loops
                return StatusCode(StatusCodes.Status500InternalServerError, "A critical error occurred while displaying the error page.");
            }
        }

        /// <summary>
        /// Handles HTTP status code errors (e.g., 404 Not Found, 403 Forbidden) and displays an appropriate view.
        /// </summary>
        /// <param name="statusCode">The specific HTTP status code encountered by the pipeline.</param>
        /// <returns>The error view contextualized for the status code.</returns>
        [AllowAnonymous]
        [Route("Home/Error/{statusCode}")]
        public IActionResult Error(int statusCode)
        {
            try
            {
                // Pass the specific status code to the view so it can tailor the UI message (e.g., "Page Not Found")
                ViewBag.StatusCode = statusCode;

                // Render the error view using the current trace/activity ID
                return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
            catch (Exception ex)
            {
                // Failsafe logging if the status code error handler crashes
                _logger.LogError(ex, nameof(Error), "HomeController", $"An error occurred within the status code error handler for code {statusCode}.");

                // Return a raw 500 status to safely exit the pipeline
                return StatusCode(StatusCodes.Status500InternalServerError, "A critical error occurred while displaying the error page.");
            }
        }
    }
}