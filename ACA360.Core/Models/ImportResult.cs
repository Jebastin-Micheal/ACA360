using System;

namespace ACA360.Core.Models
{
    public class ImportResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public ImportValidationSummary Data { get; set; } = new ImportValidationSummary();
    }
    public class ImportValidationSummary
    {
        public int SummaryId { get; set; } = new int();
        public int FileLogId { get; set; } = new int();
        public int TotalEmployees { get; set; } = new int();
        public int EnrollmentRows { get; set; } = new int();
        public bool HasPlanData { get; set; }
        public bool HasPayrollData { get; set; }
        public bool HasCoverageData { get; set; }
        public bool IsImportComplete { get; set; }
        public string? ValidationMessage { get; set; }
        public int PlanCount { get; set; } = new int(); // Make sure this exists
        public int PayrollRows { get; set; } = new int(); // Make sure this exists


        // --- NEW PROPERTIES ---
        public int DependentRows { get; set; } = new int();
        public int PremiumRows { get; set; } = new int();
        public decimal TotalPayrollImported { get; set; }
    }
    public class fileDashboardStatsDto
    {
        public int TotalUploads { get; set; } = new int();
        public int ActionRequired { get; set; } = new int();
        public int Processing { get; set; } = new int();
        public int Completed { get; set; } = new int();
    }
}