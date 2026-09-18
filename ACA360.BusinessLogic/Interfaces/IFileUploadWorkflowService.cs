using ACA360.Core.Models;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IFileUploadWorkflowService
    {
        //Task CreateLogAndTriggerWorkflowAsync(string originalFileName, string storedFileName, string filePath, string fileHash, long fileSize, int templateId, string userId, string role, int planYear);
        //Task TriggerWorkflowAsync(UploadedFileLog log, string userId, string role, int? fileLogId = null);
        //void EnqueuePreProcessingJob(int fileLogId, int templateId);
        //void EnqueueDeepProcessingJob(int fileLogId);
        Task<bool> VerifyFileAccessAsync(int fileLogId, string userId, string role);
        string GetSuccessMessage(string role);
        bool IsDAOrAbove(string role);
        bool CanImport(string role);
        string ComputeSha256Hash(System.IO.Stream stream);
        // Interface definition
        bool VerifyFileAccess(UploadedFileLog log, string userId, string role);
    }
}
