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
    }
}