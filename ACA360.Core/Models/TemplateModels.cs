namespace ACA360.Core.Models
{
    public class ImportTemplate
    {
        public int TemplateId { get; set; }
        public string? TemplateName { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
    }

    public class TemplateColumnMap
    {
        public int MapId { get; set; }
        public int TemplateId { get; set; }
        public string? TargetSheetName { get; set; }  // Grouping Key (e.g. "Employees")
        public string? SourceSheetName { get; set; }  // Excel Sheet Name

        public string? TargetColumnName { get; set; } // DB Column (e.g. "EmployeeSSN")
        public string? SourceColumnName { get; set; } // Excel Header (e.g. "Employee SSN")

        public bool IsRequired { get; set; } = true;
        public string? AlternateNames { get; set; } // Comma-separated list
    }

    /// <summary>An employer's own name for a template column (script 073), kept by EIN so it holds across years.</summary>
    public class EmployerColumnMap
    {
        public int EmployerColumnMapId { get; set; }
        public string EmployerEin { get; set; } = "";
        public int TemplateId { get; set; }
        public string SheetName { get; set; } = "";
        public string TargetColumnName { get; set; } = "";
        /// <summary>The header as the employer's file has it.</summary>
        public string SourceHeader { get; set; } = "";
        /// <summary>The template's own header for that column.</summary>
        public string? TemplateHeader { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}