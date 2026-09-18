using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace ACA360.Core.Models
{
    namespace ACA360.Core.Models
    {
        public class PdfTemplate
        {
            public int TemplateId { get; set; }
            public string? FormType { get; set; }
            public int TaxYear { get; set; }

            // FIX: Added FileName property (matches SQL)
            public string? FileName { get; set; }
            public string? FilePath { get; set; }
            public bool IsActive { get; set; }
        }

        public class PdfFieldMap
        {
            public int MapId { get; set; }
            public int TemplateId { get; set; }

            // FIX: Switched from 'FieldName' to 'PdfFieldName' (matches SQL)
            public string? PdfFieldName { get; set; }

            // FIX: Switched from 'X/Y' to 'SystemDataKey' (matches SQL)
            public string? SystemDataKey { get; set; }

            public string? DefaultValue { get; set; }
        }
        public class PdfMappingViewModel
        {
            public int TemplateId { get; set; }
            public string? TemplateName { get; set; }

            // The list of fields found in the PDF
            public List<PdfFieldMapViewModel> FieldList { get; set; } = new List<PdfFieldMapViewModel>();

            // The dropdown options (Employee.SSN, etc.)
            public List<SelectListItem> SystemDataKeys { get; set; } = new List<SelectListItem>();
        }

        public class PdfFieldMapViewModel
        {
            public string? PdfFieldName { get; set; }
            public string? MappedSystemKey { get; set; }
        }
    }
}