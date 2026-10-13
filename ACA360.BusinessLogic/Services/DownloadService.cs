using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class DownloadService : IDownloadService
    {
        private readonly string _connectionString;
        private readonly IWebHostEnvironment _env;
        private readonly IAzureBlobService _azureBlobService;
        private readonly string _generatedReportsFolder;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DownloadService> _logger;

        /// <summary>
        /// Purpose: Initializes the DownloadService with configuration, environment, Blob storage, and logging dependencies.
        /// Input parameters: IConfiguration configuration, string connectionString, IWebHostEnvironment env, IAzureBlobService azureBlobService, ILogger logger
        /// Output/return value: None
        /// </summary>
        public DownloadService(IConfiguration configuration, string connectionString, IWebHostEnvironment env, IAzureBlobService azureBlobService, ILogger<DownloadService> logger)
        {
            try
            {
                _connectionString = connectionString;
                _env = env;
                _configuration = configuration;
                _azureBlobService = azureBlobService;
                _generatedReportsFolder = configuration["AzureBlob:GeneratedReports"] ?? "GeneratedReports";
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "Error occurred during initialization of DownloadService.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);


        /// <summary>
        /// Purpose: Retrieves the download history dashboard data, including files and filter options.
        /// Input parameters: string userId, string role
        /// Output/return value: Task of DownloadHubViewModel
        /// </summary>
        public async Task<DownloadHubViewModel> GetDownloadsDashboardAsync(
            IEnumerable<long> permittedEmployerIds, bool unrestricted)
        {
            try
            {
                var model = new DownloadHubViewModel();

                // 1. Ensure the lists are initialized before we try to use them!
                model.Files ??= new List<DownloadFileItem>();
                model.AvailableYears ??= new List<string>();
                model.AvailableEmployers ??= new List<string>();

                using var db = Connection;

                // Scoped by the caller's permitted employers. The procedure returns
                // a batch only when every employer in it is permitted — a
                // multi-employer archive contains forms for all of them, so partial
                // entitlement must not grant the download.
                var historyRecords = await db.QueryAsync<dynamic>(
                    "sp_GetDownloadHistory",
                    new
                    {
                        EmployerIDs = ToJsonIdArray(permittedEmployerIds),
                        Unrestricted = unrestricted,
                        FileName = (string?)null
                    },
                    commandType: CommandType.StoredProcedure);

                // Map Database Records to View Model
                if (historyRecords != null)
                {
                    foreach (var record in historyRecords)
                    {
                        // Safely handle potential nulls from the database
                        long fileSizeBytes = record.FileSizeBytes != null ? (long)record.FileSizeBytes : 0;

                        model.Files.Add(new DownloadFileItem
                        {
                            FileName = record.FileName?.ToString() ?? "Unknown File",
                            FilePath = string.Empty,
                            FormType = (record.FormType?.ToString() ?? "Unknown") + " Multi-Batch",
                            Year = record.TaxYear,
                            EmployerId = 0,
                            EmployerName = record.EmployerNames?.ToString() ?? "Unknown Employer",
                            CreatedDate = record.GeneratedDate ?? DateTime.Now,
                            FileSize = FormatSize(fileSizeBytes)
                        });
                    }
                }

                // Populate Filters
                model.AvailableYears = model.Files
                    .Select(f => f.Year.ToString())
                    .Distinct()
                    .OrderByDescending(y => y)
                    .ToList();

                model.AvailableEmployers = model.Files
                    .Select(f => f.EmployerName ?? string.Empty)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error occurred in GetDownloadsDashboardAsync. Unrestricted={Unrestricted}", unrestricted);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Confirms the caller may download a specific generated archive.
        /// Runs the listing predicate narrowed to one file name, so a file cannot be
        /// fetchable unless it would also be listable.
        /// Input parameters: string fileName, IEnumerable of long permittedEmployerIds, bool unrestricted
        /// Output/return value: Task of bool
        /// </summary>
        public async Task<bool> CanAccessFileAsync(
            string fileName, IEnumerable<long> permittedEmployerIds, bool unrestricted)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;

            try
            {
                using var db = Connection;

                var match = await db.QueryFirstOrDefaultAsync<string>(
                    "sp_GetDownloadHistory",
                    new
                    {
                        EmployerIDs = ToJsonIdArray(permittedEmployerIds),
                        Unrestricted = unrestricted,
                        FileName = Path.GetFileName(fileName)
                    },
                    commandType: CommandType.StoredProcedure);

                return match != null;
            }
            catch (Exception ex)
            {
                // Fail closed. An access check that cannot complete is a denial,
                // never a pass.
                _logger.LogError(ex, "Error occurred in CanAccessFileAsync for FileName: {FileName}", fileName);
                return false;
            }
        }

        /// <summary>
        /// Purpose: Renders employer ids as the JSON array sp_GetDownloadHistory
        /// parses with OPENJSON. Returns "[]" for an empty set, which the procedure
        /// treats as "entitled to nothing" rather than "no filter".
        /// Input parameters: IEnumerable of long ids
        /// Output/return value: string
        /// </summary>
        private static string ToJsonIdArray(IEnumerable<long>? ids)
        {
            var list = (ids ?? Enumerable.Empty<long>())
                       .Where(id => id > 0)
                       .Distinct()
                       .ToList();

            return list.Count == 0
                ? "[]"
                : "[" + string.Join(",", list.Select(id => id.ToString(CultureInfo.InvariantCulture))) + "]";
        }

        /// <summary>
        /// Purpose: Retrieves the file stream for a requested file name from Azure Blob Storage.
        /// Input parameters: string fileName
        /// Output/return value: Task of Stream
        /// </summary>
        public async Task<Stream> GetFileBytesAsync(string fileName)
        {
            try
            {
                // FIX: Defend against null parameters before hitting path logic
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    throw new ArgumentException("File name cannot be null or empty.", nameof(fileName));
                }

                // FIX: Path.GetFileName can technically return null if the path string is null (though we guarded it above)
                string safeFileName = Path.GetFileName(fileName) ?? string.Empty;

                var stream = await _azureBlobService.DownloadStreamAsync(_generatedReportsFolder, safeFileName);

                // FIX: "Possible null reference return."
                // If the blob service doesn't find the file, it might return null. 
                // Throw an exception so we guarantee the method signature (Task<Stream>) returns a valid Stream.
                if (stream == null)
                {
                    throw new FileNotFoundException($"The requested file '{safeFileName}' was not found in blob storage.");
                }

                return stream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetFileBytesAsync for FileName: {FileName}", fileName);
                throw;
            }
        }

        // ── REPORT 1: MONTH-BY-MONTH IRS 1095-C CODE GRID ──────────────────
        public async Task<byte[]> GenerateCodeReportAsync(int employerId, int year)
        {
            using var db = Connection;
            var data = await db.QueryAsync<dynamic>(
                "SELECT e.ssn as SSN, e.firstName as FirstName, e.lastName as LastName, " +
                "c.JAN_COC, c.JAN_LCMP, c.JAN_SHC, c.FEB_COC, c.FEB_LCMP, c.FEB_SHC, c.MAR_COC, c.MAR_LCMP, c.MAR_SHC, " +
                "c.APR_COC, c.APR_LCMP, c.APR_SHC, c.MAY_COC, c.MAY_LCMP, c.MAY_SHC, c.JUN_COC, c.JUN_LCMP, c.JUN_SHC, " +
                "c.JUL_COC, c.JUL_LCMP, c.JUL_SHC, c.AUG_COC, c.AUG_LCMP, c.AUG_SHC, c.SEP_COC, c.SEP_LCMP, c.SEP_SHC, " +
                "c.OCT_COC, c.OCT_LCMP, c.OCT_SHC, c.NOV_COC, c.NOV_LCMP, c.NOV_SHC, c.DEC_COC, c.DEC_LCMP, c.DEC_SHC " +
                "FROM EmployeeCode c JOIN Employee e ON c.employeeId = e.id " +
                "WHERE e.EmployerId = @EmployerId AND c.filingYear = @Year AND e.IsDeleted = 0",
                new { EmployerId = employerId, Year = year });

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("1095-C Calculated Codes");

            // Setup Header Row
            string[] columns = { "SSN", "First Name", "Last Name", "Jan Offer", "Jan Prem", "Jan SH", "Feb Offer", "Feb Prem", "Feb SH", "Mar Offer", "Mar Prem", "Mar SH", "Apr Offer", "Apr Prem", "Apr SH", "May Offer", "May Prem", "May SH", "Jun Offer", "Jun Prem", "Jun SH", "Jul Offer", "Jul Prem", "Jul SH", "Aug Offer", "Aug Prem", "Aug SH", "Sep Offer", "Sep Prem", "Sep SH", "Oct Offer", "Oct Prem", "Oct SH", "Nov Offer", "Nov Prem", "Nov SH", "Dec Offer", "Dec Prem", "Dec SH" };
            for (int i = 0; i < columns.Length; i++)
            {
                ws.Cells[1, i + 1].Value = columns[i];
                ws.Cells[1, i + 1].Style.Font.Bold = true;
            }

            int rowIdx = 2;
            foreach (var item in data)
            {
                var dict = (IDictionary<string, object>)item;
                ws.Cells[rowIdx, 1].Value = dict["SSN"];
                ws.Cells[rowIdx, 2].Value = dict["FirstName"];
                ws.Cells[rowIdx, 3].Value = dict["LastName"];

                int c = 4;
                string[] months = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
                foreach (var m in months)
                {
                    ws.Cells[rowIdx, c++].Value = dict[$"{m}_COC"];
                    ws.Cells[rowIdx, c++].Value = dict[$"{m}_LCMP"];
                    ws.Cells[rowIdx, c++].Value = dict[$"{m}_SHC"];
                }
                rowIdx++;
            }
            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            return package.GetAsByteArray();
        }

        // ── REPORT 2: 4980H RISK & PENALTY SUMMARY REPORT ─────────────────
        public async Task<byte[]> GeneratePenaltyReportAsync(int employerId, int year)
        {
            using var db = Connection;

            // Fetch overview totals
            var summary = await db.QueryFirstOrDefaultAsync<dynamic>(
                "sp_GetEmployerPenaltySummary", new { Id = employerId, Year = year }, commandType: CommandType.StoredProcedure);

            // Fetch list of violation row items
            var violations = await db.QueryAsync<dynamic>(
                "sp_GetEmployeePenaltyDetails", new { Id = employerId, Year = year, RateB = 360m }, commandType: CommandType.StoredProcedure);

            using var package = new ExcelPackage();
            var wsSummary = package.Workbook.Worksheets.Add("Risk Overview");
            var wsDetails = package.Workbook.Worksheets.Add("Infraction Log");

            // Tab 1: Build executive brief
            wsSummary.Cells["A1"].Value = "IRS 4980H Assessment Exposure Summary";
            wsSummary.Cells["A1"].Style.Font.Size = 16;
            wsSummary.Cells["A1"].Style.Font.Bold = true;

            wsSummary.Cells["A3"].Value = "Filing Calendar Year:"; wsSummary.Cells["B3"].Value = year;
            wsSummary.Cells["A4"].Value = "Aggregate Liability Exposure:"; wsSummary.Cells["B4"].Value = summary?.TotalEstimatedPenalty ?? 0;
            wsSummary.Cells["B4"].Style.Numberformat.Format = "$#,##0.00";
            wsSummary.Cells["A5"].Value = "Section 4980H(a) Triggered:"; wsSummary.Cells["B5"].Value = (summary?.TypeA_Triggered == true) ? "YES" : "NO";

            // Tab 2: Detailed row validation anomalies
            string[] headings = { "SSN", "First Name", "Last Name", "Month Num", "Failure Trigger Code", "Identified Issue" };
            for (int i = 0; i < headings.Length; i++)
            {
                wsDetails.Cells[1, i + 1].Value = headings[i];
                wsDetails.Cells[1, i + 1].Style.Font.Bold = true;
            }

            int rowIdx = 2;
            foreach (var v in violations)
            {
                wsDetails.Cells[rowIdx, 1].Value = v.EmployeeSSN;
                wsDetails.Cells[rowIdx, 2].Value = v.EmployeeLegalFirstName;
                wsDetails.Cells[rowIdx, 3].Value = v.EmployeeLegalLastName;
                wsDetails.Cells[rowIdx, 4].Value = v.MonthNum;
                wsDetails.Cells[rowIdx, 5].Value = v.FailureType;
                wsDetails.Cells[rowIdx, 6].Value = v.Description;
                rowIdx++;
            }

            wsSummary.Cells[wsSummary.Dimension.Address].AutoFitColumns();
            wsDetails.Cells[wsDetails.Dimension.Address].AutoFitColumns();
            return package.GetAsByteArray();
        }

        // ── REPORT 3: COMPLETE EMPLOYER MASTER REPORT (ALL SHEETS) ────────
        public async Task<byte[]> GenerateFullEmployerReportAsync(int employerId, int year)
        {
            using var db = Connection;

            var empTask = await db.QueryAsync<dynamic>("SELECT ssn as EmployeeSSN, firstName as EmployeeLegalFirstName, lastName as EmployeeLegalLastName, Status, HireDate, TerminationDate FROM Employee WHERE EmployerId = @EmployerId AND IsDeleted = 0", new { EmployerId = employerId });
            var planTask = await db.QueryAsync<dynamic>("SELECT name as PlanName, bandingType as PlanType, offeredSpouse as OfferedToSpouse, offeredDependents as OfferedToDependents, FundingType, MinimumValue FROM EmployerPlan WHERE employerId=@EmployerId", new { EmployerId = employerId });
            var depTask = await db.QueryAsync<dynamic>("SELECT d.ssn as DependentSSN, d.firstName as DependentLegalFirstName, d.lastName as DependentLegalLastName FROM CoveredIndividual d WHERE EmployerId = @EmployerId", new { EmployerId = employerId });

           // await Task.WhenAll(empTask, planTask, depTask);

            using var package = new ExcelPackage();

            // Tab 1: Staff Directory
            var ws1 = package.Workbook.Worksheets.Add("Workforce Directory");
            ws1.Cells[1, 1].Value = "SSN"; ws1.Cells[1, 2].Value = "First Name"; ws1.Cells[1, 3].Value = "Last Name"; ws1.Cells[1, 4].Value = "Classification"; ws1.Cells[1, 5].Value = "Hire Date"; ws1.Cells[1, 6].Value = "Termination Date";
            ws1.Row(1).Style.Font.Bold = true;
            int r1 = 2;
            foreach (var e in empTask)
            {
                ws1.Cells[r1, 1].Value = e.EmployeeSSN;
                ws1.Cells[r1, 2].Value = e.EmployeeLegalFirstName;
                ws1.Cells[r1, 3].Value = e.EmployeeLegalLastName;
                ws1.Cells[r1, 4].Value = (e.Status == 1) ? "Full-Time" : "Part-Time";
                ws1.Cells[r1, 5].Value = e.HireDate;
                ws1.Cells[r1, 6].Value = e.TerminationDate;
                r1++;
            }

            // Tab 2: Benefit Matrix Configurations
            var ws2 = package.Workbook.Worksheets.Add("Benefit Plan Configurations");
            ws2.Cells[1, 1].Value = "Plan Name"; ws2.Cells[1, 2].Value = "Type"; ws2.Cells[1, 3].Value = "Spouse Offer"; ws2.Cells[1, 4].Value = "Dep Offer"; ws2.Cells[1, 5].Value = "Funding Structural Type"; ws2.Cells[1, 6].Value = "Minimum Value Met";
            ws2.Row(1).Style.Font.Bold = true;
            int r2 = 2;
            foreach (var p in planTask)
            {
                ws2.Cells[r2, 1].Value = p.PlanName;
                ws2.Cells[r2, 2].Value = p.PlanType;
                ws2.Cells[r2, 3].Value = p.OfferedToSpouse;
                ws2.Cells[r2, 4].Value = p.OfferedToDependents;
                ws2.Cells[r2, 5].Value = (p.FundingType == "2") ? "Self-Insured" : "Fully Insured";
                //ws2.Cells[r2, 6].Value = (p.MinimumValue == "1") ? "Yes" : "No";
                r2++;
            }

            // Tab 3: Dependents Log
            var ws3 = package.Workbook.Worksheets.Add("Covered Dependents");
            ws3.Cells[1, 1].Value = "Dependent SSN"; ws3.Cells[1, 2].Value = "First Name"; ws3.Cells[1, 3].Value = "Last Name";
            ws3.Row(1).Style.Font.Bold = true;
            int r3 = 2;
            foreach (var d in depTask)
            {
                ws3.Cells[r3, 1].Value = d.DependentSSN;
                ws3.Cells[r3, 2].Value = d.DependentLegalFirstName;
                ws3.Cells[r3, 3].Value = d.DependentLegalLastName;
                r3++;
            }

            ws1.Cells[ws1.Dimension.Address].AutoFitColumns();
            if (ws2.Dimension != null) ws2.Cells[ws2.Dimension.Address].AutoFitColumns();
            if (ws3.Dimension != null) ws3.Cells[ws3.Dimension.Address].AutoFitColumns();

            return package.GetAsByteArray();
        }

        /// <summary>
        /// Purpose: Formats a byte size into a human-readable string (B, KB, MB, GB).
        /// Input parameters: long bytes
        /// Output/return value: string
        /// </summary>
        private string FormatSize(long bytes)
        {
            try
            {
                string[] sizes = { "B", "KB", "MB", "GB" };
                double len = (double)bytes;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in FormatSize.");
                throw;
            }
        }
    }
}