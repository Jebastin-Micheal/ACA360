using System.IO.Compression;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using OfficeOpenXml;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>
    /// Builds the Dummy Data page's downloads with <see cref="DummyDataGenerator"/>. Reads employers
    /// and EINs only; never writes to the database.
    /// </summary>
    public class DummyDataService : IDummyDataService
    {
        private readonly string _connectionString;

        public DummyDataService(string connectionString) => _connectionString = connectionString;

        public async Task<List<DummyEmployer>> GetMainEmployersAsync()
        {
            using var db = new SqlConnection(_connectionString);
            var rows = await db.QueryAsync<(int Id, string Name, string Ein, int? FilingYear)>(
                @"SELECT id, name, taxid, filingYear FROM dbo.Employer
                  WHERE ISNULL(companyId, 0) = 0 AND ISNULL(IsDeleted, 0) = 0
                    AND LTRIM(RTRIM(ISNULL(taxid, ''))) <> '' AND LTRIM(RTRIM(ISNULL(name, ''))) <> ''");
            return rows
                .GroupBy(r => Digits(r.Ein))
                .Where(g => g.Key.Length == 9)
                .Select(g => g.OrderByDescending(r => r.FilingYear ?? 0).ThenByDescending(r => r.Id).First())
                .Select(r => new DummyEmployer(r.Id, r.Name.Trim(), DummyDataGenerator.FormatEin(r.Ein)))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<List<int>> GetFilingYearsAsync()
        {
            using var db = new SqlConnection(_connectionString);
            // The years in System Settings > Filing Year.
            return (await db.QueryAsync<int>(
                "SELECT DISTINCT filingYear FROM dbo.FilingYear WHERE ISNULL(IsActive, 1) = 1 AND filingYear BETWEEN 2000 AND 2100 ORDER BY filingYear DESC")).ToList();
        }

        public async Task<(byte[] Content, string FileName, string ContentType, List<DummyFileSummary> Files)> GenerateAsync(
            DummyDataRequest request, string templatePath)
        {
            var problems = DummyDataGenerator.Check(request);
            if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems));

            var ids = request.EmployerIds.Distinct().ToList();
            var chosen = (await GetMainEmployersAsync()).Where(e => ids.Contains(e.Id)).ToList();
            if (chosen.Count != ids.Count)
                throw new ArgumentException("One of the chosen employers is not a main employer with an EIN.");

            HashSet<string> takenEins;
            using (var db = new SqlConnection(_connectionString))
                takenEins = (await db.QueryAsync<string>("SELECT DISTINCT taxid FROM dbo.Employer WHERE taxid IS NOT NULL"))
                    .Select(Digits).Where(d => d.Length > 0).ToHashSet();
            var usedSsns = new HashSet<string>();

            var seed = request.Seed ?? Random.Shared.Next();
            // New names exist only in the file, with a made-up EIN no employer in ACA360 has.
            var einRandom = new Random(unchecked(seed - 1));
            chosen.AddRange(DummyDataGenerator.NewNames(request.NewEmployerNames)
                .Select(name => new DummyEmployer(0, name, DummyDataGenerator.NewEin(einRandom, takenEins))));
            var files = new List<(DummyFileSummary Summary, byte[] Content)>();
            foreach (var (employer, index) in chosen.Select((e, i) => (e, i)))
            {
                var book = DummyDataGenerator.Generate(request, employer, new Random(unchecked(seed + index * 7919)), takenEins, usedSsns);
                files.Add((book.Summary, Write(book, templatePath)));
            }

            if (files.Count == 1)
                return (files[0].Content, files[0].Summary.FileName,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", new() { files[0].Summary });

            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var (summary, content) in files)
                {
                    var entry = zip.CreateEntry(summary.FileName, System.IO.Compression.CompressionLevel.Fastest);
                    using var s = entry.Open();
                    s.Write(content);
                }
            return (zipStream.ToArray(), $"DummyData_{files.Count}_employers_{request.FilingYear}.zip", "application/zip",
                    files.Select(f => f.Summary).ToList());
        }

        /// <summary>Writes the rows into a copy of the import template, matching columns by their (trimmed) header text.</summary>
        public static byte[] Write(DummyWorkbook book, string templatePath)
        {
            using var package = new ExcelPackage(new FileInfo(templatePath));
            foreach (var (sheetName, rows) in book.Sheets)
            {
                var ws = package.Workbook.Worksheets[sheetName]
                         ?? throw new InvalidOperationException($"The import template has no sheet '{sheetName}'.");
                int lastColumn = ws.Dimension?.End.Column ?? 0;
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 1; c <= lastColumn; c++)
                {
                    var text = ws.Cells[1, c].Text?.Trim();
                    if (!string.IsNullOrEmpty(text)) headers[text] = c;
                }
                // The template carries example rows: start from an empty sheet.
                if (ws.Dimension != null && ws.Dimension.End.Row > 1)
                    ws.DeleteRow(2, ws.Dimension.End.Row - 1);
                if (rows.Count == 0) continue;

                var data = new List<object?[]>(rows.Count);
                foreach (var row in rows)
                {
                    var values = new object?[lastColumn];
                    foreach (var (header, value) in row)
                    {
                        if (!headers.TryGetValue(header, out var col))
                            throw new InvalidOperationException($"Sheet '{sheetName}' of the import template has no column '{header}'.");
                        values[col - 1] = value;
                    }
                    data.Add(values);
                }
                ws.Cells[2, 1].LoadFromArrays(data!);
            }
            return package.GetAsByteArray();
        }

        private static string Digits(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());
    }
}
