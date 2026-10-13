using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IFileUploadLogService
    {
        Task<int> CreateLogAsync(UploadedFileLog log);

        // This was missing but is required for your Controller's Accept/Approve logic
        Task UpdateLogAsync(UploadedFileLog log);

        Task AddErrorsAsync(int fileLogId, List<string> errors);
        Task<UploadedFileLog?> GetLogByIdAsync(int fileLogId);
        Task<IEnumerable<FileUploadError>> GetErrorsByFileLogIdAsync(int fileLogId);
        // Interface definition
        Task<IEnumerable<UploadedFileLog>> GetLogsByIdsAsync(IEnumerable<int> fileLogIds);

        /// <summary>
        /// The ids among <paramref name="fileLogIds"/> this user may open: the same rule the File
        /// Upload list applies (dbo.fn_FileVisibleToUser, script 041).
        /// </summary>
        Task<HashSet<int>> GetVisibleFileIdsAsync(IEnumerable<int> fileLogIds, int userId);
        Task SoftDeleteLogAsync(int fileLogId);
        Task<(IEnumerable<UploadedFileLog> Logs, int TotalCount)> SearchPaginatedAsync(string searchTerm, int pageSize, int pageNumber, string userId, string sortColumn, string sortOrder, string filterType = "All");
        // Update signature to include WHO and WHY
        // Update this line in your Interface
        Task UpdateStatusAsync(int fileLogId, int? validationStatusId, int? workflowStatusId, string? userId, string action, string? message = null);

        /// <summary>Progress shown on the File Upload page while a file is checked or validated. Nulls clear it.</summary>
        Task UpdateProgressAsync(int fileLogId, string? step, decimal? percent);
        Task<List<FileLifecycleHistory>> GetLifecycleHistoryAsync(int fileLogId);
        Task<UploadedFileLog?> GetFileByHashAsync(string hash);
        Task<PagedResult<UploadedFileLog>> SearchPaginatedAsync(
        string searchTerm,
        int pageSize,
        int pageNumber,
        string userId,
        string sortColumn,
        string sortOrder,
        string filterType,
        DateTime? dateFrom,
        DateTime? dateTo,
        bool showMyFiles,
        string viewMode = "Active" // <--- ADD THIS
    );
        // 2. Add HardDeleteLogAsync
        Task HardDeleteLogAsync(int fileLogId); // <--- ADD THIS
        byte[] GenerateValidationReport(int fileLogId);
    }
}