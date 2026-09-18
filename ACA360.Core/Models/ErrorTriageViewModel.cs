using System.Collections.Generic;

namespace ACA360.Core.Models
{
    // ── Triage error row ──────────────────────────────────────────────────────
    public class TriageError
    {
        public long StagingRowErrorId { get; set; }
        public string? StagingTableName { get; set; }
        public long StagingRowId { get; set; }
        public int RowNumber { get; set; }
        public string? TargetColumn { get; set; }
        public string? BadValue { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Severity { get; set; }
        public bool IsCorrectable { get; set; }
        public string? HelpText { get; set; }
        public string? Status { get; set; }
        public string? AuditTabNote { get; set; }
    }

    // ── Error counts returned by sp / repo ────────────────────────────────────
    public class ErrorCounts
    {
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public int InformationCount { get; set; }
        public int EmployerActionCount { get; set; }
        public int TotalErrorsCount { get; set; }
    }

    // ── Main dashboard view model ─────────────────────────────────────────────
    public class ErrorTriageViewModel
    {
        public int FileLogId { get; set; }
        public string? OriginalFileName { get; set; }

        // FIX: three properties required by ErrorTriageService and ErrorTriageDashboard
        public string? EmployerName { get; set; }
        public string? TaxId { get; set; }
        public int WorkflowStatusId { get; set; }

        public List<TriageError> Errors { get; set; } = new List<TriageError>();
        public ErrorCounts ErrorCountList { get; set; } = new ErrorCounts();

        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public int InformationCount { get; set; }
        public int EmployerActionCount { get; set; }
        public int TotalErrorsCount { get; set; }

        public int TotalRowsValidated { get; set; }
        public int EmployeesWithErrors { get; set; }
        public decimal ErrorPercentage { get; set; }

        public PaginationViewEntity Pagination { get; set; } = new PaginationViewEntity();
        public PaginationViewEntity Metadata { get; set; } = new PaginationViewEntity();

        public string? Search { get; set; }
        public int SortColumn { get; set; }
        public string? SortOrder { get; set; }
    }

    // ── Misc supporting models ────────────────────────────────────────────────
    public class ErrorSummary
    {
        public string? StagingTableName { get; set; }
        public int ErrorCount { get; set; }
    }

    public class UpdateStatusPayload
    {
        public long StagingRowErrorId { get; set; }
        public string? Status { get; set; }
    }

    public class EditRowViewModel
    {
        public int FileLogId { get; set; }
        public string? TableName { get; set; }
        public long RowId { get; set; }
        public Dictionary<string, object> RowData { get; set; } = new Dictionary<string, object>();
        public List<StagingRowError> AllErrorsForThisRow { get; set; } = new List<StagingRowError>();
    }

    public class UpdateFieldPayload
    {
        public int FileLogId { get; set; }
        public string? TableName { get; set; }
        public long RowId { get; set; }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    // FIX: added NewErrorCount and NewWarningCount so service and controller can
    //      return live counts to the JS badge updater after each save
    public class UpdateRowResult
    {
        public bool IsSuccess { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public long? NextRowId { get; set; }
        public int NewErrorCount { get; set; }
        public int NewWarningCount { get; set; }
    }
}
