using ACA360.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IFileUploadWorkflowService
    {
        //Task CreateLogAndTriggerWorkflowAsync(string originalFileName, string storedFileName, string filePath, string fileHash, long fileSize, int templateId, string userId, string role, int planYear);
        //Task TriggerWorkflowAsync(UploadedFileLog log, string userId, string role, int? fileLogId = null);
        //void EnqueuePreProcessingJob(int fileLogId, int templateId);
        //void EnqueueDeepProcessingJob(int fileLogId);
        /// <summary>May this user open this uploaded file? The File Upload list's own rule (dbo.fn_FileVisibleToUser).</summary>
        Task<bool> VerifyFileAccessAsync(int fileLogId, string userId);
        /// <summary>The ids among <paramref name="fileLogIds"/> this user may open.</summary>
        Task<HashSet<int>> GetVisibleFileIdsAsync(IEnumerable<int> fileLogIds, string userId);
        bool IsDAOrAbove(string role);
        bool CanImport(string role);
        string ComputeSha256Hash(System.IO.Stream stream);
    }
}
