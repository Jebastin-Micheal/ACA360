namespace ACA360.Core.Models
{
    // One row per distinct flag raised for an employee (sp_GetEmployeeFlagDetails)
    public class EmployeeFlagDetail
    {
        public int FlagId { get; set; }
        public string? FlagCode { get; set; }
        public string? FlagName { get; set; }
        public string? Description { get; set; }
        public string? Severity { get; set; }
        public string? Category { get; set; }
        public string? RemediationSuggestion { get; set; }
        public string? MonthsAffected { get; set; }
        public string? TargetTab { get; set; }
        public string? TargetField { get; set; }
    }
}
