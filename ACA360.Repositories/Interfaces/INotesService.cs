using ACA360.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface INotesService
    {
        /// <summary>
        /// Retrieves all notes for a specific entity (File, Employer, etc.) visible to the current user.
        /// </summary>
        Task<List<NoteViewModel>> GetNotesAsync(string userId, string entityType, int entityId);

        /// <summary>
        /// Adds a new note to the system.
        /// </summary>
        Task AddNoteAsync(string userId, string entityType, int entityId, string message, bool isInternal);

        /// <summary>
        /// Retrieves notes for an entity with category/year/author/text filtering.
        /// </summary>
        Task<List<NoteModel>> GetNotesByEntityAsync(NoteFilterModel filter, string currentUserId);

        /// <summary>
        /// Inserts or updates a categorized note for an entity.
        /// </summary>
        Task<int> SaveNoteAsync(SaveNoteModel model, int currentUserId);

        /// <summary>
        /// Soft-deletes a note.
        /// </summary>
        Task DeleteNoteAsync(int noteId, int currentUserId);

        /// <summary>
        /// Returns the distinct authors who have notes against an entity, for filter dropdowns.
        /// </summary>
        Task<List<NoteAuthorModel>> GetNoteAuthorsAsync(string entityType, int entityId);

        /// <summary>
        /// Flips the pinned state of a note. Returns the resulting IsPinned value.
        /// </summary>
        Task<bool> ToggleNotePinAsync(int noteId, int currentUserId);

        /// <summary>
        /// The entity a note is attached to (EntityType / EntityId set), or null when the
        /// note does not exist. Edit, delete and pin act on the note id alone.
        /// </summary>
        Task<NoteModel?> GetNoteEntityAsync(int noteId);

        /// <summary>The employer an EmployerPlan row belongs to, or null.</summary>
        Task<long?> GetEmployerIdForPlanAsync(int planId);

        /// <summary>The employee a CoveredIndividual (dependent) row belongs to, or null.</summary>
        Task<int?> GetEmployeeIdForDependentAsync(int coveredIndividualId);
    }
}