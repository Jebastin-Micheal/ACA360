namespace ACA360.Core.Models
{
    // Counts for the quick-filter badge bar above the employee list (sp_Employee_QuickStats)
    public class EmployeeQuickStats
    {
        public int AllCount { get; set; }
        public int FlaggedCount { get; set; }
        public int MissingCount { get; set; }
        public int ValidCount { get; set; }
    }

    // One row of the flagged-employee Excel download (sp_FlaggedEmployees_Export)
    public class FlaggedEmployeeExportRow
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? SSN { get; set; }
        public string? EmployerName { get; set; }
        public string? TaxId { get; set; }
        public string? FlagSeverity { get; set; }
        public string? FlagDescription { get; set; }
    }
}
