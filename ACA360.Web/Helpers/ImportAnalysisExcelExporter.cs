using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using OfficeOpenXml.Style;
using System.Drawing;
using System.Globalization;
using System.Text.Json;

namespace ACA360.Web.Helpers
{
    public static class ImportAnalysisExcelExporter
    {
        private static readonly Color Navy = ColorTranslator.FromHtml("#102C65");
        private const int HeaderRow = 5;
        private static readonly string[] ExportOrder = { "Employer", "Plan", "Employee", "Premium", "Dependent" };

        public static async Task<Stream> CreateAsync(IDataProcessingRepository repository, int fileId,
            string? fileName, int? planYear, string mode, CancellationToken cancellationToken = default)
        {
            // Reuse the application's existing EPPlus license configuration in Program.cs.
            using var package = new ExcelPackage();
            package.Workbook.Properties.Title = "ACA360 Import Analysis";
            package.Workbook.Properties.Author = "ACA360";
            var generatedAt = DateTime.UtcNow;
            var totals = new List<ImportAnalysisEntitySummary>();
            foreach (var entity in ExportOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sheet = ImportAnalysisCatalog.Find(entity)!;
                var total = new ImportAnalysisEntitySummary { EntityType = entity };
                totals.Add(total);
                int part = 1, rowIndex = HeaderRow + 1;
                var ws = AddDataSheet(package, sheet, part, fileName, fileId, planYear, mode);
                await foreach (var row in repository.ReadImportAnalysisExportRowsAsync(fileId, entity, mode, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Preserve every record if an entity exceeds Excel's per-sheet row limit.
                    if (rowIndex > ExcelPackage.MaxRows)
                    {
                        FinishDataSheet(ws, rowIndex - 1, sheet.Columns.Count + 6);
                        ws = AddDataSheet(package, sheet, ++part, fileName, fileId, planYear, mode);
                        rowIndex = HeaderRow + 1;
                    }
                    WriteDataRow(ws, rowIndex++, sheet, row);
                    CountRow(total, row);
                }
                FinishDataSheet(ws, rowIndex - 1, sheet.Columns.Count + 6);
            }

            // Quality/impact totals are counted from the rows actually written above.
            // Related deletion counts use the same summary routine as the modal.
            var summary = await repository.GetImportAnalysisSummaryAsync(fileId, mode, cancellationToken);
            AddCharts(package, fileId, fileName, planYear, mode, generatedAt, totals, summary.RelatedRemovals);
            AddChanges(package, await repository.GetImportChangesAsync(fileId, null, null, 1, null, cancellationToken), fileName, fileId, planYear);
            var output = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            try
            {
                await package.SaveAsAsync(output, cancellationToken);
                output.Position = 0;
                return output;
            }
            catch
            {
                await output.DisposeAsync();
                throw;
            }
        }

        private static ExcelWorksheet AddDataSheet(ExcelPackage package, ImportAnalysisSheet sheet,
            int part, string? fileName, int fileId, int? year, string mode)
        {
            var ws = package.Workbook.Worksheets.Add(sheet.Label + (part == 1 ? "" : " " + part));
            int lastColumn = sheet.Columns.Count + 6;
            ws.View.ShowGridLines = false;
            ws.DefaultRowHeight = 19;
            ws.Cells.Style.Font.Name = "Calibri";
            ws.Cells.Style.Font.Size = 11;
            Title(ws, 1, lastColumn, sheet.Label + " — Import Analysis");
            MergeText(ws, 2, lastColumn, $"File: {fileName} | ID: {fileId} | Filing year: {year} | Mode: {mode}");
            MergeText(ws, 3, lastColumn, "All records; grid filters are not applied. Removed rows are database preview records, not completed deletions.");
            var headings = new[] { "Source row", "Change", "Validation" }
                .Concat(sheet.Columns.Select(c => c.Title))
                .Concat(new[] { "Row source", "Action note", "Validation details" }).ToArray();
            for (int i = 0; i < headings.Length; i++)
            {
                Text(ws.Cells[HeaderRow, i + 1], headings[i]);
                ws.Column(i + 1).Width = i < 3 ? 19 : 24;
            }
            ws.Column(lastColumn - 1).Width = 48;
            ws.Column(lastColumn).Width = 65;
            Header(ws.Cells[HeaderRow, 1, HeaderRow, lastColumn]);
            ws.Row(HeaderRow).Height = 44;
            ws.View.FreezePanes(HeaderRow + 1, 4);
            return ws;
        }

        private static void WriteDataRow(ExcelWorksheet ws, int index, ImportAnalysisSheet sheet,
            IDictionary<string, object?> row)
        {
            int lastColumn = sheet.Columns.Count + 6;
            string action = Value(row, "actionType"), validation = Value(row, "validationStatus");
            var range = ws.Cells[index, 1, index, lastColumn];
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(RowColor(action, validation));
            range.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            range.Style.Font.Color.SetColor(Color.Black);
            Text(ws.Cells[index, 1], Value(row, "rowNumber") is { Length: > 0 } number ? number : "Database");
            Text(ws.Cells[index, 2], ActionLabel(action));
            Text(ws.Cells[index, 3], validation);
            for (int i = 0; i < sheet.Columns.Count; i++)
                Text(ws.Cells[index, i + 4], Value(row, sheet.Columns[i].Key));
            Text(ws.Cells[index, lastColumn - 2], Value(row, "rowSource"));
            Text(ws.Cells[index, lastColumn - 1], Value(row, "actionNote"));
            string details = Value(row, "errorMessages");
            var json = Value(row, "issuesJson");
            if (!string.IsNullOrWhiteSpace(json))
            {
                using var issues = JsonDocument.Parse(json);
                var messages = new List<string>();
                foreach (var issue in issues.RootElement.EnumerateArray())
                {
                    string field = JsonValue(issue, "column"), severity = JsonValue(issue, "severity");
                    messages.Add($"{severity} | {field}: {JsonValue(issue, "message")}");
                }
                // Error wins when more than one rule marks the same source field.
                for (int i = 0; i < sheet.Columns.Count; i++)
                {
                    var fieldIssues = issues.RootElement.EnumerateArray().Where(x =>
                        string.Equals(JsonValue(x, "column"), sheet.Columns[i].Key, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (fieldIssues.Length == 0) continue;
                    bool error = fieldIssues.Any(x => JsonValue(x, "severity") is "Error" or "Critical" or "Fatal");
                    ws.Cells[index, i + 4].Style.Fill.BackgroundColor.SetColor(
                        ColorTranslator.FromHtml(error ? "#FADDDF" : "#FFF0C5"));
                    ws.Cells[index, i + 4].Style.Font.Bold = true;
                }
                if (messages.Count > 0) details = string.Join("\n", messages);
            }
            Text(ws.Cells[index, lastColumn], details);
            ws.Cells[index, lastColumn - 1, index, lastColumn].Style.WrapText = true;
        }

        private static void FinishDataSheet(ExcelWorksheet ws, int lastRow, int lastColumn)
        {
            ws.Cells[HeaderRow, 1, Math.Max(HeaderRow, lastRow), lastColumn].AutoFilter = true;
            // Fixed widths avoid scanning every cell for AutoFit on large exports.
        }

        private static void CountRow(ImportAnalysisEntitySummary total, IDictionary<string, object?> row)
        {
            switch (Value(row, "actionType"))
            {
                case "INSERT": total.Inserts++; break;
                case "UPDATE": total.Updates++; break;
                case "NOCHANGE": total.Skips++; break;
                case "REMOVE": total.Removes++; break;
                case "EXCLUDED": total.Excluded++; break;
            }
            if (Value(row, "rowSource") != "Upload") return;
            total.Total++;
            switch (Value(row, "validationStatus"))
            {
                case "Error": total.ErrorRows++; break;
                case "Warning": total.WarningRows++; break;
                case "Information": total.InformationRows++; break;
                default: total.CleanRows++; break;
            }
        }

        private static void AddCharts(ExcelPackage package, int fileId, string? fileName, int? year, string mode,
            DateTime generatedAt, IReadOnlyList<ImportAnalysisEntitySummary> totals,
            IReadOnlyList<ImportAnalysisRemovalSummary> related)
        {
            var ws = package.Workbook.Worksheets.Add("Charts");
            ws.View.ShowGridLines = false;
            ws.Cells.Style.Font.Name = "Calibri";
            ws.Cells.Style.Font.Size = 11;
            for (int i = 1; i <= 14; i++) ws.Column(i).Width = i == 1 ? 27 : 14;
            ws.DefaultRowHeight = 21;
            Title(ws, 1, 14, "ACA360 — Import Analysis");
            MergeText(ws, 2, 14, $"File: {fileName} | ID: {fileId} | Filing year: {year}");
            MergeText(ws, 3, 14, $"Mode: {mode} | Export started (UTC): {generatedAt:yyyy-MM-dd HH:mm:ss} | Scope: all five sheets, all rows");
            MergeText(ws, 4, 14, "Preview only. Grid filters are ignored. Counts below are based on exported rows; source data can change while exporting.");
            Section(ws, 6, 1, 3, "File quality");
            string[] names = { "Clean", "Warning", "Error", "Information" };
            int[] counts = { totals.Sum(x => x.CleanRows), totals.Sum(x => x.WarningRows),
                totals.Sum(x => x.ErrorRows), totals.Sum(x => x.InformationRows) };
            string[] actions = { "Ready", "Review", "Resolve", "Review" };
            int totalRows = totals.Sum(x => x.Total);
            Row(ws, 7, "Category", "Count", "Share"); Header(ws.Cells[7, 1, 7, 3]);
            for (int i = 0; i < names.Length; i++)
            {
                Row(ws, 8 + i, names[i], counts[i], totalRows == 0 ? 0d : (double)counts[i] / totalRows);
                ws.Cells[8 + i, 3].Style.Numberformat.Format = "0.0%";
            }
            Row(ws, 12, "Uploaded rows", totalRows);
            Row(ws, 13, "Clean rows (%)", totalRows == 0 ? 0d : (double)counts[0] / totalRows);
            ws.Cells[13, 2].Style.Numberformat.Format = "0.0%";
            var chart = ws.Drawings.AddChart("FileQualityChart", eChartType.Doughnut);
            chart.Title.Text = "File quality";
            chart.Series.Add(ws.Cells[8, 2, 11, 2], ws.Cells[8, 1, 11, 1]);
            chart.Legend.Position = eLegendPosition.Bottom;
            chart.SetPosition(5, 0, 4, 0);
            chart.SetSize(620, 310);

            Section(ws, 16, 1, 3, "Validation summary");
            Row(ws, 17, "Category", "Count", "Action"); Header(ws.Cells[17, 1, 17, 3]);
            for (int i = 0; i < names.Length; i++) Row(ws, 18 + i, names[i], counts[i], actions[i]);
            Section(ws, 24, 1, 7, "Database impact preview — " + mode);
            Row(ws, 25, "Entity", "New", "Update", "No change", "Remove", "Excluded", "Upload");
            Header(ws.Cells[25, 1, 25, 7]);
            for (int i = 0; i < totals.Count; i++)
            {
                var t = totals[i];
                Row(ws, 26 + i, t.EntityType == "Dependent" ? "Dependents" : t.EntityType,
                    t.Inserts, t.Updates, t.Skips, t.Removes, t.Excluded, t.Total);
            }
            Row(ws, 31, "Total", totals.Sum(x => x.Inserts), totals.Sum(x => x.Updates),
                totals.Sum(x => x.Skips), totals.Sum(x => x.Removes), totals.Sum(x => x.Excluded), totalRows);
            ws.Cells[31, 1, 31, 7].Style.Font.Bold = true;
            Section(ws, 34, 1, 4, "Related removals — " + mode);
            Row(ws, 35, "Entity", "Remove"); Header(ws.Cells[35, 1, 35, 2]);
            int next = 36;
            foreach (var item in related) Row(ws, next++, item.EntityType, item.Removes);
            MergeText(ws, next + 1, 14, "Removal counts describe the selected Overwrite scope. Employee and Plan master records remain.");
            MergeText(ws, next + 2, 14, "New: green | Updated: blue | Removed: rose | Warning: amber | Error / Excluded: red. Source values are text to preserve SSN/EIN/ZIP formatting.");
            ws.View.FreezePanes(6, 1);
        }

        // "What changed" (script 057): each change an import would make, by kind, before and after.
        private static void AddChanges(ExcelPackage package, ImportChanges changes, string? fileName, int fileId, int? year)
        {
            var ws = package.Workbook.Worksheets.Add("What changed");
            const int last = 8;
            Title(ws, 1, last, "What changed");
            MergeText(ws, 2, last, $"{fileName} | File #{fileId} | Filing year {year} | compared with what the system holds now");
            int row = 4;
            Section(ws, row++, 1, 2, "Summary");
            foreach (var kind in changes.Kinds)
                Row(ws, row++, kind.Label, kind.Changes);
            row++;
            Row(ws, row, "What", "Row", "Name", "Field", "Before", "After", "Note", "Entity");
            Header(ws.Cells[row, 1, row, last]);
            int headerRow = row++;
            var labels = changes.Kinds.ToDictionary(k => k.Kind, k => k.Label);
            foreach (var c in changes.Items)
            {
                if (row > ExcelPackage.MaxRows) break;
                Row(ws, row, labels.GetValueOrDefault(c.Kind, c.Kind), c.RowNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
                    c.Name ?? "", c.Field ?? "", c.OldValue ?? "", c.NewValue ?? "", c.Note ?? "", c.Entity);
                row++;
            }
            ws.Cells[headerRow, 1, Math.Max(headerRow, row - 1), last].AutoFilter = true;
            ws.Column(1).Width = 34; ws.Column(2).Width = 8; ws.Column(3).Width = 34; ws.Column(4).Width = 30;
            ws.Column(5).Width = 28; ws.Column(6).Width = 28; ws.Column(7).Width = 44; ws.Column(8).Width = 12;
            ws.View.FreezePanes(headerRow + 1, 1);
        }

        private static string Value(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";
        private static string JsonValue(JsonElement item, string key) =>
            item.TryGetProperty(key, out var value) ? value.ToString() : "";
        private static string ActionLabel(string action) => action switch
        {
            "INSERT" => "New", "UPDATE" => "Updated", "NOCHANGE" => "No change",
            "REMOVE" => "Removed on import", "EXCLUDED" => "Excluded", _ => action
        };
        private static Color RowColor(string action, string validation) => ColorTranslator.FromHtml(
            validation == "Error" ? "#FFF0F1" : validation == "Warning" ? "#FFF9E8" :
            action == "REMOVE" ? "#FBEEF5" : action == "INSERT" ? "#F1FAF4" :
            action == "UPDATE" ? "#F3F7FE" : action == "EXCLUDED" ? "#FFF0F1" : "#FFFFFF");
        private static void Text(ExcelRange cell, string? value)
        {
            value ??= "";
            // Do not silently truncate workbook data at Excel's per-cell limit.
            if (value.Length > 32767) throw new InvalidOperationException("An export value exceeds Excel's 32,767-character cell limit.");
            cell.Style.Numberformat.Format = "@";
            cell.Value = value; // Explicit string value, never Formula: uploaded '=...' stays inert text.
        }
        private static void Row(ExcelWorksheet ws, int row, params object[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (values[i] is string text) Text(ws.Cells[row, i + 1], text);
                else ws.Cells[row, i + 1].Value = values[i];
        }
        private static void Header(ExcelRange range)
        {
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(Navy);
            range.Style.Font.Color.SetColor(Color.White);
            range.Style.Font.Bold = true;
            range.Style.WrapText = true;
            range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }
        private static void Title(ExcelWorksheet ws, int row, int lastColumn, string title)
        {
            MergeText(ws, row, lastColumn, title);
            Header(ws.Cells[row, 1, row, lastColumn]);
            ws.Cells[row, 1].Style.Font.Size = 17;
            ws.Row(row).Height = 34;
        }
        private static void Section(ExcelWorksheet ws, int row, int first, int last, string title)
        {
            ws.Cells[row, first, row, last].Merge = true;
            Text(ws.Cells[row, first], title);
            Header(ws.Cells[row, first, row, last]);
            ws.Row(row).Height = 27;
        }
        private static void MergeText(ExcelWorksheet ws, int row, int lastColumn, string text)
        {
            ws.Cells[row, 1, row, lastColumn].Merge = true;
            Text(ws.Cells[row, 1], text);
        }
    }
}
