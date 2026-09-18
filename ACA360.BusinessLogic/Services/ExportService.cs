using ACA360.Core.Models;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class ExportService : IExportService
    {
        public byte[] BuildCsv(ExportRequest request)
        {
            var sb = new StringBuilder();

            // Header row
            sb.AppendLine(string.Join(",", request.Columns.Select(c => EscapeCsv(c.Header))));

            // Data rows
            foreach (var row in request.Rows)
            {
                var line = request.Columns.Select(col =>
                {
                    row.TryGetValue(col.Key, out var value);
                    return EscapeCsv(value?.ToString() ?? string.Empty);
                });
                sb.AppendLine(string.Join(",", line));
            }

            // UTF-8 BOM so Excel/Windows opens special characters (₹, etc.) correctly
            var preamble = Encoding.UTF8.GetPreamble();
            var body = Encoding.UTF8.GetBytes(sb.ToString());
            var result = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
            return result;
        }

        public byte[] BuildExcel(ExportRequest request)
        {
            using var workbook = new XSSFWorkbook();
            var sheetName = string.IsNullOrWhiteSpace(request.Title) ? "Sheet1" : Truncate(request.Title, 31);
            var sheet = workbook.CreateSheet(sheetName);

            var headerStyle = workbook.CreateCellStyle();
            var headerFont = workbook.CreateFont();
            headerFont.IsBold = true;
            headerFont.Color = NPOI.SS.UserModel.IndexedColors.White.Index;
            headerStyle.SetFont(headerFont);
            headerStyle.FillForegroundColor = NPOI.SS.UserModel.IndexedColors.Blue.Index;
            headerStyle.FillPattern = NPOI.SS.UserModel.FillPattern.SolidForeground;

            // Header row
            var headerRow = sheet.CreateRow(0);
            for (int c = 0; c < request.Columns.Count; c++)
            {
                var cell = headerRow.CreateCell(c);
                cell.SetCellValue(request.Columns[c].Header);
                cell.CellStyle = headerStyle;
            }

            // Data rows
            for (int r = 0; r < request.Rows.Count; r++)
            {
                var row = request.Rows[r];
                var dataRow = sheet.CreateRow(r + 1);
                for (int c = 0; c < request.Columns.Count; c++)
                {
                    row.TryGetValue(request.Columns[c].Key, out var value);
                    dataRow.CreateCell(c).SetCellValue(value?.ToString() ?? string.Empty);
                }
            }

            for (int c = 0; c < request.Columns.Count; c++)
            {
                sheet.SetColumnWidth(c, 20 * 256);
            }
            sheet.CreateFreezePane(0, 1);

            using var ms = new MemoryStream();
            workbook.Write(ms, leaveOpen: true);
            return ms.ToArray();
        }

        public byte[] BuildPdf(ExportRequest request)
        {
            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Text(request.Title)
                        .FontSize(16).Bold().FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);

                    page.Content().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            foreach (var _ in request.Columns)
                                columns.RelativeColumn();
                        });

                        // Header
                        table.Header(header =>
                        {
                            foreach (var col in request.Columns)
                            {
                                header.Cell().Background(QuestPDF.Helpers.Colors.Blue.Darken2).Padding(4)
                                    .Text(col.Header).FontColor(QuestPDF.Helpers.Colors.White).Bold();
                            }
                        });

                        // Rows
                        foreach (var row in request.Rows)
                        {
                            foreach (var col in request.Columns)
                            {
                                row.TryGetValue(col.Key, out var value);
                                table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2)
                                    .Padding(4).Text(value?.ToString() ?? string.Empty);
                            }
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Generated on ");
                        x.Span(DateTime.Now.ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture));
                        x.Span("  |  Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
                });
            });

            return document.GeneratePdf();
        }

        public string BuildPrintHtml(ExportRequest request)
        {
            var sb = new StringBuilder();
            sb.Append("<html><head><title>").Append(System.Net.WebUtility.HtmlEncode(request.Title)).Append("</title>");
            sb.Append(@"<style>
                body { font-family: Arial, sans-serif; margin: 24px; color:#333; }
                h2 { color:#696CFF; margin-bottom: 4px; }
                table { width:100%; border-collapse: collapse; margin-top: 16px; }
                th, td { border: 1px solid #ddd; padding: 8px; text-align: left; font-size: 13px; }
                th { background:#696CFF; color:#fff; }
                tr:nth-child(even) { background:#f8f8f8; }
                .meta { color:#888; font-size:12px; margin-bottom: 12px; }
            </style></head><body onload='window.print()'>");

            sb.Append("<h2>").Append(System.Net.WebUtility.HtmlEncode(request.Title)).Append("</h2>");
            sb.Append("<div class='meta'>Generated on ")
              .Append(DateTime.Now.ToString("dd-MMM-yyyy HH:mm"))
              .Append("  |  Total records: ").Append(request.Rows.Count)
              .Append("</div>");

            sb.Append("<table><thead><tr>");
            foreach (var col in request.Columns)
                sb.Append("<th>").Append(System.Net.WebUtility.HtmlEncode(col.Header)).Append("</th>");
            sb.Append("</tr></thead><tbody>");

            foreach (var row in request.Rows)
            {
                sb.Append("<tr>");
                foreach (var col in request.Columns)
                {
                    row.TryGetValue(col.Key, out var value);
                    sb.Append("<td>").Append(System.Net.WebUtility.HtmlEncode(value?.ToString() ?? string.Empty)).Append("</td>");
                }
                sb.Append("</tr>");
            }

            sb.Append("</tbody></table></body></html>");
            return sb.ToString();
        }

        private static string EscapeCsv(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        private static string Truncate(string value, int maxLength)
        {
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}