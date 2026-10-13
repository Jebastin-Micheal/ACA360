using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces; // Use the Interface
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class NotesController : BaseController
    {
        private readonly INotesService _notesService; // Injected notes service for business logic
        private readonly ILogger<NotesController> _logger; // Injected logger for error and diagnostic logging

        /// <summary>
        /// Initializes the NotesController with required service and logger dependencies.
        /// </summary>
        /// <param name="notesService">Service handling notes-related business logic.</param>
        /// <param name="logger">Logger instance for capturing runtime diagnostics and errors.</param>
        public NotesController(INotesService notesService, ILogger<NotesController> logger)
        {
            _notesService = notesService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves all notes associated with a specific entity type and ID for the current user.
        /// GET: /Notes/GetNotes?type={type}&id={id}
        /// </summary>
        /// <param name="type">The entity type for which notes are being fetched (e.g., "Employer", "Broker").</param>
        /// <param name="id">The unique identifier of the entity whose notes are to be retrieved.</param>
        /// <returns>A JSON list of notes belonging to the specified entity, or an error response on failure.</returns>
        [HttpGet]
        public async Task<IActionResult> GetNotes(string type, int id)
        {
            try
            {
                if (await CheckNoteEntityAccessAsync(type, id) is { } denied) return denied;

                // Resolve the currently authenticated user's ID from the base controller helper
                string userId = GetCurrentUserId();

                // Fetch notes from the service layer using entity type, entity ID, and current user context
                var notes = await _notesService.GetNotesAsync(userId, type, id);

                // sp_GetEntityNotes hides internal notes only from a real Employer user
                // (Role_ID 13), so brokers and View As are handled here.
                if (HidesInternalNotes())
                    notes = notes.Where(n => !n.IsInternal).ToList();

                // Return the notes collection as a JSON response
                return Json(notes);
            }
            catch (ArgumentException ex)
            {
                // Handle invalid argument scenarios — e.g., unsupported entity type or invalid ID
                _logger.LogWarning(ex, "Invalid argument in GetNotes. Type: {Type}, Id: {Id}", type, id);
                return BadRequest("Invalid request parameters.");
            }
            catch (Exception ex)
            {
                // Catch-all for unexpected failures — log full exception details for debugging
                _logger.LogError(ex, "An unexpected error occurred in GetNotes. Type: {Type}, Id: {Id}", type, id);
                return StatusCode(500, "An unexpected error occurred while retrieving notes.");
            }
        }

        /// <summary>
        /// Adds a new note to the specified entity on behalf of the currently authenticated user.
        /// POST: /Notes/AddNote
        /// </summary>
        /// <param name="model">The note payload containing the entity context, message body, and visibility flag.</param>
        /// <returns>200 OK on success, 400 Bad Request for validation failures, or 500 on unexpected errors.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddNote([FromBody] AddNoteModel model)
        {
            try
            {
                // Validate that the note message is not empty or whitespace before processing
                if (string.IsNullOrWhiteSpace(model.Message))
                    return BadRequest("Message required");
                if (string.IsNullOrWhiteSpace(model.EntityType))
                    return BadRequest("Entity type required");

                if (await CheckNoteEntityAccessAsync(model.EntityType, model.EntityId) is { } denied) return denied;

                // Resolve the currently authenticated user's ID from the base controller helper
                string userId = GetCurrentUserId();

                // Delegate note creation to the service layer with all required entity and content details
                await _notesService.AddNoteAsync(
                    userId,
                    model.EntityType,
                    model.EntityId,
                    model.Message,
                    model.IsInternal && !HidesInternalNotes()   // employers and brokers cannot write staff-only notes
                );

                // Return 200 OK to indicate the note was successfully persisted
                return Ok();
            }
            catch (ArgumentException ex)
            {
                // Handle validation-level failures raised by the service layer (e.g., invalid entity type)
                _logger.LogWarning(ex, "Validation error in AddNote for EntityType: {EntityType}, EntityId: {EntityId}", model?.EntityType, model?.EntityId);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // Catch-all for unexpected failures — log full exception details for debugging
                _logger.LogError(ex, "An unexpected error occurred in AddNote for EntityType: {EntityType}, EntityId: {EntityId}", model?.EntityType, model?.EntityId);
                return StatusCode(500, "An unexpected error occurred while adding the note.");
            }
        }

        /// <summary>
        /// Retrieves categorized notes for an entity, with optional category/year/author/text filters.
        /// GET: /Notes/GetNotesByEntity?entityType={entityType}&entityId={entityId}
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetNotesByEntity(string entityType, int entityId, int? year, string category, int? userId, string search)
        {
            try
            {
                if (await CheckNoteEntityAccessAsync(entityType, entityId) is { } denied) return denied;

                var filter = new NoteFilterModel
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    FilingYear = year,
                    Category = category,
                    CreatedByUserId = userId,
                    SearchText = search
                };

                string currentUserId = GetCurrentUserId();
                var notes = await _notesService.GetNotesByEntityAsync(filter, currentUserId);

                // sp_GetNotesByEntity returns internal notes to everyone.
                if (HidesInternalNotes())
                    notes = notes.Where(n => !n.IsInternal).ToList();

                return Json(notes);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid argument in GetNotesByEntity. EntityType: {EntityType}, EntityId: {EntityId}", entityType, entityId);
                return BadRequest("Invalid request parameters.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred in GetNotesByEntity. EntityType: {EntityType}, EntityId: {EntityId}", entityType, entityId);
                return StatusCode(500, "An unexpected error occurred while retrieving notes.");
            }
        }

        /// <summary>
        /// Returns the distinct authors with notes on an entity, for populating the user-filter dropdown.
        /// GET: /Notes/GetNoteAuthors?entityType={entityType}&entityId={entityId}
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetNoteAuthors(string entityType, int entityId)
        {
            try
            {
                if (await CheckNoteEntityAccessAsync(entityType, entityId) is { } denied) return denied;

                // sp_GetNoteAuthorsByEntity counts internal notes too, which would name the
                // staff who wrote them; for employers and brokers the list comes from the visible notes.
                if (HidesInternalNotes())
                {
                    var visible = await _notesService.GetNotesByEntityAsync(
                        new NoteFilterModel { EntityType = entityType, EntityId = entityId }, GetCurrentUserId());
                    return Json(visible
                        .Where(n => !n.IsInternal)
                        .GroupBy(n => n.CreatedByUserId)
                        .Select(g => new NoteAuthorModel { UserId = g.Key, UserName = g.First().CreatedByName })
                        .OrderBy(a => a.UserName)
                        .ToList());
                }

                var authors = await _notesService.GetNoteAuthorsAsync(entityType, entityId);
                return Json(authors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred in GetNoteAuthors. EntityType: {EntityType}, EntityId: {EntityId}", entityType, entityId);
                return StatusCode(500, "An unexpected error occurred while retrieving note authors.");
            }
        }

        /// <summary>
        /// Inserts or updates a categorized note for an entity.
        /// POST: /Notes/SaveNote
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserRoles.InternalTeam)]
        public async Task<IActionResult> SaveNote(SaveNoteModel model)
        {
            try
            {
                if (model == null)
                    return BadRequest("Note payload is required.");

                if (string.IsNullOrWhiteSpace(model.Category))
                    return BadRequest("Category is required.");

                if (string.IsNullOrWhiteSpace(model.NoteText))
                    return BadRequest("Note text is required.");

                // A new note is checked against the entity it is being attached to; an
                // edit against the note's own entity, since sp_SaveNote updates by NoteId
                // and ignores the EntityType / EntityId sent with it.
                var denied = model.NoteId > 0
                    ? await CheckNoteAccessAsync(model.NoteId)
                    : await CheckNoteEntityAccessAsync(model.EntityType, model.EntityId);
                if (denied != null) return denied;

                int currentUserId = int.TryParse(GetCurrentUserId(), out var uid) ? uid : 0;

                if (model.FilingYear <= 0)
                    model.FilingYear = int.TryParse(GetCurrentFilingYear(), out var sessionYear) ? sessionYear : DateTime.Now.Year;

                var noteId = await _notesService.SaveNoteAsync(model, currentUserId);
                return Json(new { success = true, noteId });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation error in SaveNote for EntityType: {EntityType}, EntityId: {EntityId}", model?.EntityType, model?.EntityId);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred in SaveNote for EntityType: {EntityType}, EntityId: {EntityId}", model?.EntityType, model?.EntityId);
                return StatusCode(500, "An unexpected error occurred while saving the note.");
            }
        }

        /// <summary>
        /// Soft-deletes a categorized note.
        /// POST: /Notes/DeleteNoteById
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserRoles.InternalTeam)]
        public async Task<IActionResult> DeleteNoteById(int id)
        {
            try
            {
                if (id <= 0)
                    return BadRequest("Invalid note id.");

                if (await CheckNoteAccessAsync(id) is { } denied) return denied;

                int currentUserId = int.TryParse(GetCurrentUserId(), out var uid) ? uid : 0;
                await _notesService.DeleteNoteAsync(id, currentUserId);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred in DeleteNoteById for NoteId: {NoteId}", id);
                return StatusCode(500, "An unexpected error occurred while deleting the note.");
            }
        }

        /// <summary>
        /// Flips the pinned state of a note (e.g. a client-confirmed fact that shouldn't be re-asked every year).
        /// POST: /Notes/TogglePin
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserRoles.InternalTeam)]
        public async Task<IActionResult> TogglePin(int id)
        {
            try
            {
                if (id <= 0)
                    return BadRequest("Invalid note id.");

                if (await CheckNoteAccessAsync(id) is { } denied) return denied;

                int currentUserId = int.TryParse(GetCurrentUserId(), out var uid) ? uid : 0;
                var isPinned = await _notesService.ToggleNotePinAsync(id, currentUserId);
                return Json(new { success = true, isPinned });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred in TogglePin for NoteId: {NoteId}", id);
                return StatusCode(500, "An unexpected error occurred while pinning the note.");
            }
        }

        /// <summary>
        /// Internal notes are staff-only: Employer and Broker users (the roles outside
        /// UserRoles.InternalTeam) do not see them or write them. Uses the effective role,
        /// so an admin viewing as an employer or broker sees what they see.
        /// </summary>
        private bool HidesInternalNotes() =>
            string.Equals(GetEffectiveRole(), UserRoles.Employer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(GetEffectiveRole(), UserRoles.Broker, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Employer scope for a note's entity, which comes from the client: null when the
        /// request may reach it, otherwise the result to return. Each type resolves to its
        /// employer (an employee or dependent through CheckEmployeeAccessAsync). Types are
        /// compared case-insensitively because the procedures compare them that way; any
        /// other type is refused, so a new one has to be added here before it works.
        /// </summary>
        private async Task<IActionResult?> CheckNoteEntityAccessAsync(string? entityType, int entityId)
        {
            if (entityId <= 0)
                return BadRequest(new { success = false, message = "Invalid note entity." });

            if (string.Equals(entityType, NoteEntityType.Employee, StringComparison.OrdinalIgnoreCase))
                return await CheckEmployeeAccessAsync(entityId);

            if (string.Equals(entityType, NoteEntityType.Dependent, StringComparison.OrdinalIgnoreCase))
            {
                var employeeId = await _notesService.GetEmployeeIdForDependentAsync(entityId);
                return employeeId == null
                    ? NotFound(new { success = false, message = "Dependent not found." })
                    : await CheckEmployeeAccessAsync(employeeId.Value);
            }

            long employerId;
            if (string.Equals(entityType, NoteEntityType.Employer, StringComparison.OrdinalIgnoreCase))
            {
                employerId = entityId;
            }
            else if (string.Equals(entityType, NoteEntityType.Plan, StringComparison.OrdinalIgnoreCase))
            {
                var planEmployerId = await _notesService.GetEmployerIdForPlanAsync(entityId);
                if (planEmployerId == null)
                    return NotFound(new { success = false, message = "Plan not found." });
                employerId = planEmployerId.Value;
            }
            else
            {
                return BadRequest(new { success = false, message = "Unsupported note type." });
            }

            if (!await IsEmployerPermittedAsync(employerId))
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "You do not have access to these notes." });

            return null;
        }

        /// <summary>
        /// For actions that take a note id: the scope check runs on the entity the note is
        /// actually attached to, not on anything sent with the request.
        /// </summary>
        private async Task<IActionResult?> CheckNoteAccessAsync(int noteId)
        {
            var note = await _notesService.GetNoteEntityAsync(noteId);
            if (note == null)
                return NotFound(new { success = false, message = "Note not found." });

            return await CheckNoteEntityAccessAsync(note.EntityType, note.EntityId);
        }
    }
}