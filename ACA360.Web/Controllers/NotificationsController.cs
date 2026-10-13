using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
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

        private int CurrentUserIdInt() => int.TryParse(GetCurrentUserId(), out var id) ? id : 0;

        /// <summary>
        /// The bell: the unread count and the latest notifications, newest unread first. Polled by the
        /// layout. It used to look the user up by Ref_ID (not User_ID) and to mark everything read the
        /// moment it was fetched, so a notification showed for one poll at most.
        /// GET: /Notifications/GetNewNotifications
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetNewNotifications()
        {
            try
            {
                var (unread, items) = await _notificationService.GetRecentAsync(CurrentUserIdInt());
                return Json(new
                {
                    unread,
                    items = items.Select(n => new
                    {
                        n.NotificationId, n.Title, n.Message, n.Type, n.IsRead,
                        // stored in UTC
                        CreatedDate = DateTime.SpecifyKind(n.CreatedDate, DateTimeKind.Utc),
                        openUrl = Url.Action(nameof(Open), new { id = n.NotificationId }),
                    }),
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loading the bell failed for UserId: {UserId}", GetCurrentUserId());
                return StatusCode(500, "An unexpected error occurred while fetching notifications.");
            }
        }

        /// <summary>Opens a notification: marks it read, then goes to its page (only a page of this app).</summary>
        [HttpGet]
        public async Task<IActionResult> Open(int id)
        {
            var own = await _notificationService.GetOwnAsync(id, CurrentUserIdInt());
            if (own == null) return RedirectToAction(nameof(Index));
            await _notificationService.SetReadAsync(id, CurrentUserIdInt(), true);
            var link = own.LinkUrl;
            return !string.IsNullOrWhiteSpace(link) && Url.IsLocalUrl(link) ? LocalRedirect(link) : RedirectToAction(nameof(Index));
        }

        /// <summary>Marks one of the user's notifications unread again.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsUnread(int id)
            => await _notificationService.SetReadAsync(id, CurrentUserIdInt(), false) ? Ok() : NotFound();

        /// <summary>Turns one kind of notification on or off for the user (script 070).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePreference(string eventCode, bool isEnabled)
        {
            if (!NotificationPreference.Events.ContainsKey(eventCode ?? "")) return BadRequest(new { success = false, message = "Unknown notification." });
            await _notificationService.SavePreferenceAsync(CurrentUserIdInt(), eventCode!, isEnabled);
            return Json(new { success = true });
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
                    ActiveFilter = filter,
                    Preferences = await _notificationService.GetPreferencesAsync(CurrentUserIdInt()),
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

                // Only the owner's own notification (it used to mark anyone's).
                return await _notificationService.SetReadAsync(id, CurrentUserIdInt(), true) ? Ok() : NotFound();
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