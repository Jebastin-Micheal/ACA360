using ACA360.Repositories.Implementations;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Mvc;

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
    }
}
