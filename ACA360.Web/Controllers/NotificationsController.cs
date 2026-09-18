using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class NotificationsController : BaseController
    {
        private readonly INotificationService _notificationService; // Service handling notification business logic
        private readonly ILogger<NotificationsController> _logger;  // Logger for capturing errors and diagnostics

        /// <summary>
        /// Initializes the NotificationsController with required service and logger dependencies.
        /// </summary>
        /// <param name="notifService">Service responsible for all notification operations.</param>
        /// <param name="logger">Logger instance for runtime diagnostics and error reporting.</param>
        public NotificationsController(INotificationService notifService, ILogger<NotificationsController> logger)
        {
            _notificationService = notifService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves all unread notifications for the currently authenticated user and marks them as read.
        /// Intended to be polled by the frontend to surface new notification badges/popups.
        /// GET: /Notifications/GetNewNotifications
        /// </summary>
        /// <returns>A JSON list of unread notifications, or an empty result on failure.</returns>
        [HttpGet]
        public async Task<IActionResult> GetNewNotifications()
        {
            try
            {
                // Resolve the current user's identity from JWT/cookie claims
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                // Fetch all unread notifications for the resolved user
                var notifs = await _notificationService.GetUnreadNotificationsAsync(userId);

                // Mark each fetched notification as read so they don't reappear on subsequent polls
                foreach (var n in notifs)
                    await _notificationService.MarkAsReadAsync(n.NotificationId);

                return Json(notifs);
            }
            catch (Exception ex)
            {
                // Log unexpected failures; return an empty JSON array to avoid breaking frontend polling
                _logger.LogError(ex, "An unexpected error occurred in GetNewNotifications for UserId: {UserId}",
                    User?.FindFirstValue(ClaimTypes.NameIdentifier));
                return StatusCode(500, "An unexpected error occurred while fetching notifications.");
            }
        }

        /// <summary>
        /// Renders the paginated "View All Notifications" page with optional filter support.
        /// Calls the underlying paginated service (backed by sp_GetUserNotificationsPaginated).
        /// GET: /Notifications/Index?filter={filter}&page={page}
        /// </summary>
        /// <param name="filter">Optional filter string to narrow notification results (default: "All").</param>
        /// <param name="page">The current page number for pagination (default: 1).</param>
        /// <returns>The Index view populated with paginated notifications, or an error response on failure.</returns>
        [HttpGet]
        public async Task<IActionResult> Index(string filter = "All", int page = 1)
        {
            try
            {
                // Resolve the currently authenticated user's ID from the base controller helper
                string userId = GetCurrentUserId();
                int pageSize = 10;

                // Retrieve a paginated slice of notifications from the service layer
                // sp_GetUserNotificationsPaginated returns (List<UserNotification>, int TotalCount)
                var result = await _notificationService.GetNotificationsPaginatedAsync(userId, filter, page, pageSize);

                // Map the service result into the view model for rendering
                var model = new NotificationListViewModel
                {
                    Notifications = result.Notifications,
                    TotalItems = result.TotalCount,
                    CurrentPage = page,
                    PageSize = pageSize,
                    ActiveFilter = filter
                };

                return View(model);
            }
            catch (Exception ex)
            {
                // Log the failure with contextual filter and pagination info for easier debugging
                _logger.LogError(ex, "An unexpected error occurred in Index. Filter: {Filter}, Page: {Page}", filter, page);
                return StatusCode(500, "An unexpected error occurred while loading notifications.");
            }
        }

        /// <summary>
        /// Marks a single notification as read by its unique identifier.
        /// Typically invoked when a user clicks on an individual notification.
        /// POST: /Notifications/MarkAsRead
        /// </summary>
        /// <param name="id">The unique identifier of the notification to mark as read.</param>
        /// <returns>200 OK on success, 400 Bad Request for invalid ID, or 500 on unexpected errors.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            try
            {
                // Guard against invalid or non-positive notification IDs before hitting the service
                if (id <= 0)
                    return BadRequest("A valid notification ID is required.");

                // Delegate the read-state update to the service layer
                await _notificationService.MarkAsReadAsync(id);

                return Ok();
            }
            catch (ArgumentException ex)
            {
                // Handle validation-level failures raised by the service (e.g., notification not found)
                _logger.LogWarning(ex, "Validation error in MarkAsRead for NotificationId: {NotificationId}", id);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // Log unexpected failures with the notification ID for traceability
                _logger.LogError(ex, "An unexpected error occurred in MarkAsRead for NotificationId: {NotificationId}", id);
                return StatusCode(500, "An unexpected error occurred while marking the notification as read.");
            }
        }

        /// <summary>
        /// Marks all notifications as read for the currently authenticated user.
        /// Calls sp_MarkAllNotificationsRead via the service layer and redirects back to the Index page.
        /// POST: /Notifications/MarkAllAsRead
        /// </summary>
        /// <returns>Redirects to Index on success, or returns a 500 response on unexpected failure.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllAsRead()
        {
            try
            {
                // Resolve the currently authenticated user's ID from the base controller helper
                string userId = GetCurrentUserId();

                // Invoke the bulk read-state update via the service layer (backed by sp_MarkAllNotificationsRead)
                await _notificationService.MarkAllAsReadAsync(userId);

                // Surface a success message to the user on the next page load via TempData
                TempData["SuccessMessage"] = "All notifications marked as read.";

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                // Log unexpected failures; avoid redirecting on error to prevent silent data inconsistency
                _logger.LogError(ex, "An unexpected error occurred in MarkAllAsRead for UserId: {UserId}", GetCurrentUserId());
                return StatusCode(500, "An unexpected error occurred while marking all notifications as read.");
            }
        }
    }
}