namespace ACA360.Core.Models
{
    public class ImportValidationSummaryModel
    {
        public int TotalEmployees { get; set; }
        public int EmployeesWithHireDate { get; set; }
        public int EmployeesWithStatus { get; set; }
        public int PayrollRows { get; set; }
        public int EnrollmentRows { get; set; }
        public int PlanCount { get; set; }

        // Booleans for checkmarks
        public bool HasCoverageData { get; set; }
        public bool HasPayrollData { get; set; }
        public bool HasPlanData { get; set; }
        public int TotalRows { get; set; }
        public int FatalErrorCount { get; set; }
        public int WarningCount { get; set; }
        public string? ValidationMessage { get; set; }
    }
}