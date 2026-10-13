using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Helpers;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class FileProcessingService : IFileProcessingService
    {
        // -- Status constants (matches SQL schema) --------------------------------
        private const int V_PreProcessing = 20;
        private const int V_StructureError = 40;

        private const int W_PreProcessing = 200;
        private const int W_SystemRejected = 209;
        private const int W_PendingAssignment = 210;

        // -- Dependencies ---------------------------------------------------------
        private readonly IFileUploadLogService _logRepo;
        private readonly ITemplateService _templateService;
        private readonly IExcelStructureValidatorService _validator;
        private readonly IAzureBlobService _azureBlobService;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ILogger<FileProcessingService> _logger;
        private readonly string _connectionString;
        private readonly string _uploadsFolder;

        // -- Single constructor — all dependencies resolved by DI -----------------
        // FIX 1: Removed second constructor (FileProcessingService(string connectionString))
        //        which left _logRepo, _validator, _azureBlobService all null.
        // FIX 2: connectionString now read from config instead of being passed as a
        //        raw string parameter which broke DI registration.
        // FIX 3: AzureBlob:Uploadsfiles ? AzureBlob:UploadsFolder (wrong key caused
        //        DownloadStreamAsync to use wrong folder, returning null stream).
        // FIX 4: Added ILogger so Hangfire job failures are visible in logs.
        public FileProcessingService(
            IConfiguration config,
            IFileUploadLogService logRepo,
            ITemplateService templateService,
            IExcelStructureValidatorService validator,
            IAzureBlobService azureBlobService,
            IBackgroundJobClient backgroundJobClient,
            ILogger<FileProcessingService> logger)
        {
            _logRepo = logRepo ?? throw new ArgumentNullException(nameof(logRepo));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _azureBlobService = azureBlobService ?? throw new ArgumentNullException(nameof(azureBlobService));
            _backgroundJobClient = backgroundJobClient ?? throw new ArgumentNullException(nameof(backgroundJobClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _connectionString = config["ConnectionStrings:DefaultConnection"]
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing from configuration.");

            // FIX 3: Correct key name — was "AzureBlob:Uploadsfiles" (typo)
            _uploadsFolder = config["AzureBlob:UploadsFolder"] ?? "Uploadsfiles";
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        // Progress is display-only: failing to record it must never fail the structure check.
        private async Task ReportProgressAsync(int fileLogId, string? step)
        {
            try { await _logRepo.UpdateProgressAsync(fileLogId, step, null); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not record progress for FileLogId {Id}", fileLogId); }
        }

        public async Task<Dictionary<string, List<string>>> ReadHeadersAsync(int fileLogId)
        {
            var log = await _logRepo.GetLogByIdAsync(fileLogId);
            var filePath = log?.FilePath;
            if (string.IsNullOrEmpty(filePath)) return new();
            try
            {
                if (filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    await using var azureStream = await _azureBlobService.DownloadStreamAsync(_uploadsFolder, log!.StoredFileName ?? "");
                    if (azureStream == null) return new();
                    // The OpenXML reader needs a stream it can seek, so the blob is buffered.
                    using var buffer = new MemoryStream();
                    await azureStream.CopyToAsync(buffer);
                    return _validator.ReadHeaders(buffer);
                }
                if (!File.Exists(filePath)) return new();
                await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                return _validator.ReadHeaders(fileStream);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ReadHeadersAsync: could not read the headers of FileLogId {Id}", fileLogId);
                return new();
            }
        }

        // ------------------------------------------------------------------------
        // 1. PROCESS FILE — structure validation
        //    Called by Hangfire after upload completes.
        //    Validates sheet names and required column headers only.
        //    Does NOT read data rows — safe for 1.2M row files.
        // ------------------------------------------------------------------------
        public async Task ProcessFileAsync(int fileLogId, int templateId)
        {
            var log = await _logRepo.GetLogByIdAsync(fileLogId);
            if (log == null)
            {
                _logger.LogWarning("ProcessFileAsync: FileLog {FileLogId} not found — job skipped.", fileLogId);
                return;
            }

            try
            {
                // 1. Mark as processing (FIXED: Now uses Pre-Processing statuses)
                await _logRepo.UpdateStatusAsync(fileLogId, V_PreProcessing, W_PreProcessing, null,
                    "Pre-Processing Started",
                    "Running initial zero-touch structure validation...");
                await ReportProgressAsync(fileLogId, "Checking sheets and column headers");

                // 2. Load template column rules
                var rules = await _templateService.GetColumnMapsForEmployerAsync(templateId, log.TaxID);   // 073: with the employer's own headers
                if (rules == null || rules.Count == 0)
                    throw new InvalidOperationException(
                        $"No mapping rules found for Template ID {templateId}. " +
                        $"Check tbl_ImportTemplate configuration.");

                // (FIXED: Now uses Pre-Processing statuses)
                await _logRepo.UpdateStatusAsync(fileLogId, V_PreProcessing, W_PreProcessing, null,
                    "Structure Check Started",
                    "System started validating file headers and tab structure against the template.");

                // 3. Download stream and validate
                // FIX 5: Removed MemoryStream wrapper — previously copied the entire
                //        115 MB blob into RAM before parsing. Now the Azure stream is
                //        passed directly to the validator. The SAX-based validator reads
                //        only the header row and stops, so RAM usage stays flat regardless
                //        of how many data rows the file contains.
                ExcelValidationResult result;

                var filePath = log.FilePath ?? throw new FileNotFoundException("The file log has no file path.");
                if (filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "ProcessFileAsync: Downloading blob — Folder: {Folder}, File: {File}",
                        _uploadsFolder, log.StoredFileName);

                    // Stream directly from Azure — no intermediate MemoryStream
                    await using var azureStream = await _azureBlobService.DownloadStreamAsync(
                        _uploadsFolder, log.StoredFileName ?? "");

                    if (azureStream == null)
                        throw new FileNotFoundException(
                            $"Blob not found in Azure. " +
                            $"Folder: '{_uploadsFolder}', File: '{log.StoredFileName}'. " +
                            $"Verify AzureBlob:UploadsFolder in appsettings matches the actual container folder.");

                    result = _validator.ValidateStructure(azureStream, rules);
                }
                else
                {
                    // Local development fallback
                    if (!File.Exists(filePath))
                        throw new FileNotFoundException(
                            $"Local file not found: {filePath}");

                    _logger.LogInformation(
                        "ProcessFileAsync: Reading local file {FilePath}", filePath);

                    await using var fileStream = new FileStream(
                        filePath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, bufferSize: 81920, useAsync: true);

                    result = _validator.ValidateStructure(fileStream, rules);
                }

                await ReportProgressAsync(fileLogId, null);

                // 4. Handle validation result
                if (result.IsValid)
                {
                    // Check if the system auto-assigned this to the SuperAdmin/DA who uploaded it
                    bool isAutoAssigned = !string.IsNullOrEmpty(log.AssignedToUserId);

                    // If auto-assigned, jump straight to 220 (Processing) and 30 (Validating)
                    int nextWs = isAutoAssigned ? 220 : W_PendingAssignment;
                    int nextVs = isAutoAssigned ? 30 : VS.Pending;

                    await _logRepo.UpdateStatusAsync(fileLogId, nextVs, nextWs, null,
                        "Structure Validated",
                        isAutoAssigned ? "Structure verified. Auto-starting deep processing." : "File structure verified. Ready for Data Analyst review.");

                    _logger.LogInformation("ProcessFileAsync: Structure valid for FileLogId {Id}", fileLogId);

                    // EXPRESS LANE: If a Super Admin or DA uploaded it, trigger Tier 2 automatically
                    if (isAutoAssigned)
                    {
                        _backgroundJobClient.Enqueue<IDataProcessingService>(s => s.ExtractAndValidateDataAsync(fileLogId));
                    }
                }
                else
                {
                    var errors = result.Errors?.Any() == true
                        ? result.Errors
                        : new List<string> { "File structure invalid (empty file or unreadable format)." };

                    // Terminal Rejection State - The file is dead
                    await _logRepo.UpdateStatusAsync(fileLogId, V_StructureError, W_SystemRejected, null,
                        "Structure Failed",
                        "File structure invalid or headers missing. A new upload is required.");

                    await _logRepo.AddErrorsAsync(fileLogId, errors);

                    _logger.LogWarning(
                        "ProcessFileAsync: Structure invalid for FileLogId {Id}. Errors: {Errors}",
                        fileLogId, string.Join(" | ", errors));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ProcessFileAsync: Unhandled exception for FileLogId {FileLogId}", fileLogId);

                await ReportProgressAsync(fileLogId, null);

                // FIX: If it completely crashes during structure check, it's a terminal System Error!
                await _logRepo.UpdateStatusAsync(fileLogId, V_StructureError, W_SystemRejected, null,
                    "System Error",
                    $"Catastrophic failure during structure check. File rejected.");

                await _logRepo.AddErrorsAsync(fileLogId,
                    new List<string> { $"System Error: {ex.Message}" });

                // FIX 6: Re-throw so Hangfire marks the job as FAILED with full stack
                //        trace visible in the Hangfire dashboard. Without this, Hangfire
                //        marks the job as Succeeded even when it crashed.
                throw;
            }
        }

        // ------------------------------------------------------------------------
        // 3. GET IMPORT SUMMARY
        // ------------------------------------------------------------------------
        public async Task<ImportValidationSummaryModel?> GetImportSummaryAsync(int fileLogId)
        {
            using var db = Connection;
            return await db.QueryFirstOrDefaultAsync<ImportValidationSummaryModel>(
                "SELECT * FROM ImportValidationSummary WHERE FileLogId = @Id",
                new { Id = fileLogId });
        }

        // ------------------------------------------------------------------------
        // NOTE: HandleBackgroundUploadAsync has been removed.
        //
        // The current upload architecture uses two-tier SAS upload:
        //   Tier 1 (= 10 MB): Controller ? UploadDirectAsync ? CreateLogAndTriggerWorkflow
        //   Tier 2 (10–120 MB): Browser ? Azure Blob direct via SAS ? FinalizeUpload
        //
        // Neither tier calls HandleBackgroundUploadAsync. Keeping it would cause
        // confusion and it referenced the old UploadFileAsync path which bypasses
        // the current retry and single-PUT optimizations in UploadDirectAsync.
        // ------------------------------------------------------------------------
    }
}
