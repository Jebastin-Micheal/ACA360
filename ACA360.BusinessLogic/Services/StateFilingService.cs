using System.Data;
using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>State filings (script 072): who among each state's residents had coverage. Reports and Excel only.</summary>
    public interface IStateFilingService
    {
        Task<List<StateFilingSummaryRow>> SummaryAsync(int userId, int year);
        Task<(List<StateFilingResident> Residents, List<StateFilingDependent> Dependents)> ResidentsAsync(long employerId, int year, string state);
        /// <summary>The workbook: residents, then dependents. SSNs in full only when <paramref name="fullSsn"/>.</summary>
        Task<byte[]> ExportAsync(long employerId, string? employerName, int year, string state, bool fullSsn);
    }

    public class StateFilingService : IStateFilingService
    {
        private readonly string _connectionString;

        public StateFilingService(string connectionString) => _connectionString = connectionString;

        public async Task<List<StateFilingSummaryRow>> SummaryAsync(int userId, int year)
        {
            using var db = new SqlConnection(_connectionString);
            return (await db.QueryAsync<StateFilingSummaryRow>("dbo.sp_StateFilings_Summary", new { UserId = userId, Year = year },
                commandType: CommandType.StoredProcedure, commandTimeout: 120)).ToList();
        }

        public async Task<(List<StateFilingResident> Residents, List<StateFilingDependent> Dependents)> ResidentsAsync(long employerId, int year, string state)
        {
            using var db = new SqlConnection(_connectionString);
            using var grid = await db.QueryMultipleAsync("dbo.sp_StateFilings_Residents", new { EmployerId = employerId, Year = year, State = state },
                commandType: CommandType.StoredProcedure, commandTimeout: 120);
            return ((await grid.ReadAsync<StateFilingResident>()).ToList(), (await grid.ReadAsync<StateFilingDependent>()).ToList());
        }

        public static string Mask(string? ssn, bool full)
        {
            if (string.IsNullOrWhiteSpace(ssn)) return "";
            if (full) return ssn;
            var digits = new string(ssn.Where(char.IsDigit).ToArray());
            return digits.Length >= 4 ? "XXX-XX-" + digits[^4..] : "XXX-XX-XXXX";
        }

        private static string Months(string? list) => string.IsNullOrEmpty(list) ? "" :
            list.Split(',', StringSplitOptions.RemoveEmptyEntries).Length == 12 ? "All 12 months" :
            string.Join(", ", list.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(m => int.TryParse(m, out var n) && n is >= 1 and <= 12
                    ? System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(n) : m));

        public async Task<byte[]> ExportAsync(long employerId, string? employerName, int year, string state, bool fullSsn)
        {
            var (residents, dependents) = await ResidentsAsync(employerId, year, state);
            using var package = new ExcelPackage();

            var ws = package.Workbook.Worksheets.Add("Residents");
            ws.Cells[1, 1].Value = $"{StateFilingStates.NameOf(state)} residents: {employerName} ({year})";
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[2, 1].Value = "Self-insured = the employer reports this coverage; insured plans are reported by the insurer. Confirm each state's current rules before filing.";
            string[] head = { "Last name", "First name", "Middle", "SSN", "Birth date", "Address", "City", "State", "ZIP", "Plan", "Self-insured", "Months covered", "Covered dependents" };
            for (var c = 0; c < head.Length; c++) ws.Cells[4, c + 1].Value = head[c];
            var row = 5;
            var depCount = dependents.GroupBy(d => d.EmployeeId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var r in residents)
            {
                ws.Cells[row, 1].Value = r.LastName;
                ws.Cells[row, 2].Value = r.FirstName;
                ws.Cells[row, 3].Value = r.MiddleName;
                ws.Cells[row, 4].Value = Mask(r.Ssn, fullSsn);
                ws.Cells[row, 5].Value = r.Birthdate;
                ws.Cells[row, 5].Style.Numberformat.Format = "mm/dd/yyyy";
                ws.Cells[row, 6].Value = r.Address;
                ws.Cells[row, 7].Value = r.City;
                ws.Cells[row, 8].Value = r.State;
                ws.Cells[row, 9].Value = r.Zip;
                ws.Cells[row, 10].Value = r.PlanName;
                ws.Cells[row, 11].Value = r.SelfInsured ? "Yes" : "No";
                ws.Cells[row, 12].Value = string.IsNullOrEmpty(r.CoveredMonths) ? "Not covered" : Months(r.CoveredMonths);
                ws.Cells[row, 13].Value = depCount.TryGetValue(r.EmployeeId, out var n) ? n : 0;
                row++;
            }
            Style(ws, 4, head.Length, row - 1);

            var wd = package.Workbook.Worksheets.Add("Dependents");
            string[] dhead = { "Employee", "Last name", "First name", "Middle", "SSN", "Birth date", "Relationship", "Months covered" };
            for (var c = 0; c < dhead.Length; c++) wd.Cells[1, c + 1].Value = dhead[c];
            var names = residents.ToDictionary(r => r.EmployeeId, r => $"{r.LastName}, {r.FirstName}");
            row = 2;
            foreach (var d in dependents)
            {
                wd.Cells[row, 1].Value = names.TryGetValue(d.EmployeeId, out var name) ? name : d.EmployeeId.ToString();
                wd.Cells[row, 2].Value = d.LastName;
                wd.Cells[row, 3].Value = d.FirstName;
                wd.Cells[row, 4].Value = d.MiddleName;
                wd.Cells[row, 5].Value = Mask(d.Ssn, fullSsn);
                wd.Cells[row, 6].Value = d.Birthdate;
                wd.Cells[row, 6].Style.Numberformat.Format = "mm/dd/yyyy";
                wd.Cells[row, 7].Value = d.Relationship;
                wd.Cells[row, 8].Value = Months(d.CoveredMonths);
                row++;
            }
            Style(wd, 1, dhead.Length, row - 1);
            return await package.GetAsByteArrayAsync();
        }

        private static void Style(ExcelWorksheet ws, int headerRow, int columns, int lastRow)
        {
            using (var h = ws.Cells[headerRow, 1, headerRow, columns])
            {
                h.Style.Font.Bold = true;
                h.Style.Fill.PatternType = ExcelFillStyle.Solid;
                h.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(232, 234, 246));
            }
            if (lastRow > headerRow) ws.Cells[headerRow, 1, lastRow, columns].AutoFilter = true;
            ws.View.FreezePanes(headerRow + 1, 1);
            ws.Cells[headerRow, 1, Math.Max(headerRow, lastRow), columns].AutoFitColumns(8, 45);
        }
    }
}
