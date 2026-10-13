using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using ExcelDataReader;
using Microsoft.Extensions.Logging;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>
    /// Compares the period the uploaded data describes against the filing year the
    /// user chose on the upload form.
    ///
    /// The filing-year dropdown pre-selects the current year, and the only existing
    /// guard warns when the selection is NOT the current year — so the one case it
    /// cannot catch is the default being wrong, which for a product that files
    /// prior-year returns is the common case. This closes that.
    /// </summary>
    public class PlanYearScanService : IPlanYearScanService
    {
        private readonly ILogger<PlanYearScanService> _logger;

        public PlanYearScanService(ILogger<PlanYearScanService> logger)
        {
            _logger = logger;
            // ExcelDataReader needs this for the legacy .xls code pages.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        // Sheet + column names as they appear in the client template. Matched after
        // whitespace collapsing and case-insensitively, the same way the extractor
        // in DataProcessingService matches them.
        private const string PremiumSheet = "Premium";
        private const string EmployeeSheet = "Employee";

        public PlanYearScanResult Scan(Stream stream, int planYear)
        {
            var result = new PlanYearScanResult { PlanYear = planYear };

            if (stream == null || planYear <= 0) return result;

            var yearStart = new DateTime(planYear, 1, 1);
            var yearEnd = new DateTime(planYear, 12, 31);
            var years = new HashSet<int>();

            try
            {
                using var reader = ExcelReaderFactory.CreateReader(stream);

                do
                {
                    var sheet = (reader.Name ?? "").Trim();
                    bool isPremium = sheet.Equals(PremiumSheet, StringComparison.OrdinalIgnoreCase);
                    bool isEmployee = sheet.Equals(EmployeeSheet, StringComparison.OrdinalIgnoreCase);
                    if (!isPremium && !isEmployee) continue;

                    if (!reader.Read()) continue;   // header row
                    var header = BuildHeaderMap(reader);

                    var pairs = isPremium
                        ? new[] { ("Start Date", "End Date") }
                        : new[]
                        {
                            ("Coverage Start Date", "Coverage End Date"),
                            ("COBRA Coverage Effective Date", "COBRA Coverage End Date"),
                            ("Retiree Plan Start Date", "Retiree Plan End Date")
                        };

                    while (reader.Read())
                    {
                        foreach (var (startCol, endCol) in pairs)
                        {
                            if (!header.TryGetValue(Norm(startCol), out var si)) continue;

                            var start = ReadDate(reader, si);
                            if (start == null) continue;                 // no period on this row

                            // Column 0 is a legitimate index, so the lookup result has to
                            // gate this rather than a >0 test on the index itself.
                            DateTime? end = header.TryGetValue(Norm(endCol), out var ei)
                                ? ReadDate(reader, ei)
                                : null;

                            years.Add(start.Value.Year);
                            if (end != null) years.Add(end.Value.Year);

                            // An open end date means the period is still running, so it
                            // reaches any later year.
                            bool covers = start.Value <= yearEnd && (end == null || end.Value >= yearStart);

                            if (isPremium)
                            {
                                result.PremiumRowsFound++;
                                if (covers) result.PremiumRowsCoveringYear++;
                                if (result.PremiumEarliest == null || start < result.PremiumEarliest)
                                    result.PremiumEarliest = start;
                                if (end != null && (result.PremiumLatest == null || end > result.PremiumLatest))
                                    result.PremiumLatest = end;
                            }
                            else
                            {
                                result.CoverageRowsFound++;
                                if (covers) result.CoverageRowsCoveringYear++;
                            }
                        }
                    }
                } while (reader.NextResult());

                result.Scanned = true;
            }
            catch (Exception ex)
            {
                // A file this cannot read is the structure validator's problem, not ours.
                // Say nothing and let the upload proceed.
                _logger.LogWarning(ex, "Plan-year scan could not read the upload; skipping the year check.");
                return new PlanYearScanResult { PlanYear = planYear, Scanned = false };
            }

            result.YearsInFile = years.Where(y => y >= 1990 && y <= DateTime.UtcNow.Year + 5)
                                      .OrderBy(y => y).ToList();

            BuildWarnings(result, planYear);
            return result;
        }

        private static void BuildWarnings(PlanYearScanResult r, int planYear)
        {
            // The decisive test. sp_Generate1095Codes builds its premium lookup from
            // the filing year's twelve months; a banding window that misses them all
            // leaves every Line 15 blank and makes 2F, 2G and 2H unreachable, because
            // all three are decided by comparing against that premium.
            if (r.PremiumRowsFound > 0 && r.PremiumRowsCoveringYear == 0)
            {
                var span = r.PremiumEarliest == null
                    ? "an unreadable range"
                    : $"{r.PremiumEarliest:d MMM yyyy} to " +
                      (r.PremiumLatest == null ? "open" : $"{r.PremiumLatest:d MMM yyyy}");

                r.Warnings.Add(
                    $"None of the {r.PremiumRowsFound} premium rows cover {planYear}. They run {span}. " +
                    $"Filed as {planYear}, every 1095-C will have an empty Line 15 and no affordability " +
                    $"safe harbour code on Line 16.");
            }

            if (r.CoverageRowsFound > 0 && r.CoverageRowsCoveringYear == 0)
            {
                r.Warnings.Add(
                    $"None of the {r.CoverageRowsFound} coverage, COBRA or retiree periods in this file " +
                    $"fall in {planYear}. Every employee will be reported as not employed for all twelve months.");
            }

            if (r.Warnings.Count > 0 && r.YearsInFile.Count > 0)
            {
                // Suggest the year the premium window sits in when there is one, since
                // that is the year the forms are really about.
                r.SuggestedYear = r.PremiumEarliest?.Year ?? r.YearsInFile.Last();
                r.Warnings.Add($"The periods in this file fall in {r.YearsSummary}.");
            }
        }

        private static Dictionary<string, int> BuildHeaderMap(IExcelDataReader reader)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var raw = reader.GetValue(i)?.ToString()?.Trim();
                if (string.IsNullOrEmpty(raw)) continue;
                var key = Norm(raw);
                if (!map.ContainsKey(key)) map[key] = i;
            }
            return map;
        }

        private static string Norm(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

        /// <summary>
        /// Cells arrive either as a real DateTime (a date-formatted cell) or as text.
        /// Text is parsed invariant-first so an ISO date reads the same on any machine,
        /// then with the current culture as a fallback.
        /// </summary>
        private static DateTime? ReadDate(IExcelDataReader reader, int index)
        {
            if (index < 0 || index >= reader.FieldCount) return null;

            var value = reader.GetValue(index);
            if (value == null) return null;
            if (value is DateTime dt) return dt.Date;

            var text = value.ToString()?.Trim();
            if (string.IsNullOrEmpty(text)) return null;

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out var parsed)) return parsed.Date;
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture,
                                  DateTimeStyles.None, out parsed)) return parsed.Date;

            // An Excel serial that arrived as a bare number.
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial)
                && serial > 20000 && serial < 80000)
            {
                try { return DateTime.FromOADate(serial).Date; } catch { }
            }
            return null;
        }
    }
}
