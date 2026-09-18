using ACA360.BusinessLogic.Interfaces;
using iText.Forms;
using iText.Forms.Fields; // <--- Added this
using iText.Kernel.Pdf;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ACA360.BusinessLogic.Services
{
    public class SmartPdfService : ISmartPdfService
    {
        public List<string> ExtractFieldNames(string filePath)
        {
            if (!File.Exists(filePath)) return new List<string>();

            using var reader = new PdfReader(filePath);
            using var doc = new PdfDocument(reader);
            var form = PdfAcroForm.GetAcroForm(doc, false);

            // FIX: Use .GetFormFields() and extract Keys
            if (form == null) return new List<string>();

            return form.GetAllFormFields().Keys.OrderBy(k => k).ToList();
        }

        public byte[] FillPdfForm(string templatePath, Dictionary<string, string> dataMap)
        {
            using var ms = new MemoryStream();
            using var reader = new PdfReader(templatePath);
            using var writer = new PdfWriter(ms);
            using var doc = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(doc, true);

            // FIX: Get fields dictionary explicitly
            var fields = form.GetAllFormFields();

            foreach (var item in dataMap)
            {
                if (fields.ContainsKey(item.Key))
                {
                    // ENABLE AUTO-SIZING for long names
                    fields[item.Key].SetFontSize(0);
                    // FIX: Cast to PdfFormField if necessary, though normally not required
                    fields[item.Key].SetValue(item.Value);
                    fields[item.Key].SetReadOnly(true);
                }
            }

            form.FlattenFields();
            doc.Close();
            return ms.ToArray();
        }
    }
}