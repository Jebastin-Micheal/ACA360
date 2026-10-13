using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.Core.Constants;
using ACA360.Core.Helpers;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ACA360.Web.Filters;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," +
                       UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," +
                       UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," +
                       UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public partial class FileUploadController : BaseController
    {
        private readonly IFileUploadLogService _logRepo;
        private readonly IDataProcessingRepository _dataRepo;
        private readonly ITemplateService _templateService;
        private readonly IFileProcessingService _fileProcessingService;
        private readonly ILogger<FileUploadController> _logger;
        private readonly IUserAccountsService _userAccountsService;
        private readonly IAzureBlobService _azureBlobService;
        private readonly IFileUploadWorkflowService _workflowService;
        private readonly ACA360.Web.Builders.FileUploadViewModelBuilder _builder;
        private readonly IFileParsingService _fileParsingService;
        private readonly IPlanYearScanService _planYearScan;
        private readonly string _uploadsFolder;
        private readonly string _archiveFolder;
        private readonly IEmployerService _employerService;
        public FileUploadController(
            IConfiguration config,
            IFileUploadLogService logRepo,
            IDataProcessingRepository dataRepo,
            ITemplateService templateService,
            IFileProcessingService fileProcessingService,
            ILogger<FileUploadController> logger,
            IUserAccountsService userAccountsService,
            IAzureBlobService azureBlobService,
            IFileUploadWorkflowService workflowService, 
            IEmployerService employerService,
            IFileParsingService fileParsingService,
            IPlanYearScanService planYearScan,
            ACA360.Web.Builders.FileUploadViewModelBuilder builder)
        {
            _planYearScan = planYearScan;
            _logRepo = logRepo;
            _dataRepo = dataRepo;
            _templateService = templateService;
            _fileProcessingService = fileProcessingService;
            _logger = logger;
            _userAccountsService = userAccountsService;
            _azureBlobService = azureBlobService;
            _workflowService = workflowService;
            _builder = builder;
            _uploadsFolder = config["AzureBlob:UploadsFolder"] ?? "Uploadsfiles";
            _archiveFolder = $"{_uploadsFolder}/archive";
            _employerService = employerService;
            _fileParsingService = fileParsingService;
        }

        // ==========================================
        // 1. DASHBOARD & GRIDS
        // ==========================================

        public async Task<IActionResult> Index(string searchTerm, string filter = "All", string? dateRange = null, bool showMyFiles = false, int pageNumber = 1)
        {
            try
            {
                var userId = GetCurrentUserId();
                var (from, to) = _builder.ParseDateRange(dateRange);

                var result = await _logRepo.SearchPaginatedAsync(searchTerm, 50, pageNumber, userId, "UploadedAt", "DESC", filter, from, to, showMyFiles);
                var activeTemplates = await _templateService.GetActiveTemplatesAsync() ?? new List<ImportTemplate>();

                var model = _builder.BuildGroupedDashboardViewModel(result.Logs?.ToList() ?? new List<UploadedFileLog>(), result.StatusCounts, activeTemplates, result.TotalCount, searchTerm, filter, dateRange, showMyFiles, pageNumber, 50);

                ViewBag.DataAnalysts = await GetDataAnalystsList();
                return View(model);
            }
            catch (Exception ex) { return Content($"Critical error: {ex.Message}"); }
        }

        [HttpGet]
        public async Task<IActionResult> GetPaginatedData(string searchTerm, string filter = "All", bool showMyFiles = false, string? dateRange = null, string viewMode = "Active", int pageSize = 50, int pageNumber = 1)
        {
            var userId = GetCurrentUserId();
            var (from, to) = _builder.ParseDateRange(dateRange);

            var result = await _logRepo.SearchPaginatedAsync(searchTerm, pageSize, pageNumber, userId, "UploadedAt", "DESC", filter, from, to, showMyFiles, viewMode);
            var model = _builder.BuildPartialDashboardViewModel(result.Logs ?? new List<UploadedFileLog>(), result.TotalCount, result.StatusCounts, filter, viewMode, pageNumber, pageSize);

            ViewBag.DataAnalysts = await GetDataAnalystsList();
            return PartialView("_FileLogTable", model);
        }

        // ==========================================
        // 2. UPLOADING
        // ==========================================
        // --- NEW: THE ROUTING ENGINE ---
        private async Task<(string? assignedUserId, int workflowStatusId)> DetermineRoutingAsync(int employerId)
        {
            string role = CurrentRole();
            string userId = GetCurrentUserId();

            // Rule 1: Self-Assign Override (DA or Admin uploads it)
            if (_workflowService.IsDAOrAbove(role))
            {
                return (userId, WS.Processing); // Maps to DA Reviewing / Processing
            }

            // Rule 2: Portfolio Match (AM uploads it, find their DA)
            var assignedDaUserId = await _employerService.GetAssignedDataAnalystForEmployerAsync(employerId);
            if (!string.IsNullOrWhiteSpace(assignedDaUserId))
            {
                return (assignedDaUserId, WS.Processing); // Auto-assigned
            }

            // Rule 3: The Fallback Queue (No DA mapped to this employer)
            return (null, WS.PendingAssignment);
        }

        [HttpPost]
        [Authorize(Roles = UserRoles.FileHandlers)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SmartDetectEmployer(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "No file detected." });

            if (!UploadConstants.AllowedExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
                return Json(new { success = false, unsupportedType = true, message = UploadConstants.UnsupportedTypeMessage });

            try
            {
                // 1. Read the stream and extract the EIN (Lightweight peek)
                string? detectedEin;
                try
                {
                    using var stream = file.OpenReadStream();
                    detectedEin = _fileParsingService.ExtractEinFromStream(stream, file.FileName);
                }
                catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException)
                {
                    // Corrupt, password-protected, or not really an .xlsx despite the name.
                    _logger.LogWarning(ex, "SmartDetectEmployer could not open {FileName}", file.FileName);
                    return Json(new { success = false, message = "This file could not be opened as an Excel workbook. Check it isn't password-protected or damaged, then save it again as .xlsx." });
                }

                if (string.IsNullOrEmpty(detectedEin))
                {
                    return Json(new { success = false, message = "No EIN was found. The first sheet needs an \"EIN\", \"Primary EIN\", \"Employer EIN\" or \"Tax ID\" column header in row 1, with the value in row 2." });
                }

                // Clean the EIN (remove dashes if necessary for DB matching)
                string cleanEin = detectedEin.Replace("-", "").Trim();

                // 2. Check the Database for the Employer
                var employer = await _employerService.GetEmployerByEinAsync(cleanEin);

                if (employer == null)
                {
                    // THE ORPHAN FILE GATEKEEPER
                    return Json(new
                    {
                        success = false,
                        isOrphan = true,
                        message = $"Orphan File Warning: The detected EIN ({detectedEin}) does not exist in ACA360. Please create the employer profile first."
                    });
                }

                // 3. Success! Return Employer details to the UI
                return Json(new
                {
                    success = true,
                    employerId = employer.Id,
                    employerName = employer.Name,
                    ein = detectedEin
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SmartDetectEmployer: error reading the file stream for EIN detection.");
                return Json(new { success = false, message = "System error occurred while analyzing the file." });
            }
        }

        [HttpPost]
        [Authorize(Roles = UserRoles.FileHandlers)]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(10_500_000)]
        // 🛡️ FIX: Added 'string taxId' to the parameter list
        [RequireEmployerScope]
        public async Task<IActionResult> Upload(IFormFile file, int templateId, int planYear, int employerId, string taxId, bool forceUpload = false, bool confirmYear = false)
        {
            if (file == null || file.Length == 0) return Json(new { error = "No file selected." });
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!UploadConstants.AllowedExtensions.Contains(ext)) return Json(new { error = UploadConstants.UnsupportedTypeMessage });
            if (file.Length > UploadConstants.SmallFileThreshold) return Json(new { error = "Files over 5 MB must use the large file uploader." });

            try
            {
                string fileHash;
                using (var s = file.OpenReadStream()) fileHash = await Task.Run(() => _workflowService.ComputeSha256Hash(s));

                if (!forceUpload) { var dupCheck = await CheckDuplicateAsync(fileHash); if (dupCheck != null) return dupCheck; }

                // Does the data actually describe the year they picked? The dropdown
                // pre-selects the current year, and nothing downstream objects if that
                // is wrong -- the file imports cleanly and the codes come out silently
                // wrong. Kept separate from forceUpload so that confirming a duplicate
                // does not also wave through a mismatched year.
                if (!confirmYear)
                {
                    using var scanStream = file.OpenReadStream();
                    var yearCheck = CheckPlanYear(scanStream, planYear);
                    if (yearCheck != null) return yearCheck;
                }

                // 1. Upload to Azure
                var storedFileName = $"{Guid.NewGuid()}{ext}";
                string fileUri;
                using (var s = file.OpenReadStream()) fileUri = await _azureBlobService.UploadDirectAsync(s, _uploadsFolder, storedFileName);

                // 2. Routing Engine & Log Creation
                var (assignedDa, wsStatus) = await DetermineRoutingAsync(employerId);
                var employer = await _employerService.GetEmployerByIdAsync(employerId.ToString());

                // 🛡️ THE GATEKEEPER: Prefer the explicitly passed taxId, fallback to employer object if missing
                string? finalTaxId = !string.IsNullOrWhiteSpace(taxId) ? taxId : employer?.EIN;

                var logEntry = new UploadedFileLog
                {
                    OriginalFileName = file.FileName,
                    StoredFileName = storedFileName,
                    FilePath = fileUri,
                    FileHash = fileHash,
                    EmployerName = employer?.Name,
                    TaxID = finalTaxId, // 👈 Guaranteed to populate now!
                    ImportTemplateId = templateId,
                    PlanYear = planYear,
                    UploadedByUserId = GetCurrentUserId(),
                    UploadedAt = DateTime.UtcNow,
                    AssignedToUserId = assignedDa,
                    ValidationStatusId = VS.Pending,
                    WorkflowStatusId = wsStatus
                };

                int fileLogId = await _logRepo.CreateLogAsync(logEntry);

                // 3. PHASE 1 TRIGGER: Structure Check Only
                BackgroundJob.Enqueue<IFileProcessingService>(worker => worker.ProcessFileAsync(fileLogId, templateId));

                return Json(new { success = true, message = "Upload complete. Structure validation is running in the background." });
            }
            catch (Exception ex) { return HandleError(ex, "Tier 1 upload failed", "Upload failed. Please try again."); }
        }

        [HttpPost]
        [Authorize(Roles = UserRoles.FileHandlers)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GetSasUploadUrl([FromBody] SasInitRequest request)
        {
            try
            {
                var ext = Path.GetExtension(request.FileName).ToLowerInvariant();
                if (!UploadConstants.AllowedExtensions.Contains(ext)) return BadRequest(new { error = UploadConstants.UnsupportedTypeMessage });
                if (request.FileSize > UploadConstants.MaxAllowedFileSize) return BadRequest(new { error = "File exceeds the 120 MB limit." });
                if (request.FileSize <= UploadConstants.SmallFileThreshold) return BadRequest(new { error = "Use standard uploader under 5 MB." });

                if (!string.IsNullOrEmpty(request.FileHash) && !request.ForceUpload)
                { var dupCheck = await CheckDuplicateAsync(request.FileHash, true); if (dupCheck != null) return dupCheck; }

                var blobName = $"{_uploadsFolder}/{Guid.NewGuid()}{ext}";
                var sasUrl = await _azureBlobService.GenerateSasUploadUrlAsync(blobName, UploadConstants.SasExpiryMinutes);

                // 1. Routing Engine
                var (assignedDa, wsStatus) = await DetermineRoutingAsync(request.EmployerId);
                var employer = await _employerService.GetEmployerByIdAsync(request.EmployerId.ToString());

                // 🛡️ THE GATEKEEPER: Prefer the explicitly passed taxId
                string? finalTaxId = !string.IsNullOrWhiteSpace(request.TaxId) ? request.TaxId : employer?.EIN;

                // 2. Initializing the Log
                var logEntry = new UploadedFileLog
                {
                    OriginalFileName = request.FileName,
                    StoredFileName = Path.GetFileName(blobName),
                    FilePath = "PENDING",
                    FileHash = request.FileHash ?? "PENDING",
                    EmployerName = employer?.Name,
                    TaxID = finalTaxId, // 👈 Guaranteed to populate now!
                    ImportTemplateId = request.TemplateId,
                    PlanYear = request.PlanYear,
                    UploadedByUserId = GetCurrentUserId(),
                    UploadedAt = DateTime.UtcNow,
                    AssignedToUserId = assignedDa,
                    ValidationStatusId = VS.Pending,
                    WorkflowStatusId = wsStatus
                };

                return Ok(new SasInitResponse { SasUrl = sasUrl, BlobName = blobName, FileLogId = await _logRepo.CreateLogAsync(logEntry), ChunkSize = UploadConstants.ChunkSize });
            }
            catch (Exception ex) { return HandleError(ex, "SAS generation failed", "Could not initialise upload."); }
        }

        [HttpPost]
        [Authorize(Roles = UserRoles.FileHandlers)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizeUpload([FromBody] FinalizeRequest request)
        {
            try
            {
                if (!await _azureBlobService.BlobExistsAsync(request.BlobName)) return BadRequest(new { error = "Upload incomplete. Please try again." });
                var log = await _logRepo.GetLogByIdAsync(request.FileLogId);
                if (log == null) return NotFound(new { error = "Upload record not found." });

                var dup = await _logRepo.GetFileByHashAsync(request.FileHash);
                if (dup != null && dup.FileLogId != request.FileLogId)
                {
                    await _azureBlobService.DeleteIfExistsAsync(request.BlobName);
                    return Ok(new { isDuplicate = true, duplicateFileName = dup.OriginalFileName, duplicateDate = dup.UploadedAt.ToString("MMM dd, yyyy HH:mm"), duplicateUser = dup.UploadedByName ?? "System" });
                }

                // The year check has to happen here for large files: at SAS-init time
                // the bytes are not in Azure yet, and once Phase 1 is queued the run is
                // under way. This costs one extra sequential read of the blob, which is
                // cheaper than a set of 1095-Cs with no Line 15 on them.
                if (!request.ConfirmYear && (log.PlanYear ?? 0) > 0)
                {
                    var slash = request.BlobName.LastIndexOf('/');
                    var folder = slash > 0 ? request.BlobName.Substring(0, slash) : "";
                    var name = slash > 0 ? request.BlobName.Substring(slash + 1) : request.BlobName;

                    using var blobStream = await _azureBlobService.DownloadStreamAsync(folder, name);
                    if (blobStream != null)
                    {
                        var yearCheck = CheckPlanYear(blobStream, log.PlanYear.GetValueOrDefault(), asOk: true);
                        if (yearCheck != null) return yearCheck;
                    }
                }

                // 1. Mark file as completely saved
                log.FilePath = _azureBlobService.GetBlobUri(request.BlobName);
                log.FileHash = request.FileHash;
                await _logRepo.UpdateLogAsync(log);

                // 2. PHASE 1 TRIGGER: Structure Check Only
                BackgroundJob.Enqueue<IFileProcessingService>(worker => worker.ProcessFileAsync(log.FileLogId, log.ImportTemplateId));

                return Ok(new { success = true, message = "Upload complete. Structure validation is running in the background." });
            }
            catch (Exception ex) { return HandleError(ex, "FinalizeUpload failed", "Could not finalize upload."); }
        }

        // ==========================================
        // 3. WORKFLOW ACTIONS (CORRECTED PIPELINE)
        // ==========================================

        // 1. CLAIM / ACCEPT FILE (DA Action)
        [HttpPost]
        [Authorize(Roles = UserRoles.DataAnalyst + "," + UserRoles.DASupervisor + "," + UserRoles.SuperAdmin)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptFile(int fileLogId)
        {
            var log = await _logRepo.GetLogByIdAsync(fileLogId);
            if (log == null || log.WorkflowStatusId != WS.PendingAssignment)
                return Json(new { success = false, message = "File is not available to be claimed." });

            log.AssignedToUserId = GetCurrentUserId();
            log.WorkflowStatusId = WS.Processing;
            log.ValidationStatusId = VS.Validating;
            await _logRepo.UpdateLogAsync(log);

            // Trigger Phase 2: Data Extraction & ACA Deep Rules
            BackgroundJob.Enqueue<IDataProcessingService>(worker => worker.ExtractAndValidateDataAsync(log.FileLogId));
            return Json(new { success = true, message = "File accepted. Deep data processing started." });
        }

        // 2. REJECT FILE (DA Action — Discards file before final AM submission)
        [HttpPost]
        [Authorize(Roles = UserRoles.DataAnalyst + "," + UserRoles.DASupervisor + "," + UserRoles.SuperAdmin)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectFile(int fileLogId, string reason = "")
        {
            var log = await _logRepo.GetLogByIdAsync(fileLogId);

            // Allow rejections any time during remediation, but lock once the import has begun.
            // Listed explicitly rather than compared with >=, because the codes above these
            // (DARejected 290, OnHold 298, Superseded 299) are not "further along".
            if (log == null || log.WorkflowStatusId is WS.QueuedForImport or WS.Importing or WS.Complete)
                return Json(new { success = false, message = "Cannot reject a file once it has been imported." });

            log.WorkflowStatusId = WS.DARejected;
            await _logRepo.UpdateLogAsync(log);

            await _logRepo.UpdateStatusAsync(fileLogId, log.ValidationStatusId, WS.DARejected, GetCurrentUserId(), "DA Rejected", string.IsNullOrWhiteSpace(reason) ? "Rejected by Data Analyst." : reason);
            return Json(new { success = true, message = "File rejected and removed from queue." });
        }

        // 3. ASSIGN FILE (Manager/AM Action)
        //    Requirement: a file that was not auto-routed to a Data Analyst on upload must be
        //    assignable to one. ACA Director added — that role can upload, so it must be able
        //    to place its own file with an analyst rather than leaving it in the pool.
        [HttpPost]
        [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.DASupervisor + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.AccountManager + "," + UserRoles.AMSupervisor)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignFile(int fileLogId, string targetUserId)
        {
            var log = await _logRepo.GetLogByIdAsync(fileLogId);
            if (log == null)
                return Json(new { success = false, message = "File not found." });

            // Detect if this file actually needs a background extraction pass executed
            bool needsDataValidation = log.WorkflowStatusId == WS.PendingAssignment;

            log.AssignedToUserId = targetUserId;
            if (needsDataValidation)
            {
                log.WorkflowStatusId = WS.Processing;
                // As AcceptFile does: without this the file keeps showing its previous
                // validation result until the job starts, so the page never shows it running.
                log.ValidationStatusId = VS.Validating;
            }

            await _logRepo.UpdateLogAsync(log);
            await _logRepo.UpdateStatusAsync(fileLogId, log.ValidationStatusId, log.WorkflowStatusId, GetCurrentUserId(), "File Assigned", "Routed to Data Analyst queue.");

            // CONDITIONAL PHASE 2: Only spin up Hangfire if it hasn't processed data rows yet
            if (needsDataValidation)
            {
                BackgroundJob.Enqueue<IDataProcessingService>(worker => worker.ExtractAndValidateDataAsync(log.FileLogId));
            }

            return Json(new { success = true, message = "File assigned successfully." });
        }

        // 4. UNASSIGN FILE (Release file back to the public pool)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnassignFile(int fileLogId)
        {
            var log = await _logRepo.GetLogByIdAsync(fileLogId);
            if (log == null) return Json(new { success = false, message = "File not found." });

            string role = CurrentRole();
            bool isAuthorizedManager = _workflowService.IsDAOrAbove(role) || role == UserRoles.AccountManager || role == UserRoles.AMSupervisor || role == UserRoles.Admin;

            if (!isAuthorizedManager && log.AssignedToUserId != GetCurrentUserId())
                return Json(new { success = false, message = "Access Denied." });

            // Allow releasing back to the pool until the import takes hold.
            if (log.WorkflowStatusId is WS.QueuedForImport or WS.Importing or WS.Complete)
                return Json(new { success = false, message = "Cannot unassign a file that is importing or already complete." });

            log.AssignedToUserId = null;
            log.WorkflowStatusId = WS.PendingAssignment;
            await _logRepo.UpdateLogAsync(log);

            await _logRepo.UpdateStatusAsync(fileLogId, log.ValidationStatusId, WS.PendingAssignment, GetCurrentUserId(), "Unassigned", "Returned to unassigned team pool.");
            return Json(new { success = true, message = "Released to unassigned pool." });
        }

        // 5/6/7. SendForApproval, AMRejectFile and ApproveFile were removed at the client's
        //        request: files are no longer sent to an Account Manager for authorisation.
        //        A Data Analyst (or a manager above them) now takes a file straight from
        //        remediation to import. WS.ReadyForApproval (240), WS.AMRejected (241) and
        //        WS.QueuedForImport (250) are consequently no longer reachable; the lookup
        //        rows survive so historical files still render a meaningful status.

        [HttpPost] [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor)] [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleHold(int fileLogId, string reason = "")
        {
            var log = await _logRepo.GetLogByIdAsync(fileLogId);
            if (log == null || log.WorkflowStatusId is WS.PreProcessing or WS.Processing or WS.Importing or WS.QueuedForImport)
                return Json(new { success = false, message = "Cannot hold active processing." });
            if (log.WorkflowStatusId == WS.OnHold)
            {
                // Back in the queue means unassigned too; otherwise the row reads
                // "Waiting for analyst" while still naming the old analyst.
                log.WorkflowStatusId = WS.PendingAssignment;
                log.AssignedToUserId = null;
                await _logRepo.UpdateLogAsync(log);
                await _logRepo.UpdateStatusAsync(fileLogId, log.ValidationStatusId, WS.PendingAssignment, GetCurrentUserId(), "Hold Released", "Back in queue.");
                return Json(new { success = true, isOnHold = false });
            }
            log.WorkflowStatusId = WS.OnHold; await _logRepo.UpdateLogAsync(log);
            await _logRepo.UpdateStatusAsync(fileLogId, log.ValidationStatusId, WS.OnHold, GetCurrentUserId(), "Put On Hold", string.IsNullOrWhiteSpace(reason) ? "Manual pause." : reason);
            return Json(new { success = true, isOnHold = true });
        }

        //[HttpPost] [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.DASupervisor + "," + UserRoles.Admin)] [ValidateAntiForgeryToken]
        //public async Task<IActionResult> AssignFile(int fileLogId, string targetUserId)
        //{
        //    var log = await _logRepo.GetLogByIdAsync(fileLogId);
        //    if (log == null || log.WorkflowStatusId >= WS.Processing) return Json(new { success = false, message = "Cannot reassign." });
        //    log.AssignedToUserId = targetUserId; log.WorkflowStatusId = WS.DAReviewing; log.ValidationStatusId = VS.Pending;
        //    await _logRepo.UpdateLogAsync(log);
        //    _workflowService.EnqueueProcessingJob(log.FileLogId, log.ImportTemplateId);
        //    return Json(new { success = true, message = "File assigned successfully." });
        //}

        //[HttpPost] [ValidateAntiForgeryToken]
        //public async Task<IActionResult> UnassignFile(int fileLogId)
        //{
        //    var log = await _logRepo.GetLogByIdAsync(fileLogId);
        //    if (log == null) return Json(new { success = false, message = "File not found." });
        //    if (!(_workflowService.IsDAOrAbove(CurrentRole())) && log.AssignedToUserId != GetCurrentUserId()) return Json(new { success = false, message = "Denied." });
        //    if (log.WorkflowStatusId >= WS.Processing) return Json(new { success = false, message = "Cannot unassign." });

        //    log.AssignedToUserId = null; log.WorkflowStatusId = WS.PendingAssignment;
        //    await _logRepo.UpdateLogAsync(log);
        //    return Json(new { success = true, message = "Released to unassigned pool." });
        //}

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportData(int FileLogId, string importMode)
        {
            var log = await _logRepo.GetLogByIdAsync(FileLogId);

            if (!ImportAnalysisCatalog.IsMode(importMode))
                return BadRequest(new { success = false, message = "Invalid import mode." });
            if (log == null || !await _workflowService.VerifyFileAccessAsync(log.FileLogId, GetCurrentUserId()))
                return NotFound(new { success = false, message = "File is unavailable." });

            if (log.IsDeleted || log.WorkflowStatusId == WS.OnHold)
                return Json(new { success = false, message = "File not found or currently on hold." });

            // GATE 1 — WHO. AM authorisation was removed, so import is the last human decision
            // in the pipeline and is limited to the roles that carry a file end to end.
            if (!_workflowService.CanImport(CurrentRole()))
                return Json(new { success = false, message = "You do not have permission to import data." });

            // GATE 2 — WHEN. The file must have finished validation and still be in the
            // analyst's hands. QueuedForImport and ImportFailed are accepted so that files
            // left over from the approval workflow, and a failed run being retried, can both
            // still be pushed through.
            if (log.WorkflowStatusId is not (WS.DAReviewing or WS.Processing
                                             or WS.QueuedForImport or WS.ImportFailed))
                return Json(new { success = false, message = "File is not ready to import. Resolve the outstanding errors first." });

            // GATE 3 — WHAT. This replaces the Account Manager's sign-off. Nothing else in the
            // pipeline inspected the validation result before writing to the live tables, so
            // without this a file full of fatal errors would import silently.
            if (log.ValidationStatusId is not (VS.Clean or VS.ValidWithWarnings))
                return Json(new { success = false, message = "This file has unresolved validation errors and cannot be imported. Clear them in Error Triage first." });

            try
            {
                var analysis = await _dataRepo.GetImportAnalysisSummaryAsync(FileLogId, importMode, HttpContext.RequestAborted);
                if (analysis.Entities.Sum(x => x.Total) == 0 || analysis.Entities.Any(x => x.ErrorRows > 0 || x.Excluded > 0))
                    return Json(new { success = false, message = "The preview has errors or excluded rows. Resolve them and analyze the file again." });

                // Claim the file BEFORE queuing. This write is the lock: Gate 2 above
                // only accepts DAReviewing/Processing/QueuedForImport/ImportFailed, so
                // a second click now finds the file at Importing and is refused.
                await _logRepo.UpdateStatusAsync(
                    FileLogId, log.ValidationStatusId, WS.Importing, GetCurrentUserId(),
                    "Import Queued", "Queued for merge into live tables.");

                BackgroundJob.Enqueue<IDataProcessingService>(
                    worker => worker.ImportToLiveAsync(FileLogId, importMode, GetCurrentUserId()));

                return Json(new
                {
                    success = true,
                    queued = true,
                    message = "Import started. You can leave this page - the file list will update when it finishes."
                });
            }
            catch (Exception ex)
            {
                // Only reached if the claim or the enqueue itself failed, so the import
                // has not begun. Put the file back where it was rather than marking it
                // ImportFailed, which would wrongly imply the merge was attempted.
                await _logRepo.UpdateStatusAsync(
                    FileLogId, log.ValidationStatusId, log.WorkflowStatusId, GetCurrentUserId(),
                    "Import Not Started", "Could not queue the import.");
                return HandleError(ex, "Queuing import failed", "Could not start the import. Please try again.");
            }
        }

        // ==========================================
        // 4. STATS & MANAGEMENT
        // ==========================================

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Analyze(int id, string importMode = "Merge", System.Threading.CancellationToken cancellationToken = default)
        {
            if (id <= 0 || !ImportAnalysisCatalog.IsMode(importMode))
                return BadRequest(new { success = false, message = "Invalid analysis request." });

            try
            {
                var fileLog = await _logRepo.GetLogByIdAsync(id);
                if (fileLog == null || fileLog.IsDeleted ||
                    !await _workflowService.VerifyFileAccessAsync(fileLog.FileLogId, GetCurrentUserId()))
                    return NotFound(new { success = false, message = "File is unavailable." });
                var summary = await _dataRepo.GetImportAnalysisSummaryAsync(id, importMode, cancellationToken);
                var errors = summary.Entities.Sum(x => x.ErrorRows);
                var excluded = summary.Entities.Sum(x => x.Excluded);
                var canImport = _workflowService.CanImport(CurrentRole()) && errors == 0 && excluded == 0 &&
                    summary.Entities.Sum(x => x.Total) > 0 &&
                    fileLog.ValidationStatusId is (VS.Clean or VS.ValidWithWarnings) &&
                    fileLog.WorkflowStatusId is (WS.DAReviewing or WS.Processing or WS.QueuedForImport or WS.ImportFailed);

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        fileLogId = id,
                        fileName = fileLog.OriginalFileName,
                        planYear = fileLog.PlanYear,
                        importMode,
                        totalRows = summary.Entities.Sum(x => x.Total),
                        errorRows = errors,
                        excludedRows = excluded,
                        warningRows = summary.Entities.Sum(x => x.WarningRows),
                        informationRows = summary.Entities.Sum(x => x.InformationRows),
                        cleanRows = summary.Entities.Sum(x => x.CleanRows),
                        entityStats = summary.Entities,
                        relatedRemovals = summary.RelatedRemovals,
                        canImport
                    }
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new EmptyResult(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import analysis failed for file {FileLogId}", id);
                return StatusCode(500, new { success = false, message = "Could not load the analysis. Please retry." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetAnalysisRows([FromBody] ImportAnalysisRequest? request,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid || request == null)
                return BadRequest(new { error = "Invalid grid request." });
            var error = request.Validate();
            if (error != null) return BadRequest(new { error });
            try
            {
                var log = await _logRepo.GetLogByIdAsync(request.FileLogId);
                if (log == null || log.IsDeleted || !await _workflowService.VerifyFileAccessAsync(log.FileLogId, GetCurrentUserId()))
                    return NotFound(new { error = "File is unavailable." });
                var page = await _dataRepo.GetImportAnalysisRowsAsync(request, cancellationToken);
                return Json(new { draw = request.Draw, recordsTotal = page.RecordsTotal,
                    recordsFiltered = page.RecordsFiltered, data = page.Rows });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new EmptyResult(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import analysis grid failed for file {FileLogId} sheet {Entity}", request.FileLogId, request.Entity);
                return StatusCode(500, new { error = "Could not load these rows. Please retry." });
            }
        }

        [HttpGet] public async Task<IActionResult> GetProcessingLogs(int id) => await _workflowService.VerifyFileAccessAsync(id, GetCurrentUserId()) ? PartialView("_ProcessingLogsPartial", await _logRepo.GetErrorsByFileLogIdAsync(id)) : Unauthorized();
        [HttpGet] public async Task<IActionResult> GetImportStatus(int id) => await _workflowService.VerifyFileAccessAsync(id, GetCurrentUserId()) ? Json(new { success = true, data = await _fileProcessingService.GetImportSummaryAsync(id) }) : Json(new { success = false, message = "Access Denied" });
        [HttpGet] public async Task<IActionResult> GetFileHistory(int id) => await _workflowService.VerifyFileAccessAsync(id, GetCurrentUserId()) ? PartialView("_LifecycleTimeline", await _logRepo.GetLifecycleHistoryAsync(id)) : Unauthorized();
        
        public async Task<IActionResult> DownloadFile(int id)
        {
            if (!await _workflowService.VerifyFileAccessAsync(id, GetCurrentUserId())) return Forbid();
            var log = await _logRepo.GetLogByIdAsync(id);
            if (log == null || string.IsNullOrEmpty(log.StoredFileName)) return NotFound("File not found.");
            var stream = await _azureBlobService.DownloadStreamAsync(_uploadsFolder, log.StoredFileName);
            if (stream == null) return NotFound("File not found in storage.");
            Response.Cookies.Append("fileDownloadToken", "true", new CookieOptions { Path = "/", HttpOnly = false });
            return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", log.OriginalFileName);
        }

        [HttpPost] [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleArchive(int fileLogId)
        {
            if (!await _workflowService.VerifyFileAccessAsync(fileLogId, GetCurrentUserId())) return Forbid();
            var log = await _logRepo.GetLogByIdAsync(fileLogId); if (log == null) return NotFound();
            log.IsArchived = !log.IsArchived; await _logRepo.UpdateLogAsync(log);
            return Json(new { success = true });
        }

        [HttpPost] [ValidateAntiForgeryToken] [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin)]
        public async Task<IActionResult> RestoreFile(int fileLogId)
        {
            var log = await _logRepo.GetLogByIdAsync(fileLogId); if (log == null) return NotFound();
            log.IsDeleted = false; await _logRepo.UpdateLogAsync(log); return Json(new { success = true });
        }

        [HttpPost] [ValidateAntiForgeryToken] [Authorize(Roles = UserRoles.SuperAdmin)]
        public async Task<IActionResult> PermanentDelete(int fileLogId)
        {
            var log = await _logRepo.GetLogByIdAsync(fileLogId); if (log == null) return NotFound();
            try
            {
                if (!string.IsNullOrEmpty(log.StoredFileName))
                {
                    await _azureBlobService.DeleteFileAsync(_uploadsFolder, log.StoredFileName);
                    await _azureBlobService.DeleteFileAsync(_archiveFolder, log.StoredFileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete files from blob storage for fileLogId {FileLogId}.", fileLogId);
            }
            await _logRepo.HardDeleteLogAsync(fileLogId); return Json(new { success = true });
        }

        [HttpPost, ActionName("Delete")] [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int FileLogId)
        {
            if (!await _workflowService.VerifyFileAccessAsync(FileLogId, GetCurrentUserId())) return Forbid();
            var log = await _logRepo.GetLogByIdAsync(FileLogId); if (log == null) return RedirectToAction(nameof(Index));
            await _logRepo.SoftDeleteLogAsync(FileLogId);
            try
            {
                if (!string.IsNullOrEmpty(log.StoredFileName))
                    await _azureBlobService.MoveFileAsync(_uploadsFolder, _archiveFolder, log.StoredFileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to move file to archive in blob storage for fileLogId {FileLogId}.", FileLogId);
            }
            TempData["Success"] = "Deleted."; return RedirectToAction(nameof(Index));
        }

        [HttpGet("download-report/{fileLogId}")]
        public IActionResult DownloadReport(int fileLogId) => File(_logRepo.GenerateValidationReport(fileLogId), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ValidationReport_{fileLogId}_{DateTime.Now:yyyyMMdd}.xlsx");

        [HttpGet]
        //public async Task<IActionResult> PollStatus(string ids)
        //{
        //    if (string.IsNullOrEmpty(ids)) return Json(Array.Empty<object>());
        //    var fileIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s.Trim(), out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();
        //    var results = new List<object>();
        //    foreach (var id in fileIds)
        //    {
        //        if (!await _workflowService.VerifyFileAccessAsync(id, GetCurrentUserId())) continue;
        //        var log = await _logRepo.GetLogByIdAsync(id); if (log == null) continue;
        //        var state = FileStatusResolver.Resolve(log.ValidationStatusId ?? 0, log.WorkflowStatusId ?? 0);
        //        results.Add(new { fileLogId = id, vs = log.ValidationStatusId, ws = log.WorkflowStatusId, label = state.Label, color = state.Color, icon = state.Icon, isWorking = state.IsWorking, tooltip = state.Tooltip });
        //    }
        //    return Json(results);
        //}
        [HttpGet]
        public async Task<IActionResult> PollStatus(string ids)
        {
            if (string.IsNullOrWhiteSpace(ids)) return Json(Array.Empty<object>());

            var fileIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                             .Where(n => n > 0)
                             .Distinct()
                             .ToList();

            if (fileIds.Count == 0) return Json(Array.Empty<object>());

            var logs = await _logRepo.GetLogsByIdsAsync(fileIds);
            var visible = await _workflowService.GetVisibleFileIdsAsync(fileIds, GetCurrentUserId());

            var results = logs
                .Where(log => visible.Contains(log.FileLogId))
                .Select(log =>
                {
                    var state = FileStatusResolver.Resolve(
                        log.ValidationStatusId ?? 0, log.WorkflowStatusId ?? 0,
                        !string.IsNullOrWhiteSpace(log.AssignedToUserId));

                    // Progress only means something while the file is being worked on. The
                    // structure check and validation clear these columns when they finish,
                    // but the import leaves its last "Complete" step behind, so an import
                    // is only trusted once it has reported a total.
                    var showProgress = state.IsWorking
                                       && !string.IsNullOrEmpty(log.ValidationCurrentStep)
                                       && (log.WorkflowStatusId != WS.Importing || (log.ValidationTotalRows ?? 0) > 0);

                    return new
                    {
                        fileLogId = log.FileLogId,
                        vs = log.ValidationStatusId,
                        ws = log.WorkflowStatusId,
                        label = state.Label,
                        color = state.Color,
                        icon = state.Icon,
                        isWorking = state.IsWorking,
                        tooltip = state.Tooltip,
                        progressPercent = showProgress ? log.ValidationProgressPercent : null,
                        progressStep = showProgress ? log.ValidationCurrentStep : null,
                        progressDone = showProgress ? log.ValidationRowsProcessed : null,
                        progressTotal = showProgress ? log.ValidationTotalRows : null
                    };
                })
                .ToList();

            return Json(results);
        }
        // ==========================================
        // HELPERS
        // ==========================================

        private string CurrentRole() => User.FindFirstValue(ClaimTypes.Role) ?? "";

        private async Task<List<SelectListItem>> GetDataAnalystsList()
        {
            var analysts = await _userAccountsService.GetUsersInRoleAsync(UserRoles.DataAnalyst);
            return analysts.Select(u => new SelectListItem { Value = u.User_ID.ToString(), Text = u.User_Name }).ToList();
        }

        private async Task<IActionResult?> CheckDuplicateAsync(string hash, bool asOk = false)
        {
            var dup = await _logRepo.GetFileByHashAsync(hash);
            if (dup != null)
            {
                var response = new { isDuplicate = true, duplicateFileName = dup.OriginalFileName, duplicateDate = dup.UploadedAt.ToString("MMM dd, yyyy HH:mm"), duplicateUser = dup.UploadedByName ?? "System" };
                return asOk ? Ok(response) : Json(response);
            }
            return null;
        }

        /// <summary>
        /// Returns a JSON result when the file's periods do not line up with the chosen
        /// filing year, or null when there is no objection. The caller stops on non-null.
        /// A file that cannot be scanned never blocks: that is the structure validator's
        /// job, and a false stop here would be worse than the warning is worth.
        /// </summary>
        private IActionResult? CheckPlanYear(Stream stream, int planYear, bool asOk = false)
        {
            var scan = _planYearScan.Scan(stream, planYear);
            if (!scan.Scanned || !scan.HasWarnings) return null;

            var response = new
            {
                yearMismatch = true,
                planYear,
                suggestedYear = scan.SuggestedYear,
                yearsInFile = scan.YearsSummary,
                warnings = scan.Warnings
            };
            return asOk ? Ok(response) : Json(response);
        }

        private IActionResult HandleError(Exception ex, string dbgMsg, string usrMsg)
        {
            _logger.LogError(ex, dbgMsg);
            return Json(new { success = false, error = usrMsg, message = usrMsg });
        }
        [HttpGet]
        public IActionResult DownloadTemplate(int templateId, string? downloadToken = null)
        {
            string fileName = templateId switch
            {
                1 => "ACA_General_Import_Template.xlsx",
                2 => "ACA_General_Import_Template.xlsx",
                _ => "ACA_General_Import_Template.xlsx"
            };

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "templates", fileName);
            if (!System.IO.File.Exists(filePath))
                return NotFound("The requested template format is not available on the server.");

            // Echo the client's token back so JS can detect this specific download
            if (!string.IsNullOrEmpty(downloadToken))
            {
                Response.Cookies.Append("fileDownloadToken", downloadToken, new CookieOptions
                {
                    Path = "/",
                    HttpOnly = false
                });
            }

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            return File(fileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

    }
}
