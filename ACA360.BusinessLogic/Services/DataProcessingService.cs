using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using ExcelDataReader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Helpers;   // WS / VS status constants
using Hangfire;
namespace ACA360.BusinessLogic.Services
{
    public class DataProcessingService : IDataProcessingService
    {
        // -- Status constants (matches SQL schema) --------------------------------
        private const int V_Validating = 30;
        private const int V_DataError = 50;
        private const int V_ValidWithWarnings = 70;
        private const int V_Clean = 80;

        private const int W_Processing = 220;
        private const int W_NeedsCorrection = 230;
        private const int W_ReadyForApproval = 240; // retired with the AM approval step
        private const int W_QueuedForImport = 250;  // retired with the AM approval step

        // -- Dependencies ---------------------------------------------------------
        private readonly IFileUploadLogService _logRepo;
        private readonly IDataProcessingRepository _dataRepo;
        private readonly ITemplateService _templateService;
        private readonly IAzureBlobService _azureBlobService;
        private readonly ILogger<DataProcessingService> _logger;
        private readonly string _uploadsFolder;

        // -- Constructor — all resolved by DI -------------------------------------
        public DataProcessingService(
            IFileUploadLogService logRepo,
            IDataProcessingRepository dataRepo,
            ITemplateService templateService,
            IConfiguration config,
            IAzureBlobService azureBlobService,
            ILogger<DataProcessingService> logger)
        {
            _logRepo = logRepo ?? throw new ArgumentNullException(nameof(logRepo));
            _dataRepo = dataRepo ?? throw new ArgumentNullException(nameof(dataRepo));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _azureBlobService = azureBlobService ?? throw new ArgumentNullException(nameof(azureBlobService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _uploadsFolder = config["AzureBlob:UploadsFolder"] ?? "Uploadsfiles";

            // Required: fixes "Encoding 1252 data could not be found" on Azure
            System.Text.Encoding.RegisterProvider(
                System.Text.CodePagesEncodingProvider.Instance);
        }

        // ------------------------------------------------------------------------
        // EXTRACT & VALIDATE DATA
        // ------------------------------------------------------------------------
        public async Task ExtractAndValidateDataAsync(int fileLogId)
        {
            var log = await _logRepo.GetLogByIdAsync(fileLogId);

            if (log == null || log.WorkflowStatusId != W_Processing)
            {
                _logger.LogWarning(
                    "ExtractAndValidateDataAsync: FileLogId {Id} skipped — " +
                    "log null or WorkflowStatusId is not W_Processing ({Status})",
                    fileLogId, log?.WorkflowStatusId);
                return;
            }

            try
            {
                // FIX: "Cannot convert null literal to non-nullable reference type."
                // Replaced 'null' with 'string.Empty' for the 4th parameter (likely userId or similar string)
                await _logRepo.UpdateStatusAsync(fileLogId, V_Validating, W_Processing, string.Empty,
                    "Processing Started",
                    "System started streaming and validating data...");

                // -- Load template rules ---------------------------------------
                var rules = await _templateService.GetColumnMapsByTemplateIdAsync(log.ImportTemplateId);
                if (rules == null || !rules.Any())
                    throw new InvalidOperationException(
                        $"No mapping rules found for Template ID {log.ImportTemplateId}.");

                // FIX 1: null-forgiving operator (!) after Where already proved SourceSheetName is non-null
                var excelSheetMap = rules
                    .Where(r => !string.IsNullOrEmpty(r.SourceSheetName))
                    .GroupBy(r => r.SourceSheetName!)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                // -- Clear stale staging data from any prior attempt -----------
                await _dataRepo.ClearStagingDataAsync(fileLogId);

                // -- Get the file stream ---------------------------------------
                Stream fileStream;
                string filePath = log.FilePath ?? string.Empty;
                if (filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    string storedFileName = log.StoredFileName ?? string.Empty;
                    // FIX 2: Guard StoredFileName before passing to DownloadStreamAsync
                    if (string.IsNullOrEmpty(log.StoredFileName))
                        throw new InvalidOperationException(
                            $"FileLogId {fileLogId}: StoredFileName is null or empty.");

                    _logger.LogInformation(
                        "ExtractAndValidateDataAsync: Streaming blob — Folder: {Folder}, File: {File}",
                        _uploadsFolder, log.StoredFileName);

                    fileStream = await _azureBlobService.DownloadStreamAsync(
                        _uploadsFolder, storedFileName);

                    if (fileStream == null)
                        throw new FileNotFoundException(
                            $"Blob not found. Folder: '{_uploadsFolder}', " +
                            $"File: '{log.StoredFileName}'. " +
                            $"Verify AzureBlob:UploadsFolder in appsettings.");
                }
                else
                {
                    if (!File.Exists(log.FilePath))
                        throw new FileNotFoundException(
                            $"Local file not found: {log.FilePath}");

                    _logger.LogInformation(
                        "ExtractAndValidateDataAsync: Reading local file {FilePath}",
                        log.FilePath);

                    fileStream = new FileStream(
                        log.FilePath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, bufferSize: 81920, useAsync: true);
                }

                // -- Stream through all sheets and batch-insert rows -----------
                await using (fileStream)
                using (var reader = ExcelReaderFactory.CreateReader(fileStream))
                {
                    do
                    {
                        var excelTabName = reader.Name?.Trim();

                        if (string.IsNullOrEmpty(excelTabName)
                            || !excelSheetMap.ContainsKey(excelTabName))
                            continue;

                        var currentRules = excelSheetMap[excelTabName];
                        var systemName = currentRules.First().TargetSheetName;

                        // FIX 3: GetTableConfig now returns nullable tuple — null means no match
                        var tableConfig = GetTableConfig(systemName);
                        if (tableConfig is null) continue;

                        // Fetch TVP schema from SQL
                        var tvpColumns = await _dataRepo.GetTvpColumnsAsync(tableConfig.Value.TypeName);
                        var batchTable = CreateDataTableFromSchema(tvpColumns);
                        const int batchSize = 1000;

                        // Read header row and build column index map
                        reader.Read();
                        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var rawVal = reader.GetValue(i)?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(rawVal))
                            {
                                // Use a helper to collapse whitespace: "Primary    EIN" becomes "Primary EIN"
                                var normalizedVal = System.Text.RegularExpressions.Regex.Replace(rawVal, @"\s+", " ");

                                if (!headerMap.ContainsKey(normalizedVal))
                                    headerMap[normalizedVal] = i;
                            }
                        }

                        int rowIndex = 1;

                        while (reader.Read())
                        {
                            var row = batchTable.NewRow();

                            // Initialise all columns to DBNull
                            foreach (DataColumn col in batchTable.Columns)
                                row[col] = DBNull.Value;

                            if (batchTable.Columns.Contains("RowNumber"))
                                row["RowNumber"] = rowIndex.ToString();

                            bool hasData = false;

                            foreach (var rule in currentRules)
                            {
                                // FIX 4: Guard TargetColumnName before passing to Contains
                                if (string.IsNullOrEmpty(rule.TargetColumnName)
                                    || !batchTable.Columns.Contains(rule.TargetColumnName))
                                    continue;

                                int columnIndex = -1;

                                //FIX 5: Guard SourceColumnName before passing to ContainsKey
                                if (!string.IsNullOrEmpty(rule.SourceColumnName)
                                    && headerMap.ContainsKey(rule.SourceColumnName))
                                {
                                    columnIndex = headerMap[rule.SourceColumnName];
                                }
                                else if (!string.IsNullOrEmpty(rule.AlternateNames))
                                {
                                    var alts = rule.AlternateNames.Split(
                                        ',', StringSplitOptions.RemoveEmptyEntries);

                                    // FIX 5b: Guard each alt before passing to ContainsKey
                                    foreach (var alt in alts)
                                    {
                                        if (!string.IsNullOrEmpty(alt)
                                            && headerMap.ContainsKey(alt.Trim()))
                                        {
                                            columnIndex = headerMap[alt.Trim()];
                                            break;
                                        }
                                    }
                                }
                                string? sourceName = !string.IsNullOrEmpty(rule.SourceColumnName)
                                    ? System.Text.RegularExpressions.Regex.Replace(rule.SourceColumnName, @"\s+", " ")
                                    : null;

                                if (sourceName != null && headerMap.ContainsKey(sourceName))
                                {
                                    columnIndex = headerMap[sourceName];
                                }
                                else if (!string.IsNullOrEmpty(rule.AlternateNames))
                                {
                                    var alts = rule.AlternateNames.Split(',', StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var alt in alts)
                                    {
                                        var normalizedAlt = System.Text.RegularExpressions.Regex.Replace(alt.Trim(), @"\s+", " ");
                                        if (headerMap.ContainsKey(normalizedAlt))
                                        {
                                            columnIndex = headerMap[normalizedAlt];
                                            break;
                                        }
                                    }
                                }
                                if (columnIndex != -1)
                                {
                                    var value = reader.GetValue(columnIndex);
                                    if (value != null
                                        && !string.IsNullOrWhiteSpace(value.ToString()))
                                    {
                                        row[rule.TargetColumnName] = value.ToString()!.Trim();
                                        hasData = true;
                                    }
                                }
                            }

                            if (hasData)
                            {
                                batchTable.Rows.Add(row);
                                rowIndex++;
                            }

                            // Flush batch to SQL when full
                            if (batchTable.Rows.Count >= batchSize)
                            {
                                await _dataRepo.BulkLoadBatchAsync(
                                    tableConfig.Value.Proc, fileLogId, batchTable);
                                batchTable.Rows.Clear();
                            }
                        }

                        // Final flush for remaining rows
                        if (batchTable.Rows.Count > 0)
                        {
                            await _dataRepo.BulkLoadBatchAsync(
                                tableConfig.Value.Proc, fileLogId, batchTable);
                        }

                        _logger.LogInformation(
                            "ExtractAndValidateDataAsync: Sheet '{Sheet}' — {Rows} rows processed for FileLogId {Id}",
                            excelTabName, rowIndex - 1, fileLogId);

                    } while (reader.NextResult());
                }

                // -- Run SQL validation engine ---------------------------------
                await _dataRepo.ExecuteValidationAsync(fileLogId);

                // -- Read final status decided by SQL, write audit entry --------
                var updatedLog = await _logRepo.GetLogByIdAsync(fileLogId);
                if (updatedLog != null)
                {
                    // =====================================================================
                    // 🛡️ THE GATEKEEPER 🛡️
                    // sp_Validation_Finalize used to park a clean file at 240 (Ready for
                    // Approval). Script 016 changes it to land on 220 instead, since the
                    // approval step no longer exists — but this stays as a safety net for
                    // any file that reaches here at 240 or 250 (a database not yet patched,
                    // or a file left mid-flight by the old workflow). Either way the analyst
                    // keeps control and can review the warnings before importing.
                    // =====================================================================
                    if (updatedLog.WorkflowStatusId is W_ReadyForApproval or W_QueuedForImport)
                    {
                        updatedLog.WorkflowStatusId = W_Processing; // Back to remediation
                        await _logRepo.UpdateLogAsync(updatedLog);
                    }
                    // =====================================================================
                    string auditTitle, auditMessage;

                    if (updatedLog.ValidationStatusId == V_DataError)
                    {
                        auditTitle = "Validation Failed";
                        auditMessage = "Critical errors found. Please review the error report.";
                    }
                    else if (updatedLog.ValidationStatusId == V_ValidWithWarnings)
                    {
                        auditTitle = "Validation Completed with Warnings";
                        auditMessage = "Non-fatal warnings found. File is ready for review.";
                    }
                    else
                    {
                        auditTitle = "Validation Passed";
                        auditMessage = "File is clean and ready for approval.";
                    }

                    await _logRepo.UpdateStatusAsync(
                        fileLogId,
                        updatedLog.ValidationStatusId,
                        updatedLog.WorkflowStatusId,
                        string.Empty,
                        auditTitle,
                        auditMessage);

                    _logger.LogInformation(
                        "ExtractAndValidateDataAsync: FileLogId {Id} — {Title}",
                        fileLogId, auditTitle);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ExtractAndValidateDataAsync: Unhandled exception for FileLogId {Id}", fileLogId);

                var msg = ex.Message.Length > 500
                    ? ex.Message[..500]
                    : ex.Message;

                await _logRepo.AddErrorsAsync(fileLogId,
                    new List<string> { $"System Error: {msg}" });

                await _logRepo.UpdateStatusAsync(fileLogId,
                    V_DataError, W_NeedsCorrection, string.Empty,
                    "System Error", msg);

                throw;
            }
        }

        // -- Helpers --------------------------------------------------------------

        private static DataTable CreateDataTableFromSchema(List<string> columnNames)
        {
            var dt = new DataTable();
            foreach (var colName in columnNames)
                dt.Columns.Add(colName, typeof(string));
            return dt;
        }

        // FIX 3 & 6: Parameter is string? and return type is nullable tuple (string, string)?
        // Returning null instead of (null, null) eliminates the non-nullable mismatch.
        private static (string Proc, string TypeName)? GetTableConfig(string? targetSystemName)
        {
            return targetSystemName?.ToUpper() switch
            {
                "EMPLOYERS" => ("sp_LoadStagingEmployers", "ut_Staging_Employers"),
                "PLANS" => ("sp_LoadStagingPlans", "ut_Staging_Plans"),
                "PREMIUMS" => ("sp_LoadStagingPremiums", "ut_Staging_Premiums"),
                "EMPLOYEES" => ("sp_LoadStagingEmployees", "ut_Staging_Employee"),
                "DEPENDENTS" => ("sp_LoadStagingDependents", "ut_Staging_Dependents"),
                _ => null
            };
        }
        // =====================================================================
        // IMPORT TO LIVE TABLES  (Hangfire)
        // =====================================================================
        // The caller has already written WS.Importing and returned to the browser.
        // Everything from here on reports its outcome by moving the file's status,
        // because Hangfire marks a job Succeeded whatever happened inside it.
        public async Task ImportToLiveAsync(int fileLogId, string importMode, string userId)
        {
            var log = await _logRepo.GetLogByIdAsync(fileLogId);
            if (log == null)
            {
                _logger.LogWarning("ImportToLiveAsync: file {FileLogId} no longer exists.", fileLogId);
                return;
            }

            // If this is a retry of a run that already finished, do nothing. Cheap
            // insurance: the merge is mostly idempotent, but "mostly" is not a
            // guarantee worth relying on for a filing system.
            if (log.WorkflowStatusId == WS.Complete)
            {
                _logger.LogInformation("ImportToLiveAsync: file {FileLogId} is already complete; skipping.", fileLogId);
                return;
            }

            try
            {
                var result = await _dataRepo.ImportDataAsync(fileLogId, importMode);

                if (result.Success)
                {
                    await _logRepo.UpdateStatusAsync(
                        fileLogId, VS.Clean, WS.Complete, userId,
                        "Import Complete", "Successfully integrated into live system.");
                    _logger.LogInformation("Import finished for file {FileLogId}.", fileLogId);
                    return;
                }

                await _logRepo.UpdateStatusAsync(
                    fileLogId, VS.Failed, WS.ImportFailed, userId,
                    "Import Failed", result.Message ?? "Database rejected the merge.");
                _logger.LogWarning("Import rejected for file {FileLogId}: {Message}", fileLogId, result.Message);
            }
            catch (Exception ex)
            {
                // Land the file on a status a person can act on, then rethrow so the
                // failure is visible on the Hangfire dashboard with its stack trace.
                await _logRepo.UpdateStatusAsync(
                    fileLogId, VS.Failed, WS.ImportFailed, userId,
                    "Import Failed", "Critical system error during integration.");
                _logger.LogError(ex, "Import threw for file {FileLogId}.", fileLogId);
                throw;
            }
        }

        // =====================================================================
        // STUCK IMPORT SWEEPER  (Hangfire, recurring)
        // =====================================================================
        public async Task RecoverStuckImportsAsync(int staleMinutes = 90)
        {
            var recovered = await _dataRepo.RecoverStuckImportsAsync(staleMinutes);
            if (recovered > 0)
                _logger.LogWarning(
                    "Released {Count} file(s) stranded at Importing for more than {Minutes} minutes.",
                    recovered, staleMinutes);
        }
    }
}