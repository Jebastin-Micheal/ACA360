using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using ACA360.Core.Constants;
using Microsoft.AspNetCore.Authorization;
namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.InternalTeam)]
    // Inherit from Controller instead of ControllerBase
    public class EmailController : BaseController
    {
        private readonly IEmailService _emailService;
        private string _connectionString;

        public EmailController(IEmailService emailService)
        {
            _emailService = emailService;
        }

        // Standard MVC Route: /Email/Index
        public IActionResult Index()
        {
            var userId = GetCurrentUserId();
            ViewBag.UserId = userId;
            return View();
        }

        // --- API ENDPOINTS ---

        [HttpGet("/api/email/inbox")]
        public async Task<IActionResult> GetInbox([FromQuery] string userId) => Ok(await _emailService.GetInboxAsync(false, userId));

        [HttpGet("/api/email/trash")]
        public async Task<IActionResult> GetTrash([FromQuery] string userId) => Ok(await _emailService.GetTrashAsync(true, userId));

        [HttpPost("/api/email/compose")]
        public async Task<IActionResult> Compose([FromQuery] string userId, [FromBody] EmailItem email)
        {
            email.UserId = userId;
            await _emailService.ComposeEmailAsync(email);
            return Ok(new { message = "Email saved successfully" });
        }

        [HttpPut("/api/email/{id}/trash")]
        public async Task<IActionResult> MoveToTrash(string id, [FromQuery] string userId)
        {
            await _emailService.MoveToTrashAsync(id, true, userId);
            return Ok();
        }

        [HttpPut("/api/email/{id}/star")]
        public async Task<IActionResult> ToggleStar(string id, [FromQuery] bool isStarred, [FromQuery] string userId)
        {
            await _emailService.ToggleStarAsync(id, isStarred, userId);
            return Ok();
        }

        [HttpPut("/api/email/{id}/read")]
        public async Task<IActionResult> ToggleRead(string id, [FromQuery] bool isRead, [FromQuery] string userId)
        {
            await _emailService.ToggleReadAsync(id, isRead, userId);
            return Ok();
        }

        [HttpPut("/api/email/{id}/label")]
        public async Task<IActionResult> AssignLabel(string id, [FromQuery] string labelId, [FromQuery] string userId)
        {
            await _emailService.AssignLabelAsync(id, labelId, userId);
            return Ok();
        }

        [HttpGet("/api/email/labels")]
        public async Task<IActionResult> GetLabels([FromQuery] string userId) => Ok(await _emailService.GetLabelsAsync(userId));

        [HttpPost("/api/email/labels")]
        public async Task<IActionResult> AddLabel([FromQuery] string userId, [FromBody] Label label)
        {
            label.UserId = userId;
            if (!string.IsNullOrEmpty(label.ColorClass)) label.ColorClass = label.ColorClass.ToLowerInvariant();
            await _emailService.AddLabelAsync(label);
            return Ok();
        }

        [HttpPut("/api/email/labels/{id}")]
        public async Task<IActionResult> UpdateLabel(string id, [FromQuery] string userId, [FromBody] Label label)
        {
            label.Id = id;
            label.UserId = userId;
            if (!string.IsNullOrEmpty(label.ColorClass)) label.ColorClass = label.ColorClass.ToLowerInvariant();
            await _emailService.UpdateLabelAsync(label);
            return Ok();
        }

        [HttpDelete("/api/email/labels/{id}")]
        public async Task<IActionResult> DeleteLabel(string id, [FromQuery] string userId)
        {
            await _emailService.DeleteLabelAsync(id, userId);
            return Ok();
        }
    }
}