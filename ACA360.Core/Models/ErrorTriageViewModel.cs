using System;
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

        // Acknowledged (skipped) with a reason (script 061)
        public string? AckReason { get; set; }
        public string? AckBy { get; set; }
        public DateTime? AckAt { get; set; }
        public int? AckCarriedFromFileLogId { get; set; }
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

    public class UpdateStatusPayload
    {
        public int FileLogId { get; set; }
        public long StagingRowErrorId { get; set; }
        // Kept as sent: every JSON string is upper-cased by default, which turned "Skipped" into "SKIPPED".
        [System.Text.Json.Serialization.JsonConverter(typeof(global::ACA360.Core.Binders.KeepCaseStringJsonConverter))]
        public string? Status { get; set; }
        /// <summary>Why a warning is acknowledged; required when Status is Skipped (script 061). Kept as typed.</summary>
        [System.Text.Json.Serialization.JsonConverter(typeof(global::ACA360.Core.Binders.KeepCaseStringJsonConverter))]
        public string? Reason { get; set; }
    }

    public class IssueSkipReason
    {
        public long StagingRowErrorId { get; set; }
        public string? Reason { get; set; }
        public string? SkippedBy { get; set; }
        public DateTime SkippedAt { get; set; }
        public int? CarriedFromFileLogId { get; set; }
    }

    // ── A file's corrections (script 056): automatic ones and those made in the editor ──
    public class FileCorrectionGroup
    {
        public string? Source { get; set; }            // "Auto" or "Manual"
        public int? RuleId { get; set; }
        public string? RuleName { get; set; }
        public string? StagingTableName { get; set; }
        public string? ColumnName { get; set; }
        public int Changes { get; set; }
    }

    public class FileCorrection
    {
        public long StagingAutoCorrectionId { get; set; }
        public string? Source { get; set; }
        public int? RuleId { get; set; }
        public string? RuleName { get; set; }
        public string? StagingTableName { get; set; }
        public long StagingRowId { get; set; }
        public int? RowNumber { get; set; }
        public string? RowLabel { get; set; }
        public string? ColumnName { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? ChangedBy { get; set; }
        public DateTime CorrectedAt { get; set; }
        public int TotalCount { get; set; }
    }

    public class FileCorrectionsViewModel
    {
        public int FileLogId { get; set; }
        public bool CanEdit { get; set; }
        public List<FileCorrectionGroup> Groups { get; set; } = new();
        public List<FileCorrection> Items { get; set; } = new();
        public string? Source { get; set; }
        public int? RuleId { get; set; }
        public string? ColumnName { get; set; }
        public string? Search { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalCount { get; set; }
        public int TotalPages => PageSize < 1 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    }

    public class EditRowViewModel
    {
        public int FileLogId { get; set; }
        public string? TableName { get; set; }
        public long RowId { get; set; }
        public Dictionary<string, object?> RowData { get; set; } = new Dictionary<string, object?>();
        public List<StagingRowError> AllErrorsForThisRow { get; set; } = new List<StagingRowError>();
        /// <summary>False once the file is imported, importing or replaced: the editor is read-only.</summary>
        public bool CanEdit { get; set; } = true;
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
