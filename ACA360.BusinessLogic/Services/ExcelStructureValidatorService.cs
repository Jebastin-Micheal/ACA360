using ACA360.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class ExcelStructureValidatorService : IExcelStructureValidatorService
    {
        private readonly ILogger<ExcelStructureValidatorService> _logger;

        /// <summary>
        /// Purpose: Initializes the ExcelStructureValidatorService with required logging dependencies.
        /// Input parameters: ILogger logger
        /// Output/return value: None
        /// </summary>
        public ExcelStructureValidatorService(ILogger<ExcelStructureValidatorService> logger)
        {
            try
            {
                _logger = logger;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Purpose: Removes spaces and symbols to normalize headers for consistent matching (e.g., "Primary  EIN" matches "primaryein").
        /// Input parameters: string header
        /// Output/return value: string
        /// </summary>
        public static string NormalizeHeader(string? header)
        {
            try
            {
                if (string.IsNullOrEmpty(header)) return string.Empty;
                return Regex.Replace(header, "[^a-zA-Z0-9]", "").ToLowerInvariant();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Purpose: Validates the structural integrity and required columns of an uploaded Excel stream based on template rules using an optimized SAX reader.
        /// Input parameters: Stream excelStream, List of TemplateColumnMap rules
        /// Output/return value: ExcelValidationResult
        /// </summary>
        public ExcelValidationResult ValidateStructure(Stream excelStream, List<TemplateColumnMap> rules)
        {
            var result = new ExcelValidationResult();

            try
            {
                if (excelStream.CanSeek) excelStream.Position = 0;

                // ── Group rules by sheet name ───────────────
                var sheetRules = rules
                    .Where(r => !string.IsNullOrEmpty(r.SourceSheetName))
                    .GroupBy(r => r.SourceSheetName ?? "", StringComparer.OrdinalIgnoreCase)
                    .ToList();

                using var doc = SpreadsheetDocument.Open(excelStream, isEditable: false);

                var workbookPart = doc.WorkbookPart
                    ?? throw new InvalidOperationException("Workbook has no content.");

                // Shared strings table — needed to resolve string cell values
                var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;

                // Build a map: sheet name → WorksheetPart
                // Uses the workbook's sheet list
                var sheetMap = (workbookPart.Workbook ?? throw new InvalidOperationException("Workbook has no content."))
                    .Descendants<Sheet>()
                    .ToDictionary(
                        s => s.Name?.Value ?? "",
                        s => workbookPart.GetPartById(s.Id!) as WorksheetPart,
                        StringComparer.OrdinalIgnoreCase
                    );

                foreach (var sheetGroup in sheetRules)
                {
                    var sheetName = sheetGroup.Key;

                    // ── 1. Find the sheet (case-insensitive) ──
                    if (!sheetMap.TryGetValue(sheetName, out var worksheetPart)
                        || worksheetPart == null)
                    {
                        result.Errors.Add($"Missing required worksheet: '{sheetName}'");
                        continue;
                    }

                    // ── 2. Read ONLY the header row using SAX ────────────────────
                    // SAX reader streams the XML — never loads the full sheet into RAM
                    var actualHeadersNormalized = new HashSet<string>();

                    using var reader = OpenXmlReader.Create(worksheetPart);

                    bool headerRowFound = false;

                    while (reader.Read())
                    {
                        if (reader.ElementType != typeof(Row)) continue;
                        if (reader.IsEndElement) continue;

                        // Load only this one Row element — not the whole sheet
                        if (reader.LoadCurrentElement() is not Row row) continue;

                        foreach (var cell in row.Elements<Cell>())
                        {
                            var value = ResolveCellValue(cell, sharedStrings);
                            if (!string.IsNullOrWhiteSpace(value))
                                actualHeadersNormalized.Add(NormalizeHeader(value));
                        }

                        headerRowFound = true;

                        // ✅ Stop here — we have the headers, no need to read remaining rows
                        break;
                    }

                    if (!headerRowFound || actualHeadersNormalized.Count == 0)
                    {
                        result.Errors.Add(
                            $"Worksheet '{sheetName}' is empty or missing headers.");
                        continue;
                    }

                    // ── 3. Validate required columns ──
                    foreach (var rule in sheetGroup)
                    {
                        if (!rule.IsRequired) continue;

                        bool found = actualHeadersNormalized.Contains(
                            NormalizeHeader(rule.SourceColumnName));

                        // Check alternate names if primary not found
                        if (!found && !string.IsNullOrEmpty(rule.AlternateNames))
                        {
                            var alternates = rule.AlternateNames
                                .Split(',', StringSplitOptions.RemoveEmptyEntries);

                            found = alternates.Any(alt =>
                                actualHeadersNormalized.Contains(NormalizeHeader(alt)));
                        }

                        if (!found)
                        {
                            var altMsg = string.IsNullOrEmpty(rule.AlternateNames)
                                ? ""
                                : $" (or alternates: {rule.AlternateNames})";

                            result.Errors.Add(
                                $"Worksheet '{sheetName}' is missing required column: " +
                                $"'{rule.SourceColumnName}'{altMsg}");
                        }
                    }
                }

                result.IsValid = !result.Errors.Any();
                return result;
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logger.LogError(ex, "Error occurred in ValidateStructure during Excel parsing.");
                }

                // An empty or unreadable workbook is a problem with the file, not a crash: the upload is
                // rejected with this message. Throwing made the background job fail and retry (two such
                // jobs sat in Hangfire as Failed).
                result.Errors.Add(
                    "The file is empty or is not an Excel workbook (.xlsx). Save it as .xlsx and upload it again.");
                result.IsValid = false;
                return result;
            }
        }

        public Dictionary<string, List<string>> ReadHeaders(Stream excelStream)
        {
            var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (excelStream.CanSeek) excelStream.Position = 0;
            using var doc = SpreadsheetDocument.Open(excelStream, isEditable: false);
            var workbookPart = doc.WorkbookPart;
            if (workbookPart?.Workbook == null) return headers;
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;

            foreach (var sheet in workbookPart.Workbook.Descendants<Sheet>())
            {
                var row = new List<string>();
                if (sheet.Id?.Value != null && workbookPart.GetPartById(sheet.Id.Value) is WorksheetPart part)
                {
                    using var reader = OpenXmlReader.Create(part);
                    while (reader.Read())
                    {
                        if (reader.ElementType != typeof(Row) || reader.IsEndElement) continue;
                        if (reader.LoadCurrentElement() is not Row first) continue;
                        row.AddRange(first.Elements<Cell>()
                            .Select(c => ResolveCellValue(c, sharedStrings)?.Trim() ?? "")
                            .Where(v => v.Length > 0));
                        break;   // the header row only
                    }
                }
                headers[sheet.Name?.Value ?? ""] = row;
            }
            return headers;
        }

        /// <summary>
        /// Purpose: Resolves the actual text value of a cell, falling back to the SharedStringTable if the data type dictates it.
        /// Input parameters: Cell cell, SharedStringTable? sharedStrings
        /// Output/return value: string
        /// </summary>
        private static string ResolveCellValue(Cell cell, SharedStringTable? sharedStrings)
        {
            try
            {
                var raw = cell.InnerText;

                if (cell.DataType?.Value == CellValues.SharedString
                    && sharedStrings != null
                    && int.TryParse(raw, out int index))
                {
                    // Shared string — look up actual text from the shared table
                    return sharedStrings.ElementAt(index).InnerText;
                }

                return raw;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}