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
        Task<UploadedFileLog> GetLogByIdAsync(int fileLogId);
        Task<IEnumerable<UploadedFileLog>> GetAllLogsAsync();
        Task<IEnumerable<UploadedFileLog>> GetLogsByUserIdAsync(string userId);
        Task<IEnumerable<FileUploadError>> GetErrorsByFileLogIdAsync(int fileLogId);
        // Interface definition
        Task<IEnumerable<UploadedFileLog>> GetLogsByIdsAsync(IEnumerable<int> fileLogIds);
        Task SoftDeleteLogAsync(int fileLogId);
        Task<IEnumerable<FileUploadError>> GetSimpleErrorsByFileLogIdAsync(int fileLogId);
        Task<(IEnumerable<UploadedFileLog> Logs, int TotalCount)> SearchPaginatedAsync(string searchTerm, int pageSize, int pageNumber, string userId, string sortColumn, string sortOrder, string filterType = "All");
        Task AssignFileToUserAsync(int fileLogId, string userId);
        Task UpdatePeriodAsync(int fileLogId, DateTime? start, DateTime? end);
        Task<List<UploadedFileLog>> GetPendingFilesForUserAsync(string userId);
        // Update signature to include WHO and WHY
        // Update this line in your Interface
        Task UpdateStatusAsync(int fileLogId, int? validationStatusId, int? workflowStatusId, string userId, string action, string message = null);
        Task<List<FileLifecycleHistory>> GetLifecycleHistoryAsync(int fileLogId);
        Task<UploadedFileLog> GetFileByHashAsync(string hash);
        Task<fileDashboardStatsDto> GetFileDashboardStatsAsync(string userId);
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