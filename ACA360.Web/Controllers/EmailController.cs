using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Mvc;
using ACA360.Core.Constants;
using Microsoft.AspNetCore.Authorization;
namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.InternalTeam)]
    // Inherit from Controller instead of ControllerBase
    public class EmailController : BaseController
    {
        private readonly IEmailService _emailService;

        public EmailController(IEmailService emailService)
        {
            _emailService = emailService;
        }

        // Standard MVC Route: /Email/Index
        public IActionResult Index() => View();

        // --- API ENDPOINTS ---
        // Every endpoint works on the signed-in user's own mailbox. The user is taken from the
        // auth cookie, never from the request, so one user cannot read or change another's mail.

        private bool TryGetUser(out string userId)
        {
            userId = GetCurrentUserId();
            return userId != "0";
        }

        [HttpGet("/api/email/inbox")]
        public async Task<IActionResult> GetInbox()
        {
            if (!TryGetUser(out var userId)) return Unauthorized();
            return Ok(await _emailService.GetInboxAsync(false, userId));
        }

        [HttpGet("/api/email/trash")]
        public async Task<IActionResult> GetTrash()
        {
            if (!TryGetUser(out var userId)) return Unauthorized();
            return Ok(await _emailService.GetTrashAsync(true, userId));
        }

        [HttpPost("/api/email/compose")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Compose([FromBody] EmailItem email)
        {
            if (!TryGetUser(out var userId)) return Unauthorized();
            email.UserId = userId;
            await _emailService.ComposeEmailAsync(email);
            return Ok(new { message = "Email saved successfully" });
        }

        [HttpPut("/api/email/{id}/trash")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveToTrash(string id)
        {
            if (!TryGetUser(out var userId)) return Unauthorized();
            await _emailService.MoveToTrashAsync(id, true, userId);
            return Ok();
        }

        [HttpPut("/api/email/{id}/star")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStar(string id, [FromQuery] bool isStarred)
        {
            if (!TryGetUser(out var userId)) return Unauthorized();
            await _emailService.ToggleStarAsync(id, isStarred, userId);
            return Ok();
        }

        [HttpPut("/api/email/{id}/read")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleRead(string id, [FromQuery] bool isRead)
        {
            if (!TryGetUser(out var userId)) return Unauthorized();
            await _emailService.ToggleReadAsync(id, isRead, userId);
            return Ok();
        }

        [HttpPut("/api/email/{id}/label")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignLabel(string id, [FromQuery] string labelId)
        {
            if (!TryGetUser(out var userId)) return Unauthorized();
            await _emailService.AssignLabelAsync(id, labelId, userId);
            return Ok();
        }

        [HttpGet("/api/email/labels")]
        public async Task<IActionResult> GetLabels()
        {
            if (!TryGetUser(out var userId)) return Unauthorized();
            return Ok(await _emailService.GetLabelsAsync(userId));
        }

        [HttpPost("/api/email/labels")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddLabel([FromBody] Label label)
        {
            if (!TryGetUser(out var userId)) return Unauthorized();
            label.UserId = userId;
            if (!string.IsNullOrEmpty(label.ColorClass)) label.ColorClass = label.ColorClass.ToLowerInvariant();
            await _emailService.AddLabelAsync(label);
            return Ok();
        }

        [HttpPut("/api/email/labels/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLabel(string id, [FromBody] Label label)
        {
            if (!TryGetUser(out var userId)) return Unauthorized();
            label.Id = id;
            label.UserId = userId;
            if (!string.IsNullOrEmpty(label.ColorClass)) label.ColorClass = label.ColorClass.ToLowerInvariant();
            await _emailService.UpdateLabelAsync(label);
            return Ok();
        }

        [HttpDelete("/api/email/labels/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLabel(string id)
        {
            if (!TryGetUser(out var userId)) return Unauthorized();
            await _emailService.DeleteLabelAsync(id, userId);
            return Ok();
        }
    }
}
