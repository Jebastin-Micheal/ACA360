using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class NotesService : INotesService
    {
        private readonly string _connectionString;
        private readonly ILogger<NotesService> _logger;

        /// <summary>
        /// Purpose: Initializes the NotesService with database connection and logging dependencies.
        /// Input parameters: string connectionString, ILogger logger
        /// Output/return value: None
        /// </summary>
        public NotesService(string connectionString, ILogger<NotesService> logger)
        {
            try
            {
                _connectionString = connectionString;
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "Error occurred during initialization of NotesService.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves all notes associated with a specific entity and user.
        /// Input parameters: string userId, string entityType, int entityId
        /// Output/return value: Task of List of NoteViewModel
        /// </summary>
        public async Task<List<NoteViewModel>> GetNotesAsync(string userId, string entityType, int entityId)
        {
            try
            {
                using var db = Connection;

                var notes = await db.QueryAsync<NoteViewModel>(
                    "sp_GetEntityNotes",
                    new { UserID = userId, EntityType = entityType, EntityID = entityId },
                    commandType: CommandType.StoredProcedure
                );

                return notes.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetNotesAsync for EntityType: {EntityType}, EntityID: {EntityId}", entityType, entityId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Adds a new note to a specific entity with transaction handling.
        /// Input parameters: string userId, string entityType, int entityId, string message, bool isInternal
        /// Output/return value: Task
        /// </summary>
        public async Task AddNoteAsync(string userId, string entityType, int entityId, string message, bool isInternal)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_AddSystemNote",
                    new
                    {
                        UserID = userId,
                        EntityType = entityType,
                        EntityID = entityId,
                        Message = message,
                        IsInternal = isInternal
                    },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure
                );

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in AddNoteAsync for EntityType: {EntityType}, EntityID: {EntityId}", entityType, entityId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves filtered, categorized notes for an entity (Employer/Employee/Plan/Dependent).
        /// </summary>
        public async Task<List<NoteModel>> GetNotesByEntityAsync(NoteFilterModel filter, string currentUserId)
        {
            try
            {
                using var db = Connection;

                var notes = await db.QueryAsync<NoteModel>(
                    "sp_GetNotesByEntity",
                    new
                    {
                        filter.EntityType,
                        filter.EntityId,
                        filter.FilingYear,
                        filter.Category,
                        filter.CreatedByUserId,
                        filter.SearchText
                    },
                    commandType: CommandType.StoredProcedure
                );

                var list = notes.ToList();
                foreach (var note in list)
                {
                    note.IsMine = !string.IsNullOrEmpty(currentUserId) && note.CreatedByUserId.ToString() == currentUserId;
                }

                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetNotesByEntityAsync for EntityType: {EntityType}, EntityID: {EntityId}", filter.EntityType, filter.EntityId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Inserts or updates a categorized note for an entity, with transaction handling.
        /// </summary>
        public async Task<int> SaveNoteAsync(SaveNoteModel model, int currentUserId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var result = await db.QueryFirstOrDefaultAsync<int?>(
                    "sp_SaveNote",
                    new
                    {
                        model.NoteId,
                        model.EntityType,
                        model.EntityId,
                        model.Category,
                        model.NoteText,
                        model.FilingYear,
                        model.IsInternal,
                        UserId = currentUserId
                    },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure
                );

                tx.Commit();
                return result ?? model.NoteId;
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in SaveNoteAsync for EntityType: {EntityType}, EntityID: {EntityId}", model.EntityType, model.EntityId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Soft-deletes a note, with transaction handling.
        /// </summary>
        public async Task DeleteNoteAsync(int noteId, int currentUserId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_DeleteNote",
                    new { NoteId = noteId, UserId = currentUserId },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure
                );

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in DeleteNoteAsync for NoteId: {NoteId}", noteId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Returns distinct authors with notes on an entity, for the user-filter dropdown.
        /// </summary>
        public async Task<List<NoteAuthorModel>> GetNoteAuthorsAsync(string entityType, int entityId)
        {
            try
            {
                using var db = Connection;

                var authors = await db.QueryAsync<NoteAuthorModel>(
                    "sp_GetNoteAuthorsByEntity",
                    new { EntityType = entityType, EntityId = entityId },
                    commandType: CommandType.StoredProcedure
                );

                return authors.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetNoteAuthorsAsync for EntityType: {EntityType}, EntityID: {EntityId}", entityType, entityId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Flips the pinned state of a note, with transaction handling.
        /// </summary>
        public async Task<bool> ToggleNotePinAsync(int noteId, int currentUserId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var isPinned = await db.QueryFirstOrDefaultAsync<bool>(
                    "sp_ToggleNotePin",
                    new { NoteId = noteId, UserId = currentUserId },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure
                );

                tx.Commit();
                return isPinned;
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in ToggleNotePinAsync for NoteId: {NoteId}", noteId);
                throw;
            }
        }
    }
}