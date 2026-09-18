using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface ISmartPdfService
    {
        // 1. For the Admin UI: Get all available fields from a blank PDF
        List<string> ExtractFieldNames(string filePath);

        // 2. For the Engine: Fill the PDF with real data
        byte[] FillPdfForm(string templatePath, Dictionary<string, string> dataMap);
    }
}