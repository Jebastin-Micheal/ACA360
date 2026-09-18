using OfficeOpenXml;
using System.IO;
using System.Linq;

namespace ACA360.BusinessLogic.Services
{
    public interface IFileParsingService
    {
        string ExtractEinFromStream(Stream fileStream, string fileName);
    }

    public class FileParsingService : IFileParsingService
    {
        public string ExtractEinFromStream(Stream fileStream, string fileName)
        {
            // Only handle Excel for this EPPlus implementation (add CSV logic if needed)
            if (!fileName.EndsWith(".xlsx", System.StringComparison.OrdinalIgnoreCase))
                return null;

            using var package = new ExcelPackage(fileStream);

            // Assuming the data is on the first worksheet
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet == null || worksheet.Dimension == null) return null;

            // STRATEGY A: Scan headers for "EIN" and get the value from Row 2
            int columnsToScan = Math.Min(worksheet.Dimension.Columns, 50); // Only scan first 50 cols

            for (int col = 1; col <= columnsToScan; col++)
            {
                var headerValue = worksheet.Cells[1, col].Text?.Trim().ToUpper();

                if (headerValue == "EIN" || headerValue == "EMPLOYER EIN" || headerValue == "TAX ID" || headerValue == "PRIMARY EIN")
                {
                    // Return the value directly underneath the header (Row 2)
                    return worksheet.Cells[2, col].Text?.Trim();
                }
            }

            // STRATEGY B: Hardcoded Cell (Uncomment if your clients use a strict Cover Page template)
            // var coverPage = package.Workbook.Worksheets["CoverPage"];
            // if (coverPage != null) return coverPage.Cells["B4"].Text?.Trim();

            return null; // EIN not found
        }
    }
}