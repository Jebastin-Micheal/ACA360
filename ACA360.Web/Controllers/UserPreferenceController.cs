using ACA360.Repositories.Implementations;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ACA360.Web.Controllers
{
    public class UserPreferenceController : BaseController
    {
        private readonly IUserPreferenceService _userPreferenceService;

        public UserPreferenceController(IUserPreferenceService userPreferenceService)
        {
            _userPreferenceService = userPreferenceService;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveViewMode(string moduleKey, string viewMode)
        {
            try
            {
                // Note: Replace GetCurrentUserIdInt() with your actual method to get the user ID
                var userId = GetCurrentUserId();
                await _userPreferenceService.SetViewModeAsync(userId, moduleKey, viewMode);
                return Ok();
            }
            catch
            {
                return StatusCode(500, "Error saving preference");
            }
        }

        // A grid key is the list's own address (e.g. "/plan/planlistpartial").
        private static readonly Regex GridKey = new(@"^[a-z0-9/_\-]{1,80}$", RegexOptions.Compiled);

        /// <summary>The saved state of a list page (common-grid.js), or {} when there is none.</summary>
        [HttpGet]
        public async Task<IActionResult> GridState(string key)
        {
            if (string.IsNullOrEmpty(key) || !GridKey.IsMatch(key)) return BadRequest();
            var json = await _userPreferenceService.GetGridStateAsync(GetCurrentUserId(), key);
            return Content(json ?? "{}", "application/json");
        }

        /// <summary>Saves a list page's search, sort, page size, hidden columns and named filters. Empty state removes it.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveGridState(string key, string? state)
        {
            if (string.IsNullOrEmpty(key) || !GridKey.IsMatch(key)) return BadRequest();
            if (string.IsNullOrWhiteSpace(state) || state == "{}")
            {
                await _userPreferenceService.SetGridStateAsync(GetCurrentUserId(), key, null);
                return Ok();
            }
            if (state.Length > 4000) return BadRequest();
            try
            {
                using var doc = JsonDocument.Parse(state);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return BadRequest();
            }
            catch (JsonException)
            {
                return BadRequest();
            }
            await _userPreferenceService.SetGridStateAsync(GetCurrentUserId(), key, state);
            return Ok();
        }
    }
}
