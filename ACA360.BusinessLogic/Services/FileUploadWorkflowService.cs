using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Helpers;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Hangfire;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class FileUploadWorkflowService : IFileUploadWorkflowService
    {
        private readonly IFileUploadLogService _logRepo;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public FileUploadWorkflowService(
            IFileUploadLogService logRepo,
            IBackgroundJobClient backgroundJobClient)
        {
            _logRepo = logRepo;
            _backgroundJobClient = backgroundJobClient;
        }

        //public async Task CreateLogAndTriggerWorkflowAsync(
        //    string originalFileName, string storedFileName, string filePath,
        //    string fileHash, long fileSize, int templateId, string userId, string role, int planYear)
        //{
        //    var log = new UploadedFileLog
        //    {
        //        OriginalFileName = originalFileName,
        //        StoredFileName = storedFileName,
        //        FilePath = filePath,
        //        FileHash = fileHash,
        //        ImportTemplateId = templateId,
        //        UploadedByUserId = userId,
        //        UploadedAt = DateTime.UtcNow,
        //        IsDeleted = false,
        //        ValidationStatusId = VS.Pending,
        //        PlanYear = planYear,
        //    };

        //    if (IsDAOrAbove(role)) log.AssignedToUserId = userId;

        //    int fileLogId = await _logRepo.CreateLogAsync(log);
        //    await TriggerWorkflowAsync(log, userId, role, fileLogId);
        //}

        //public async Task TriggerWorkflowAsync(UploadedFileLog log, string userId, string role, int? fileLogId = null)
        //{
        //    var id = fileLogId ?? log.FileLogId;

        //    if (IsDAOrAbove(role))
        //    {
        //        log.WorkflowStatusId = WS.DAReviewing;
        //        log.ValidationStatusId = VS.Pending;
        //        await _logRepo.UpdateLogAsync(log);
        //        EnqueueProcessingJob(id, log.ImportTemplateId);
        //    }
        //    else
        //    {
        //        log.WorkflowStatusId = WS.PendingAssignment;
        //        log.ValidationStatusId = VS.Pending;
        //        await _logRepo.UpdateLogAsync(log);
        //    }
        //}

        // Drives upload ROUTING, not authority: a file uploaded by one of these roles is
        // self-assigned because they can work it themselves. Widening this would stop an
        // Account Manager's upload from routing to the employer's Data Analyst, so keep
        // import authority in CanImport below rather than adding roles here.
        public bool IsDAOrAbove(string role) =>
            role is UserRoles.DataAnalyst or UserRoles.DASupervisor or UserRoles.SuperAdmin;

        // Who may push a validated file into the live tables. Since the AM approval step
        // was removed there is no second pair of eyes, so the safety gate is the file's
        // own validation status (enforced in FileUploadController.ImportData), not a role.
        public bool CanImport(string role) =>
            role is UserRoles.SuperAdmin or UserRoles.Admin or UserRoles.ACADirector
                 or UserRoles.DASupervisor or UserRoles.AMSupervisor
                 or UserRoles.DataAnalyst or UserRoles.AccountManager;

        // One rule for every file action, and the same one the File Upload list applies
        // (dbo.fn_FileVisibleToUser, script 041): a file can be opened exactly when it is
        // listed. The earlier in-code checks disagreed with the list in both directions.
        public async Task<bool> VerifyFileAccessAsync(int fileLogId, string userId) =>
            (await GetVisibleFileIdsAsync(new[] { fileLogId }, userId)).Contains(fileLogId);

        public async Task<HashSet<int>> GetVisibleFileIdsAsync(IEnumerable<int> fileLogIds, string userId) =>
            int.TryParse(userId, out var id) && id > 0
                ? await _logRepo.GetVisibleFileIdsAsync(fileLogIds, id)
                : new HashSet<int>();

        public string ComputeSha256Hash(System.IO.Stream stream)
        {
            using var sha = SHA256.Create();
            var hashBytes = sha.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        // REPLACE EnqueueProcessingJob WITH THESE TWO METHODS:
        //public void EnqueuePreProcessingJob(int fileLogId, int templateId)
        //{
        //    // Tier 1 Only - Runs immediately on upload
        //    _backgroundJobClient.Enqueue<IFileProcessingService>(s =>
        //        s.ProcessFileAsync(fileLogId, templateId));
        //}

        //public void EnqueueDeepProcessingJob(int fileLogId)
        //{
        //    // Tier 2 Only - Runs only after a Data Analyst claims the file
        //    _backgroundJobClient.Enqueue<IDataProcessingService>(s =>
        //        s.ExtractAndValidateDataAsync(fileLogId));
        //}

        //// UPDATE TRIGGER WORKFLOW
        //public async Task TriggerWorkflowAsync(UploadedFileLog log, string userId, string role, int? fileLogId = null)
        //{
        //    var id = fileLogId ?? log.FileLogId;

        //    // EVERY FILE NOW GOES DIRECTLY TO ZERO-TOUCH PRE-PROCESSING
        //    log.WorkflowStatusId = WS.PreProcessing;
        //    log.ValidationStatusId = VS.Pending;

        //    // Maintain auto-assignment if a DA uploaded it, but still force it through Tier 1 first
        //    if (IsDAOrAbove(role)) log.AssignedToUserId = userId;

        //    await _logRepo.UpdateLogAsync(log);

        //    // Only start the Structure Check
        //    EnqueuePreProcessingJob(id, log.ImportTemplateId);
        //}


    }
}
