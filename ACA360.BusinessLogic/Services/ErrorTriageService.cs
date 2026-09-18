using ACA360.Core.Helpers;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class ErrorTriageService : IErrorTriageService
    {
        private readonly IDataTriageRepository _triageRepo;
        private readonly IFileUploadLogService _logRepo;
        private readonly IAzureBlobService _azureBlobService;
        private readonly string _uploadsFolder;

        public ErrorTriageService(
            IDataTriageRepository triageRepo,
            IFileUploadLogService logRepo,
            IConfiguration config,
            IAzureBlobService azureBlobService)
        {
            _triageRepo = triageRepo;
            _logRepo = logRepo;
            _azureBlobService = azureBlobService;
            // FIX: correct config key (was "AzureBlob:Uploadsfiles")
            _uploadsFolder = config["AzureBlob:UploadsFolder"] ?? "Uploadsfiles";
        }

        // -- Staging row loader (whitelist prevents SQL injection) -------------
        public async Task<object> GetStagingRowAsync(string tableName, long rowId)
        {
            return tableName.ToLower() switch
            {
                "staging_employees" => await _triageRepo.GetStagingEmployeeByIdAsync(rowId),
                "staging_employers" => await _triageRepo.GetStagingEmployerByIdAsync(rowId),
                "staging_plans" => await _triageRepo.GetStagingPlanByIdAsync(rowId),
                "staging_premiums" => await _triageRepo.GetStagingPremiumByIdAsync(rowId),
                "staging_dependents" => await _triageRepo.GetStagingDependentByIdAsync(rowId),
                _ => null
            };
        }

        // -- Main triage view model builder ------------------------------------
        public async Task<ErrorTriageViewModel> BuildTriageViewModelAsync(
            int fileLogId, PaginationEntity paginationEntity, string severity)
        {
            var fileLog = await _logRepo.GetLogByIdAsync(fileLogId);
            if (fileLog == null) return null;

            var (errors, pageInfo, counts) =
                await _triageRepo.GetTriageErrorsAsync(fileLogId, paginationEntity, severity);

            // Enrich each error with the current bad value from the staging row
            foreach (var error in errors)
            {
                var stagingRow = await GetStagingRowAsync(error.StagingTableName, error.StagingRowId);
                if (stagingRow != null)
                {
                    var prop = stagingRow.GetType().GetProperty(error.TargetColumn);
                    error.BadValue = prop?.GetValue(stagingRow)?.ToString();
                }
            }

            return new ErrorTriageViewModel
            {
                FileLogId = fileLog.FileLogId,
                OriginalFileName = fileLog.OriginalFileName,
                EmployerName = fileLog.EmployerName,
                TaxId = fileLog.TaxID,
                WorkflowStatusId = fileLog.WorkflowStatusId ?? 0,
                Errors = errors,
                ErrorCount = counts.ErrorCount,
                WarningCount = counts.WarningCount,
                InformationCount = counts.InformationCount,
                EmployerActionCount = counts.EmployerActionCount,
                TotalErrorsCount = counts.TotalErrorsCount,
                TotalRowsValidated = fileLog.TotalRowsValidated ?? 0,
                EmployeesWithErrors = fileLog.FatalErrorCount ?? 0,
                ErrorPercentage = fileLog.ErrorPercentage ?? 0,
                Metadata = pageInfo
            };
        }

        // -- Edit row view model -----------------------------------------------
        public async Task<EditRowViewModel> BuildEditRowViewModelAsync(
            int fileLogId, string tableName, long rowId)
        {
            var rowDataObj = await GetStagingRowAsync(tableName, rowId);

            var rowDataDict = new Dictionary<string, object>();
            if (rowDataObj != null)
            {
                foreach (var prop in rowDataObj.GetType().GetProperties())
                    rowDataDict[prop.Name] = prop.GetValue(rowDataObj);
            }

            var allErrors = await _triageRepo.GetStagingErrorsByRowIdAsync(fileLogId, tableName, rowId);

            return new EditRowViewModel
            {
                FileLogId = fileLogId,
                TableName = tableName,
                RowId = rowId,
                RowData = rowDataDict,
                AllErrorsForThisRow = allErrors.ToList()
            };
        }

        // -- Update field + revalidate -----------------------------------------
        // FIX: returns newErrorCount so the JS can update the badge immediately
        public async Task<UpdateRowResult> UpdateFieldAndRevalidateAsync(UpdateFieldPayload payload)
        {
            var result = new UpdateRowResult { IsSuccess = true };
            try
            {
                // Step 1: persist all changed fields to the staging row
                foreach (var field in payload.Values)
                {
                    if (field.Key == "__RequestVerificationToken") continue;

                    await _triageRepo.UpdateSingleFieldOnlyAsync(
                        payload.FileLogId,
                        payload.TableName,
                        payload.RowId,
                        field.Key,
                        field.Value);
                }

                // Step 2: re-run the full validation engine for this file
                await _triageRepo.RunValidationEngineAsync(payload.FileLogId);

                // Step 3: return the live error count so the dashboard can
                //         update the badge and decide whether to unlock "Send for Approval"
                var counts = await _triageRepo.GetErrorCountsAsync(payload.FileLogId);
                result.NewErrorCount = counts.ErrorCount;
                result.NewWarningCount = counts.WarningCount;

                return result;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Errors = new List<string> { ex.Message };
                return result;
            }
        }

        // -- Skip / update error status ----------------------------------------
        public async Task UpdateErrorStatusAsync(long stagingRowErrorId, string status)
        {
            await _triageRepo.UpdateErrorStatusAsync(stagingRowErrorId, status);
        }

        // SendForApprovalAsync was removed with the AM authorisation step. Its one piece of
        // real logic — refusing to hand on a file that still has fatal errors — was moved to
        // FileUploadController.ImportData, which is now the last gate before the live tables.

        // ----------------------------------------------------------------------
        // AUDIT REPORT
        // ----------------------------------------------------------------------
        public async Task<MemoryStream> GenerateErrorReportAsync(int fileLogId)
        {
            var fileLog = await _logRepo.GetLogByIdAsync(fileLogId);
            if (fileLog == null) throw new FileNotFoundException("File log not found.");

            // Download source file from Azure (or local fallback)
            var sourceStream = new MemoryStream();
            try
            {
                if (fileLog.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    using var azureStream = await _azureBlobService.DownloadStreamAsync(
                        _uploadsFolder, fileLog.StoredFileName);
                    if (azureStream == null)
                        throw new FileNotFoundException("Original file not found in Azure Storage.");
                    await azureStream.CopyToAsync(sourceStream);
                }
                else
                {
                    if (!File.Exists(fileLog.FilePath))
                        throw new FileNotFoundException("Original file not found on disk.");
                    using var fs = new FileStream(fileLog.FilePath, FileMode.Open, FileAccess.Read);
                    await fs.CopyToAsync(sourceStream);
                }
                sourceStream.Position = 0;
            }
            catch
            {
                await sourceStream.DisposeAsync();
                throw;
            }

            var errors = (await _triageRepo.GetErrorsForReportAsync(fileLogId)).ToList();
            var outputStream = new MemoryStream();

            using (sourceStream)
            using (var package = new ExcelPackage(sourceStream))
            {
                // Dynamic header scan � finds column positions by name, not hardcoded index
                var sheetColumnMappings =
                    new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

                foreach (var sheet in package.Workbook.Worksheets)
                {
                    var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    if (sheet.Dimension != null)
                    {
                        for (int col = 1; col <= sheet.Dimension.End.Column; col++)
                        {
                            var hdr = sheet.Cells[1, col].Text.Trim();
                            if (!string.IsNullOrEmpty(hdr) && !colMap.ContainsKey(hdr))
                                colMap[hdr] = col;
                        }
                    }
                    sheetColumnMappings[sheet.Name] = colMap;
                }

                var errorColor = Color.Red;
                var warningColor = Color.Yellow;
                var infoColor = Color.SkyBlue;

                // Highlight cells in original sheets
                foreach (var error in errors)
                {
                    var sheetName = ResolveSheetName(error.StagingTableName, package);
                    var worksheet = package.Workbook.Worksheets[sheetName];
                    if (worksheet == null) continue;

                    int column = FindColumnIndex(sheetColumnMappings, sheetName, error.TargetColumn);
                    int row = error.RowNumber;

                    if (column > 0 && row > 0 && row <= worksheet.Dimension.Rows)
                    {
                        var cell = worksheet.Cells[row, column];
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;

                        if (error.Severity == "Error")
                            cell.Style.Fill.BackgroundColor.SetColor(errorColor);
                        else if (error.Severity == "Warning")
                            cell.Style.Fill.BackgroundColor.SetColor(warningColor);
                        else
                            cell.Style.Fill.BackgroundColor.SetColor(infoColor);
                    }
                }

                // Audit tab
                var auditSheet = package.Workbook.Worksheets.Add(
                    $"Audit Results {fileLog.AnalysisDate:MM-dd-yyyy}");
                auditSheet.TabColor = Color.Blue;
                SetupAuditTabHeaders(auditSheet);

                int logRow = 9;
                string[] tableOrder =
                {
                    "staging_employers", "staging_plans", "staging_premiums",
                    "staging_employees", "staging_dependents"
                };

                foreach (var table in tableOrder)
                {
                    var tableErrors = errors
                        .Where(e => e.StagingTableName.Equals(table, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    auditSheet.Cells[logRow, 1].Value = $"Errors in {table.Replace("staging_", "")}:";
                    auditSheet.Cells[logRow, 1].Style.Font.Bold = true;
                    auditSheet.Cells[logRow, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    auditSheet.Cells[logRow, 1].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                    logRow++;

                    if (tableErrors.Any())
                    {
                        var sheetName = ResolveSheetName(table, package);
                        var srcSheet = package.Workbook.Worksheets[sheetName];

                        if (srcSheet?.Dimension != null)
                        {
                            var groupedErrors = tableErrors.GroupBy(e => e.AuditTabNote);

                            foreach (var group in groupedErrors)
                            {
                                var rowsData = new List<List<string>>();
                                foreach (var err in group)
                                {
                                    var rowValues = Enumerable
                                        .Range(1, srcSheet.Dimension.End.Column)
                                        .Select(col => srcSheet.Cells[err.RowNumber, col]?.Text ?? "")
                                        .ToList();
                                    rowsData.Add(rowValues);
                                }

                                // Merge description column
                                int startRow = logRow + 1;
                                int endRow = logRow + rowsData.Count;
                                var merged = auditSheet.Cells[startRow, 1, endRow, 1];
                                merged.Merge = true;
                                merged.Value = group.Key;
                                merged.Style.WrapText = true;
                                merged.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
                                merged.Style.Border.BorderAround(ExcelBorderStyle.Thin);

                                var headers = srcSheet.Cells[1, 1, 1, srcSheet.Dimension.End.Column]
                                    .Select(c => c.Text).ToList();
                                headers.Add("Client Response");

                                auditSheet.Cells[logRow, 2].LoadFromArrays(
                                    new object[][] { headers.ToArray() });

                                var headerRange = auditSheet.Cells[logRow, 1, logRow, headers.Count + 1];
                                headerRange.Style.Font.Bold = true;
                                headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                headerRange.Style.Fill.BackgroundColor.SetColor(Color.DarkBlue);
                                headerRange.Style.Font.Color.SetColor(Color.White);
                                auditSheet.Cells[logRow, headers.Count + 1].Style.Fill
                                    .BackgroundColor.SetColor(Color.Green);

                                logRow++;

                                for (int i = 0; i < rowsData.Count; i++)
                                {
                                    var rData = rowsData[i];
                                    var err = group.ElementAt(i);
                                    rData.Add(""); // empty client response
                                    auditSheet.Cells[logRow, 2].LoadFromArrays(
                                        new object[][] { rData.ToArray() });

                                    int colIdx = FindColumnIndex(sheetColumnMappings, sheetName, err.TargetColumn);
                                    if (colIdx > 0)
                                    {
                                        var cell = auditSheet.Cells[logRow, colIdx + 1];
                                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                        if (err.Severity == "Error")
                                            cell.Style.Fill.BackgroundColor.SetColor(errorColor);
                                        else if (err.Severity == "Warning")
                                            cell.Style.Fill.BackgroundColor.SetColor(warningColor);
                                        else
                                            cell.Style.Fill.BackgroundColor.SetColor(infoColor);
                                    }
                                    logRow++;
                                }
                                logRow++;
                            }
                        }
                    }
                    else
                    {
                        auditSheet.Cells[logRow, 1].Value = "--- NO ERRORS ---";
                        logRow += 2;
                    }
                    logRow++;
                }

                // Summary footer
                logRow += 2;
                AddSummaryRow(auditSheet, ref logRow,
                    $"Audit Summary {fileLog.AnalysisDate:MM-dd-yyyy}", Color.DarkBlue, Color.White, true);
                AddSummaryRow(auditSheet, ref logRow,
                    "File Name: " + fileLog.OriginalFileName, Color.White, Color.Black, true);
                AddSummaryRow(auditSheet, ref logRow,
                    "Analysis Date: " + (fileLog.AnalysisDate?.ToString("g") ?? "-"), Color.White, Color.Black, true);
                AddSummaryRow(auditSheet, ref logRow,
                    "Total Employees With Errors: " + (fileLog.FatalErrorCount ?? 0), Color.White, Color.Black, true);
                AddSummaryRow(auditSheet, ref logRow,
                    "Total Employees: " + (fileLog.TotalRowsValidated ?? 0), Color.White, Color.Black, true);
                AddSummaryRow(auditSheet, ref logRow,
                    $"Employee Error Percentage: {(fileLog.ErrorPercentage ?? 0):F2}%", Color.White, Color.Black, true);

                auditSheet.Cells.AutoFitColumns();
                auditSheet.Column(1).Width = 61;

                package.SaveAs(outputStream);
            }

            outputStream.Position = 0;
            return outputStream;
        }

        // ----------------------------------------------------------------------
        // PRIVATE HELPERS
        // ----------------------------------------------------------------------

        private string ResolveSheetName(string tableName, ExcelPackage package) =>
            tableName.ToLower() switch
            {
                "staging_employers" => "Employer Tab",
                "staging_employees" => "Employee",
                "staging_plans" => "Plan Information",
                "staging_premiums" => "Premium",
                "staging_dependents" => "Dependents",
                _ => package.Workbook.Worksheets.FirstOrDefault()?.Name
            };

        private int FindColumnIndex(
            Dictionary<string, Dictionary<string, int>> mappings,
            string sheetName, string dbColumn)
        {
            if (!mappings.TryGetValue(sheetName, out var sheetMap)) return 0;
            if (sheetMap.TryGetValue(dbColumn, out int idx)) return idx;
            var header = MapDbColumnToExcelHeader(dbColumn);
            if (sheetMap.TryGetValue(header, out int idx2)) return idx2;
            return 0;
        }

        private string MapDbColumnToExcelHeader(string dbCol) => dbCol.ToLower() switch
        {
            "primaryein" => "Primary EIN",
            "affiliatedein" => "Affiliated EIN",
            "employername" => "Employer Name",
            "foreignaddressind" => "Foreign Address Ind",
            "address" => "Address",
            "address2" => "Address 2",
            "city" => "City",
            "stateprovince" => "State/Province",
            "zip" => "Zip",
            "country" => "Country",
            "phone" => "Phone",
            "contact" => "Contact",
            "title" => "Title",
            "origincode" => "Origin Code",
            "shopidentifier" => "SHOP Identifier",
            "notes" => "Notes",
            "planname" => "Plan Name",
            "plantype" => "Plan Type",
            "offeredtospouse" => "Offered To Spouse",
            "offeredtodependents" => "Offered To Dependents",
            "waitingperiodnumberofdays" => "Waiting Period (Number Of Days)",
            "eligiblefirstofthemonth" => "Eligible First Of The Month",
            "fundingtype" => "Funding Type",
            "planrenewalmonth" => "Plan Renewal Month",
            "planterminatesondateoftermination" => "Plan Terminates On Date Of Termination",
            "minimumvalue" => "Minimum Value",
            "bandingtype" => "Banding Type",
            "premiumcap" => "Premium Cap",
            "start" => "Start",
            "end" => "End",
            "startdate" => "Start Date",
            "enddate" => "End Date",
            "eemonthlycontribution" => "EE Monthly Contribution",
            "einassociatedwithee" => "EIN Associated With EE",
            "employeelegalfirstname" => "Employee Legal First Name",
            "employeemiddleinitial" => "Employee Middle Initial",
            "employeelegallastname" => "Employee Legal Last Name",
            "employeessn" => "Employee SSN",
            "employeebirthdate" => "Employee Birthdate",
            "status" => "Status",
            "hiredate" => "Hire Date",
            "terminationdate" => "Termination Date",
            "coverageelected" => "Coverage Elected",
            "coveragestartdate" => "Coverage Start Date",
            "coverageenddate" => "Coverage End Date",
            "dependentlegalfirstname" => "Dependent Legal First Name",
            "dependentlegallastname" => "Dependent Legal Last Name",
            "dependentssn" => "Dependent SSN",
            "dependentbirthdate" => "Dependent Birthdate",
            "dependentcoveragestartdate" => "Dependent Coverage Start Date",
            "dependentcoverageenddate" => "Dependent Coverage End Date",
            "relationship" => "Relationship",
            _ => dbCol
        };

        private void SetupAuditTabHeaders(ExcelWorksheet sheet)
        {
            sheet.Cells["A1"].Value = "Questions & Notes:";
            sheet.Cells["A1"].Style.Font.Bold = true;
            sheet.Cells["A1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A1"].Style.Fill.BackgroundColor.SetColor(Color.SkyBlue);
            sheet.Cells["A1"].Style.Font.Size = 14;

            sheet.Cells["B1"].Value = "Audit questions will be repeated if not answered completely.";
            sheet.Cells["B1"].Style.Font.Bold = true;
            sheet.Cells["B1"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["B1"].Style.Fill.BackgroundColor.SetColor(Color.White);
            sheet.Cells["B1"].Style.Font.Size = 14;

            sheet.View.FreezePanes(2, 1);

            sheet.Cells["A3"].Value = "Audit Tab Key";
            sheet.Cells["A3"].Style.Font.Bold = true;
            sheet.Cells["A3"].Style.Font.Color.SetColor(Color.White);
            sheet.Cells["A3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A3"].Style.Fill.BackgroundColor.SetColor(Color.Black);

            sheet.Cells["A4"].Value = "Yellow Highlight: Missing required information (e.g. missing hire date).";
            sheet.Cells["A4"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A4"].Style.Fill.BackgroundColor.SetColor(Color.Yellow);

            sheet.Cells["A5"].RichText.Add("Red Font:").Color = Color.Red;
            sheet.Cells["A5"].RichText.Add(" Data needs clarification but is populated.").Color = Color.Black;
            sheet.Cells["A5"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A5"].Style.Fill.BackgroundColor.SetColor(Color.White);

            sheet.Cells["A7"].RichText.Add("Note to Client:").Bold = true;
            sheet.Cells["A7"].RichText.Add(" Use a different color font for any changes. Use the green Client Response field to reply.");
            sheet.Cells["A7"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A7"].Style.Fill.BackgroundColor.SetColor(Color.White);
            sheet.Column(1).Width = 60;

            for (int r = 1; r <= 7; r++)
            {
                sheet.Row(r).CustomHeight = true;
                sheet.Cells[r, 1].Style.WrapText = true;
            }
        }

        private void AddSummaryRow(ExcelWorksheet sheet, ref int row,
            string text, Color bg, Color font, bool merge)
        {
            sheet.Cells[row, 1].Value = text;
            if (merge) sheet.Cells[row, 1, row, 2].Merge = true;
            sheet.Cells[row, 1].Style.Font.Bold = true;
            sheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            sheet.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(bg);
            sheet.Cells[row, 1].Style.Font.Color.SetColor(font);
            sheet.Cells[row, 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
            row++;
        }
    }
}
