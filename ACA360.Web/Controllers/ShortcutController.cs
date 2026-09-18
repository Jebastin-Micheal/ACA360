using ACA360.BusinessLogic.Interfaces;
using ACA360.Logging.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.Controllers
{
    [Authorize]
    public class ShortcutController : Controller
    {
        private readonly IShortcutService _shortcutService;
        private readonly ILoggerService _logger;

        public ShortcutController(IShortcutService shortcutService, ILoggerService logger)
        {
            _shortcutService = shortcutService;
            _logger = logger;
        }

        private long? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            return long.TryParse(userIdClaim, out long userId) ? userId : (long?)null;
        }

        [HttpGet]
        public async Task<IActionResult> GetMyShortcuts()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null) return Unauthorized();

                var list = await _shortcutService.GetUserShortcuts(userId.Value);
                return Json(new { success = true, data = list });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetMyShortcuts), nameof(ShortcutController), "Error loading shortcuts.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error loading shortcuts.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailable()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null) return Unauthorized();

                var list = await _shortcutService.GetAvailableShortcutMenus(userId.Value);
                return Json(new { success = true, data = list });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAvailable), nameof(ShortcutController), "Error loading available menus.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error loading available menus.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int menuId)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null) return Unauthorized();

                var added = await _shortcutService.AddUserShortcut(userId.Value, menuId);
                if (!added)
                    return BadRequest(new { success = false, message = "All slots are full. Please remove one first." });

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Add), nameof(ShortcutController), $"Error adding menu {menuId}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Error adding shortcut." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int menuId)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null) return Unauthorized();

                await _shortcutService.RemoveUserShortcut(userId.Value, menuId);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Remove), nameof(ShortcutController), $"Error removing menu {menuId}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Error removing shortcut." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reorder([FromBody] List<int> orderedMenuIds)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null) return Unauthorized();

                await _shortcutService.ReorderUserShortcuts(userId.Value, orderedMenuIds);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Reorder), nameof(ShortcutController), "Error reordering shortcuts.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Error reordering." });
            }
        }
    }
}
