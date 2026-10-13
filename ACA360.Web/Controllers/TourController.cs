using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models.Dashboard;
using ACA360.Logging.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Remembers which guided tours (js/aca360-tour.js) the signed-in user has finished or closed,
    /// so a tour starts by itself only once per user, on any device. Stored with the dashboard
    /// layouts (UserDashboardLayout, key "tours"): the Order list holds the tour ids seen, e.g.
    /// "home:1". A tour whose id changes (new version) shows again.
    /// </summary>
    [Authorize]
    public class TourController : BaseController
    {
        private const string StoreKey = "tours";
        private static readonly Regex TourId = new(@"^[a-z0-9-]{1,32}:\d{1,3}$", RegexOptions.Compiled);

        private readonly IDashboardService _dashboardService;
        private readonly ILoggerService _logger;

        public TourController(IDashboardService dashboardService, ILoggerService logger)
        {
            _dashboardService = dashboardService;
            _logger = logger;
        }

        /// <summary>Whether the user has already seen this tour.</summary>
        [HttpGet]
        public async Task<IActionResult> Status(string tour)
        {
            tour = Normalise(tour);   // the global binder upper-cases strings
            int.TryParse(GetCurrentUserId(), out int userId);
            if (userId <= 0 || string.IsNullOrEmpty(tour) || !TourId.IsMatch(tour))
                return Json(new { success = false, seen = true });   // unknown: never auto-start

            try
            {
                var seen = await _dashboardService.GetDashboardLayoutAsync(userId, StoreKey);
                return Json(new { success = true, seen = seen.Order?.Contains(tour) == true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Status), nameof(TourController), $"Could not read tour state for user {userId}");
                return Json(new { success = false, seen = true });
            }
        }

        /// <summary>Records that the user finished or closed this tour.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Seen(string tour)
        {
            tour = Normalise(tour);
            int.TryParse(GetCurrentUserId(), out int userId);
            if (userId <= 0 || string.IsNullOrEmpty(tour) || !TourId.IsMatch(tour))
                return Json(new { success = false });

            try
            {
                var state = await _dashboardService.GetDashboardLayoutAsync(userId, StoreKey);
                var seen = state.Order ?? new();
                if (!seen.Contains(tour))
                {
                    // Keep one entry per tour: a newer version replaces the older one.
                    var name = tour.Split(':')[0] + ":";
                    seen = seen.Where(t => !t.StartsWith(name, StringComparison.Ordinal)).Append(tour).ToList();
                    await _dashboardService.SaveDashboardLayoutAsync(userId, new DashboardLayoutDto { Order = seen }, StoreKey);
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Seen), nameof(TourController), $"Could not save tour state for user {userId}");
                return Json(new { success = false });
            }
        }

        private static string Normalise(string tour) => (tour ?? "").Trim().ToLowerInvariant();
    }
}
