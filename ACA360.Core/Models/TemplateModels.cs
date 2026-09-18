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
}