using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq; // Added for ToList()
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class FileUploadLogRepository : IFileUploadLogService
    {
        private readonly string _connectionString;
        public FileUploadLogRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Inserts a new file upload log record via <c>sp_CreateFileLog</c> and
        /// returns the newly generated <c>FileLogId</c>. Captures all audit and
        /// workflow fields introduced in the FIX revision: WorkflowStatusId,
        /// ValidationStatusId, AssignedToUserId, and FileHash. Returns 0 on any
        /// error so the caller can treat a zero ID as a failed insert without
        /// crashing the upload pipeline.
        /// </summary>
        /// <param name="log">The <see cref="UploadedFileLog"/> model containing all fields to persist.</param>
        /// <returns>The new integer <c>FileLogId</c>, or 0 on error.</returns>
        public async Task<int> CreateLogAsync(UploadedFileLog log)
        {
            try
            {
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        try
                        {
                            // FIX: Added WorkflowStatusId, ValidationStatusId, and AssignedToUserId
                            var parameters = new
                            {
                                log.OriginalFileName,
                                log.StoredFileName,
                                log.FilePath,
                                log.UploadedByUserId,
                                log.EmployerName,
                                log.TaxID,
                                log.ImportTemplateId,
                                // New Fields
                                log.WorkflowStatusId,
                                log.ValidationStatusId,
                                log.AssignedToUserId,
                                log.FileHash,
                                log.PlanYear
                            };

                            int newId = await db.QuerySingleAsync<int>(
                                "sp_CreateFileLog",
                                parameters,
                                transaction: transaction,
                                commandType: CommandType.StoredProcedure
                            );

                            transaction.Commit();
                            return newId;
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log exception here if you have a logger
                return 0;
            }
        }
        
        /// <summary>
        /// Returns an existing file log record whose SHA-256 file hash matches
        /// <paramref name="hash"/> via <c>sp_GetFileByHash</c>. Used during
        /// upload processing to detect duplicate files before persisting a new
        /// log entry. Returns <c>null</c> when no match is found or on error.
        /// </summary>
        /// <param name="hash">The SHA-256 hex hash string of the file to look up.</param>
        /// <returns>The matching <see cref="UploadedFileLog"/>, or <c>null</c> if not found.</returns>
        public async Task<UploadedFileLog> GetFileByHashAsync(string hash)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QuerySingleOrDefaultAsync<UploadedFileLog>(
                        "sp_GetFileByHash",
                        new { FileHash = hash },
                        commandType: CommandType.StoredProcedure
                    );
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving file by hash '{hash}'.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving file by hash '{hash}'.", ex);
            }
        }

        /// <summary>
        /// Updates the mutable fields of an existing file log record via
        /// <c>sp_UpdateFileLog</c>. Covers workflow status, validation status,
        /// assignment, soft-delete flag, archive flag, stored file name, and
        /// file path. A transaction is used so all column updates within the SP
        /// are committed atomically or rolled back on failure.
        /// </summary>
        /// <param name="log">
        /// The <see cref="UploadedFileLog"/> whose <c>FileLogId</c> identifies
        /// the target row and whose other properties supply the updated values.
        /// </param>
        public async Task UpdateLogAsync(UploadedFileLog log)
        {
            try
            {
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        try
                        {
                            var parameters = new
                            {
                                log.FileLogId,
                                log.WorkflowStatusId,
                                log.ValidationStatusId,
                                log.AssignedToUserId,
                                log.IsDeleted,
                                log.IsArchived,
                                log.StoredFileName,
                                log.FilePath
                            };

                            await db.ExecuteAsync(
                                "sp_UpdateFileLog",
                                parameters,
                                transaction: transaction,
                                commandType: CommandType.StoredProcedure
                            );

                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error updating file log for FileLogId {log.FileLogId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error updating file log for FileLogId {log.FileLogId}.", ex);
            }
        }

        /// <summary>
        /// Updates the validation and workflow status of a file log record and
        /// appends an audit trail entry in a single call to
        /// <c>sp_UpdateFileLogStatus</c>. Either or both status IDs may be
        /// <c>null</c> to leave the corresponding column unchanged on the
        /// database side. The <paramref name="action"/> and optional
        /// <paramref name="message"/> parameters are stored in the audit log
        /// by the SP. A transaction ensures the status update and audit entry
        /// are written atomically.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry to update.</param>
        /// <param name="validationStatusId">New validation status ID, or <c>null</c> to leave unchanged.</param>
        /// <param name="workflowStatusId">New workflow status ID, or <c>null</c> to leave unchanged.</param>
        /// <param name="userId">The ID of the user performing the status change (audit).</param>
        /// <param name="action">A short action label written to the audit log (e.g. "Validate", "Import").</param>
        /// <param name="message">Optional free-text message stored alongside the audit entry.</param>
        public async Task UpdateStatusAsync(int fileLogId, int? validationStatusId, int? workflowStatusId, string userId, string action, string message = null)
        {
            try
            {
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        try
                        {
                            var parameters = new
                            {
                                FileLogId = fileLogId,
                                ValidationStatusId = validationStatusId,
                                WorkflowStatusId = workflowStatusId,
                                // Audit Params
                                UserId = userId,
                                Action = action,
                                Message = message
                            };

                            await db.ExecuteAsync(
                                "sp_UpdateFileLogStatus",
                                parameters,
                                transaction: transaction,
                                commandType: CommandType.StoredProcedure
                            );

                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error updating status for FileLogId {fileLogId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error updating status for FileLogId {fileLogId}.", ex);
            }
        }

        /// <summary>
        /// Persists the reporting period start date, end date, and derived plan
        /// year for a file log record via <c>sp_UpdateFileLogPeriod</c>. The
        /// plan year is derived from the year component of
        /// <paramref name="start"/> when that value is non-null; otherwise it
        /// is stored as <c>null</c>. A transaction guards the UPDATE so it can
        /// be rolled back if the SP raises an error.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry to update.</param>
        /// <param name="start">The reporting period start date (nullable).</param>
        /// <param name="end">The reporting period end date (nullable).</param>
        public async Task UpdatePeriodAsync(int fileLogId, DateTime? start, DateTime? end)
        {
            try
            {
                int? planYear = start.HasValue ? start.Value.Year : null;

                // Replaced inline UPDATE with sp_UpdateFileLogPeriod stored procedure
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        try
                        {
                            await db.ExecuteAsync(
                                "sp_UpdateFileLogPeriod",
                                new
                                {
                                    Start = start,
                                    End = end,
                                    PlanYear = planYear,
                                    Id = fileLogId
                                },
                                transaction: transaction,
                                commandType: CommandType.StoredProcedure);

                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error updating period for FileLogId {fileLogId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error updating period for FileLogId {fileLogId}.", ex);
            }
        }

        /// <summary>
        /// Bulk-inserts a list of validation error messages for a file log entry
        /// via <c>sp_AddFileErrors</c>. The error strings are packed into a
        /// <see cref="DataTable"/> and passed to the SP as a table-valued
        /// parameter of type <c>ut_ErrorList</c>, allowing the SP to INSERT all
        /// rows in a single server round-trip. A transaction is used so the batch
        /// INSERT is atomic — a partial failure does not leave orphaned error rows.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry to attach errors to.</param>
        /// <param name="errors">A list of error message strings to insert.</param>
        public async Task AddErrorsAsync(int fileLogId, List<string> errors)
        {
            try
            {
                var errorTable = new DataTable();
                errorTable.Columns.Add("ErrorMessage", typeof(string));
                foreach (var error in errors)
                {
                    errorTable.Rows.Add(error);
                }

                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        try
                        {
                            await db.ExecuteAsync(
                                "sp_AddFileErrors",
                                new
                                {
                                    FileLogId = fileLogId,
                                    ErrorMessages = errorTable.AsTableValuedParameter("ut_ErrorList")
                                },
                                transaction: transaction,
                                commandType: CommandType.StoredProcedure);

                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error adding errors for FileLogId {fileLogId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error adding errors for FileLogId {fileLogId}.", ex);
            }
        }

        /// <summary>
        /// Returns a single file log record by its primary key via
        /// <c>sp_GetFileLogById</c>. Returns <c>null</c> when no matching
        /// record is found. Used by the controller layer before performing
        /// status updates or file operations that require the full log context.
        /// </summary>
        /// <param name="fileLogId">The primary-key ID of the file log entry.</param>
        /// <returns>The <see cref="UploadedFileLog"/>, or <c>null</c> if not found.</returns>
        public async Task<UploadedFileLog> GetLogByIdAsync(int fileLogId)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QuerySingleOrDefaultAsync<UploadedFileLog>(
                        "sp_GetFileLogById",
                        new { FileLogId = fileLogId },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving file log for FileLogId {fileLogId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving file log for FileLogId {fileLogId}.", ex);
            }
        }
        /// <summary>
        /// Fetches multiple file logs in a single query to prevent N+1 database calls during polling.
        /// </summary>
        public async Task<IEnumerable<UploadedFileLog>> GetLogsByIdsAsync(IEnumerable<int> fileLogIds)
        {
            if (fileLogIds == null || !fileLogIds.Any()) return Enumerable.Empty<UploadedFileLog>();

            try
            {
                using var db = Connection;
                // The table is UploadedFileLog. This read previously named
                // tbl_UploadedFileLog -- a placeholder that was never corrected -- so
                // every call threw "Invalid object name". PollStatus is the only
                // caller and the poller swallows non-OK responses, which is why the
                // failure was silent rather than visible.
                return await db.QueryAsync<UploadedFileLog>(
                    "SELECT * FROM UploadedFileLog WHERE FileLogId IN @Ids AND IsDeleted = 0",
                    new { Ids = fileLogIds }
                );
            }
            catch (Exception ex)
            {
                throw new Exception("Database error retrieving multiple file logs by IDs.", ex);
            }
        }
        /// <summary>
        /// Returns all file log records in the system (no filtering) via
        /// <c>sp_GetAllFileLogs</c>. Intended for administrative views that
        /// require unrestricted visibility. Returns an empty enumerable on error.
        /// </summary>
        /// <returns>An <see cref="IEnumerable{UploadedFileLog}"/> of all log records.</returns>
        public async Task<IEnumerable<UploadedFileLog>> GetAllLogsAsync()
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QueryAsync<UploadedFileLog>(
                        "sp_GetAllFileLogs",
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (SqlException ex)
            {
                throw new Exception("Database error retrieving all file logs.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception("Unexpected error retrieving all file logs.", ex);
            }
        }

        /// <summary>
        /// Returns all file log records uploaded by a specific user via
        /// <c>sp_GetFileLogsByUserId</c>. Used to populate a user's personal
        /// upload history view. Returns an empty enumerable on error.
        /// </summary>
        /// <param name="userId">The ID of the user whose uploads should be returned.</param>
        /// <returns>An <see cref="IEnumerable{UploadedFileLog}"/> for the given user.</returns>
        public async Task<IEnumerable<UploadedFileLog>> GetLogsByUserIdAsync(string userId)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QueryAsync<UploadedFileLog>(
                        "sp_GetFileLogsByUserId",
                        new { UploadedByUserId = userId },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving file logs for UserId '{userId}'.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving file logs for UserId '{userId}'.", ex);
            }
        }

        /// <summary>
        /// Returns all validation error records associated with a given file log
        /// entry via <c>sp_GetFileErrorsByFileLogId</c>. Used to display the
        /// full error list on the file detail page. Returns an empty enumerable
        /// on error.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry whose errors should be returned.</param>
        /// <returns>An <see cref="IEnumerable{FileUploadError}"/> of error records.</returns>
        public async Task<IEnumerable<FileUploadError>> GetErrorsByFileLogIdAsync(int fileLogId)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QueryAsync<FileUploadError>(
                        "sp_GetFileErrorsByFileLogId",
                        new { FileLogId = fileLogId },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving errors for FileLogId {fileLogId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving errors for FileLogId {fileLogId}.", ex);
            }
        }

        /// <summary>
        /// Soft-deletes a file log record by setting its <c>IsDeleted</c> flag
        /// and recording the deletion timestamp via <c>sp_SoftDeleteFileLog</c>.
        /// The record remains in the database and can be recovered; only active
        /// queries that filter on <c>IsDeleted = 0</c> will exclude it. A
        /// transaction is used so the UPDATE can be rolled back on failure.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry to soft-delete.</param>
        public async Task SoftDeleteLogAsync(int fileLogId)
        {
            try
            {
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        try
                        {
                            await db.ExecuteAsync(
                                "sp_SoftDeleteFileLog",
                                new { FileLogId = fileLogId, DeletedAt = DateTime.UtcNow },
                                transaction: transaction,
                                commandType: CommandType.StoredProcedure);

                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error soft-deleting FileLogId {fileLogId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error soft-deleting FileLogId {fileLogId}.", ex);
            }
        }

        /// <summary>
        /// Returns a simplified list of validation errors for a given file log
        /// entry via <c>sp_GetSimpleErrorsByFileLogId</c>. Distinguished from
        /// <see cref="GetErrorsByFileLogIdAsync"/> in that the SP returns a
        /// reduced column set suitable for summary widgets rather than the full
        /// error detail view. Returns an empty enumerable on error.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry whose simplified errors should be returned.</param>
        /// <returns>An <see cref="IEnumerable{FileUploadError}"/> with summary-level error data.</returns>
        public async Task<IEnumerable<FileUploadError>> GetSimpleErrorsByFileLogIdAsync(int fileLogId)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QueryAsync<FileUploadError>(
                        "sp_GetSimpleErrorsByFileLogId",
                        new { FileLogId = fileLogId },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving simple errors for FileLogId {fileLogId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving simple errors for FileLogId {fileLogId}.", ex);
            }
        }

        /// <summary>
        /// Returns a paginated, sorted, and optionally filtered page of file log
        /// records alongside the total matching row count via
        /// <c>sp_SearchFileLogsPaginated</c> (basic overload). The SP returns
        /// two result sets: the log rows and a single integer count used for
        /// pagination metadata. Null-coalesces blank search and userId strings
        /// before passing them to the SP so the SP can treat NULL as "no filter".
        /// Returns empty results on error.
        /// </summary>
        /// <param name="searchTerm">Optional text to filter on file name, employer, or TaxID.</param>
        /// <param name="pageSize">Number of records per page.</param>
        /// <param name="pageNumber">1-based page index.</param>
        /// <param name="userId">Optional user ID to scope results to a specific uploader.</param>
        /// <param name="sortColumn">Column name to sort by.</param>
        /// <param name="sortOrder">"asc" or "desc".</param>
        /// <param name="filterType">Status filter token (default "All").</param>
        /// <returns>A tuple of the log page and total matching row count.</returns>
        public async Task<(IEnumerable<UploadedFileLog> Logs, int TotalCount)> SearchPaginatedAsync(
            string searchTerm, int pageSize, int pageNumber, string userId, string sortColumn, string sortOrder, string filterType = "All")
        {
            try
            {
                using var db = Connection;
                var parameters = new
                {
                    SearchTerm = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm,
                    PageSize = pageSize,
                    PageNumber = pageNumber,
                    UserId = string.IsNullOrWhiteSpace(userId) ? null : userId,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder,
                    FilterType = filterType
                };

                using var multi = await db.QueryMultipleAsync(
                    "sp_SearchFileLogsPaginated",
                    parameters,
                    commandType: CommandType.StoredProcedure
                );

                var logs = await multi.ReadAsync<UploadedFileLog>();
                var count = await multi.ReadSingleAsync<int>();
                return (logs, count);
            }
            catch (SqlException ex)
            {
                throw new Exception("Database error during paginated file log search.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception("Unexpected error during paginated file log search.", ex);
            }
        }

        /// <summary>
        /// Sets the <c>AssignedToUserId</c> column on a file log record via
        /// <c>sp_AssignFileToUser</c>. Used by the workflow engine to route a
        /// file to a specific data analyst or reviewer. A transaction guards the
        /// UPDATE so it can be rolled back on failure.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry to assign.</param>
        /// <param name="userId">The ID of the user to assign the file to.</param>
        public async Task AssignFileToUserAsync(int fileLogId, string userId)
        {
            try
            {
                // Replaced inline UPDATE with sp_AssignFileToUser stored procedure
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        try
                        {
                            await db.ExecuteAsync(
                                "sp_AssignFileToUser",
                                new { UserId = userId, FileLogId = fileLogId },
                                transaction: transaction,
                                commandType: CommandType.StoredProcedure);

                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error assigning FileLogId {fileLogId} to UserId '{userId}'.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error assigning FileLogId {fileLogId} to UserId '{userId}'.", ex);
            }
        }

        /// <summary>
        /// Returns up to 20 pending file log records relevant to a given user
        /// via <c>sp_GetPendingFilesForUser</c>. "Pending" is defined as records
        /// where the user is either the uploader or the assigned reviewer, the
        /// ValidationStatusId is 0, 1, or 2 (not yet validated or failed), and
        /// the record has not been soft-deleted. Results are ordered by
        /// ValidationStatusId descending then upload time ascending, surfacing
        /// the most urgent items first. Returns an empty list on error.
        /// </summary>
        /// <param name="userId">The ID of the user whose pending files should be returned.</param>
        /// <returns>A <see cref="List{UploadedFileLog}"/> of up to 20 pending records.</returns>
        public async Task<List<UploadedFileLog>> GetPendingFilesForUserAsync(string userId)
        {
            try
            {
                // Replaced inline SQL with sp_GetPendingFilesForUser stored procedure
                using var db = Connection;
                var result = await db.QueryAsync<UploadedFileLog>(
                    "sp_GetPendingFilesForUser",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure);
                return result.ToList();
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving pending files for UserId '{userId}'.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving pending files for UserId '{userId}'.", ex);
            }
        }

        /// <summary>
        /// Returns the full lifecycle history for a file log entry via
        /// <c>sp_GetFileLifecycleHistory</c>, ordered by timestamp descending
        /// so the most recent event appears first. Used to populate the history
        /// timeline on the file detail page. Returns an empty list on error.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry whose lifecycle history is needed.</param>
        /// <returns>A <see cref="List{FileLifecycleHistory}"/> ordered newest-first.</returns>
        public async Task<List<FileLifecycleHistory>> GetLifecycleHistoryAsync(int fileLogId)
        {
            try
            {
                // Replaced inline SQL with sp_GetFileLifecycleHistory stored procedure
                using (var db = Connection)
                {
                    return (await db.QueryAsync<FileLifecycleHistory>(
                        "sp_GetFileLifecycleHistory",
                        new { Id = fileLogId },
                        commandType: CommandType.StoredProcedure)).ToList();
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving lifecycle history for FileLogId {fileLogId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving lifecycle history for FileLogId {fileLogId}.", ex);
            }
        }

        /// <summary>
        /// Returns the four dashboard metric counts (TotalUploads, ActionRequired,
        /// Processing, Completed) scoped to the current calendar month via
        /// <c>sp_GetFileDashboardStats</c>. Each counter is derived from a
        /// combination of ValidationStatusId and WorkflowStatusId ranges as
        /// defined in the SP. Soft-deleted records are excluded. Returns a
        /// default empty DTO on error so the dashboard still renders without
        /// crashing.
        /// </summary>
        /// <param name="userId">
        /// The ID of the requesting user. Passed to the SP for potential
        /// non-admin scoping (currently SP returns global month stats;
        /// see commented-out per-user filter in the SP body).
        /// </param>
        /// <returns>A <see cref="fileDashboardStatsDto"/> with the four metric values.</returns>
        public async Task<fileDashboardStatsDto> GetFileDashboardStatsAsync(string userId)
        {
            try
            {
                // Replaced inline SQL with sp_GetFileDashboardStats stored procedure
                using var db = Connection;
                var result = await db.QueryFirstOrDefaultAsync<fileDashboardStatsDto>(
                    "sp_GetFileDashboardStats",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure);
                return result ?? new fileDashboardStatsDto();
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving dashboard stats for UserId '{userId}'.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving dashboard stats for UserId '{userId}'.", ex);
            }
        }

        /// <summary>
        /// Returns a paginated, sorted, date-range-filtered, and view-mode-aware
        /// page of file log records alongside the total matching row count via
        /// <c>sp_SearchFileLogsPaginated</c> (extended overload). Extends the
        /// basic <see cref="SearchPaginatedAsync(string,int,int,string,string,string,string)"/>
        /// with additional parameters: date range (<paramref name="dateFrom"/>,
        /// <paramref name="dateTo"/>), a "show my files only" toggle, and a
        /// view mode ("Active", "Archived", etc.) passed through to the SP.
        /// Returns an empty <see cref="PagedResult{T}"/> on error.
        /// </summary>
        /// <param name="searchTerm">Optional text filter on file name, employer, or TaxID.</param>
        /// <param name="pageSize">Number of records per page.</param>
        /// <param name="pageNumber">1-based page index.</param>
        /// <param name="userId">The requesting user's ID (used for "my files" filter).</param>
        /// <param name="sortColumn">Column name to sort by.</param>
        /// <param name="sortOrder">"asc" or "desc".</param>
        /// <param name="filterType">Status filter token.</param>
        /// <param name="dateFrom">Optional lower bound for <c>UploadedAt</c> filter.</param>
        /// <param name="dateTo">Optional upper bound for <c>UploadedAt</c> filter.</param>
        /// <param name="showMyFiles">When <c>true</c>, restricts results to files uploaded by or assigned to <paramref name="userId"/>.</param>
        /// <param name="viewMode">View mode filter token passed to the SP (default "Active").</param>
        /// <returns>A <see cref="PagedResult{UploadedFileLog}"/> with the log page and total count.</returns>
        public async Task<PagedResult<UploadedFileLog>> SearchPaginatedAsync(
            string searchTerm, int pageSize, int pageNumber, string userId,
            string sortColumn, string sortOrder, string filterType,
            DateTime? dateFrom, DateTime? dateTo, bool showMyFiles,
            string viewMode = "Active")
        {
            try
            {
                using var db = Connection;
                var p = new DynamicParameters();
                p.Add("@SearchTerm", searchTerm);
                p.Add("@PageSize", pageSize);
                p.Add("@PageNumber", pageNumber);
                p.Add("@UserId", userId);
                p.Add("@SortColumn", sortColumn);
                p.Add("@SortOrder", sortOrder);
                p.Add("@FilterType", filterType);
                p.Add("@DateFrom", dateFrom);
                p.Add("@DateTo", dateTo);
                p.Add("@ShowMyFiles", showMyFiles);
                p.Add("@ViewMode", viewMode);

                // Assuming you are using Dapper.GridReader for multiple result sets
                using var multi = await db.QueryMultipleAsync(
                    "sp_SearchFileLogsPaginated",
                    p,
                    commandType: CommandType.StoredProcedure);

                var logs = (await multi.ReadAsync<UploadedFileLog>()).ToList();
                var count = await multi.ReadFirstAsync<int>();

                return new PagedResult<UploadedFileLog>
                {
                    Logs = logs,
                    TotalCount = count
                };
            }
            catch (SqlException ex)
            {
                throw new Exception("Database error during extended paginated file log search.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception("Unexpected error during extended paginated file log search.", ex);
            }
        }

        /// <summary>
        /// Permanently removes a file log record and all its child data from the
        /// database via <c>sp_HardDeleteFileLog</c>. Unlike
        /// <see cref="SoftDeleteLogAsync"/>, this operation is irreversible. A
        /// transaction is used so the DELETE (which may cascade to child tables
        /// inside the SP) can be rolled back if any part of the operation fails.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry to permanently delete.</param>
        public async Task HardDeleteLogAsync(int fileLogId)
        {
            try
            {
                // Replaced inline DELETE with sp_HardDeleteFileLog stored procedure
                using var db = Connection;
                db.Open();
                using var transaction = db.BeginTransaction();
                try
                {
                    await db.ExecuteAsync(
                        "sp_HardDeleteFileLog",
                        new { Id = fileLogId },
                        transaction: transaction,
                        commandType: CommandType.StoredProcedure);

                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error hard-deleting FileLogId {fileLogId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error hard-deleting FileLogId {fileLogId}.", ex);
            }
        }

        /// <summary>
        /// Generates a multi-tab Excel validation report for a given file log
        /// entry. Each tab (Employers, Employees, Dependents, Plans, Premiums)
        /// is populated from the corresponding staging table via
        /// <see cref="GetDataForTable"/>. Cells in the <c>ErrorColumns</c> and
        /// <c>WarningColumns</c> helper columns are used to colour-code
        /// individual data cells (red for errors, yellow for warnings) before
        /// those helper columns are deleted from the output. The
        /// <c>Validation Status</c> column background is also coloured per-row.
        /// Returns the workbook as a raw byte array for streaming to the browser.
        /// Throws on error so the controller can return an appropriate HTTP error
        /// response rather than silently returning an empty file.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry to generate a report for.</param>
        /// <returns>A byte array of the generated <c>.xlsx</c> workbook.</returns>
        public byte[] GenerateValidationReport(int fileLogId)
        {
            try
            {
                // Define the Tabs (Sheets) you want in the report
                var tables = new Dictionary<string, string>
                {
                    { "Employers",  "Staging_Employers"  },
                    { "Employees",  "Staging_Employees"  },
                    { "Dependents", "Staging_Dependents" },
                    { "Plans",      "Staging_Plans"      },
                    { "Premiums",   "Staging_Premiums"   }
                };

                using (var package = new ExcelPackage())
                {
                    foreach (var tab in tables)
                    {
                        var sheetName = tab.Key;
                        var tableName = tab.Value;

                        // 1. Fetch Data from Stored Procedure
                        DataTable data = GetDataForTable(fileLogId, tableName);

                        // 2. Add Worksheet
                        var worksheet = package.Workbook.Worksheets.Add(sheetName);

                        // Ensure we actually have columns before trying to load or format headers
                        if (data.Columns.Count > 0)
                        {
                            // 3. Load Data into Sheet (Starting at A1)
                            worksheet.Cells["A1"].LoadFromDataTable(data, true);

                            // 4. Formatting - Auto-fit and Header Styling
                            worksheet.Cells.AutoFitColumns();

                            using (var headerRange = worksheet.Cells[1, 1, 1, data.Columns.Count])
                            {
                                headerRange.Style.Font.Bold = true;
                                headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                headerRange.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                            }

                            // 5. CELL-LEVEL HIGHLIGHTING (The Advanced UX)
                            int errColIndex = GetColumnIndex(data, "ErrorColumns");
                            int warnColIndex = GetColumnIndex(data, "WarningColumns");
                            int statusColIndex = GetColumnIndex(data, "Validation Status");

                            if (data.Rows.Count > 0)
                            {
                                // Create a dictionary to map Column Names to their Excel Column Numbers
                                var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                                for (int c = 1; c <= data.Columns.Count; c++)
                                {
                                    colMap[worksheet.Cells[1, c].Text] = c;
                                }

                                // Loop through every row of data
                                for (int r = 0; r < data.Rows.Count; r++)
                                {
                                    int excelRow = r + 2; // Data starts on row 2

                                    // 5a. Color the "Validation Status" column based on the overall row health
                                    if (statusColIndex > 0)
                                    {
                                        string status = worksheet.Cells[excelRow, statusColIndex].Text;
                                        if (status == "Failed")
                                        {
                                            worksheet.Cells[excelRow, statusColIndex].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                            worksheet.Cells[excelRow, statusColIndex].Style.Fill.BackgroundColor.SetColor(Color.LightSalmon);
                                        }
                                        else if (status == "Warning")
                                        {
                                            worksheet.Cells[excelRow, statusColIndex].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                            worksheet.Cells[excelRow, statusColIndex].Style.Fill.BackgroundColor.SetColor(Color.LightYellow);
                                        }
                                    }

                                    // 5b. Highlight specific Warning Cells (Yellow)
                                    if (warnColIndex > 0)
                                    {
                                        string warnCols = worksheet.Cells[excelRow, warnColIndex].Text;
                                        if (!string.IsNullOrEmpty(warnCols))
                                        {
                                            foreach (var colName in warnCols.Split(','))
                                            {
                                                if (colMap.TryGetValue(colName.Trim(), out int targetCol))
                                                {
                                                    worksheet.Cells[excelRow, targetCol].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                                    worksheet.Cells[excelRow, targetCol].Style.Fill.BackgroundColor.SetColor(Color.LightYellow);
                                                }
                                            }
                                        }
                                    }

                                    // 5c. Highlight specific Error Cells (Red) - Errors overwrite warnings if both exist on the same cell
                                    if (errColIndex > 0)
                                    {
                                        string errCols = worksheet.Cells[excelRow, errColIndex].Text;
                                        if (!string.IsNullOrEmpty(errCols))
                                        {
                                            foreach (var colName in errCols.Split(','))
                                            {
                                                if (colMap.TryGetValue(colName.Trim(), out int targetCol))
                                                {
                                                    worksheet.Cells[excelRow, targetCol].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                                    worksheet.Cells[excelRow, targetCol].Style.Fill.BackgroundColor.SetColor(Color.LightSalmon);
                                                }
                                            }
                                        }
                                    }
                                }

                                // 6. Clean up: Delete the helper columns so the user doesn't see them!
                                if (warnColIndex > 0) worksheet.DeleteColumn(warnColIndex);
                                // Because we deleted a column, the error column shifts left by 1
                                if (errColIndex > 0) worksheet.DeleteColumn(warnColIndex > 0 && errColIndex > warnColIndex ? errColIndex - 1 : errColIndex);
                            }
                        }
                    }

                    // 6. Return as Byte Array
                    return package.GetAsByteArray();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating validation report for FileLogId {fileLogId}.", ex);
            }
        }

        /// <summary>
        /// Fetches the staging table data for a single validation report tab by
        /// calling <c>sp_GetValidationReport</c> with the file log ID and the
        /// target staging table name. Uses a raw <see cref="SqlDataAdapter"/>
        /// (rather than Dapper) so that the result can be loaded directly into
        /// a <see cref="DataTable"/>, which EPPlus's <c>LoadFromDataTable</c>
        /// requires. The table name parameter is passed to the SP rather than
        /// being interpolated into SQL, eliminating any injection risk. Throws
        /// on error so <see cref="GenerateValidationReport"/> can catch and wrap
        /// the failure with contextual information.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry being reported on.</param>
        /// <param name="tableName">The staging table name to retrieve data from (e.g. "Staging_Employees").</param>
        /// <returns>A <see cref="DataTable"/> populated with the staging data.</returns>
        private DataTable GetDataForTable(int fileLogId, string tableName)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand("sp_GetValidationReport", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@FileLogId", fileLogId);
                    cmd.Parameters.AddWithValue("@TableName", tableName);

                    var dt = new DataTable();
                    var da = new SqlDataAdapter(cmd);

                    conn.Open();
                    da.Fill(dt);
                    return dt;
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error fetching validation data for table '{tableName}', FileLogId {fileLogId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error fetching validation data for table '{tableName}', FileLogId {fileLogId}.", ex);
            }
        }

        /// <summary>
        /// Returns the 1-based Excel column index for a named column within a
        /// <see cref="DataTable"/>. The comparison is case-insensitive. Returns
        /// -1 if the column name is not found, which callers interpret as "this
        /// helper column is absent — skip the corresponding highlight pass".
        /// This method is synchronous and performs no database access.
        /// </summary>
        /// <param name="dt">The <see cref="DataTable"/> whose columns should be searched.</param>
        /// <param name="columnName">The column name to locate (case-insensitive).</param>
        /// <returns>The 1-based column index, or -1 if not found.</returns>
        private int GetColumnIndex(DataTable dt, string columnName)
        {
            try
            {
                for (int i = 0; i < dt.Columns.Count; i++)
                {
                    if (dt.Columns[i].ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                        return i + 1; // Excel is 1-based
                }
                return -1;
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error locating column '{columnName}' in DataTable.", ex);
            }
        }
    }
}