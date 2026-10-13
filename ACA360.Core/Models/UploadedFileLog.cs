using System;
using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public class UploadedFileLog
    {
        // --- Database Columns ---
        public int FileLogId { get; set; }
        public string? OriginalFileName { get; set; }
        public string? EmployerName { get; set; }
        public string? TaxID { get; set; }
        public string? StoredFileName { get; set; }
        public string? FilePath { get; set; }
        // public string UploadStatus { get; set; } // Deprecated in favor of IDs
        public string? UploadedByUserId { get; set; }
        public DateTime UploadedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        // New DB Columns (Must match SQL ALTER script)
        public int ImportTemplateId { get; set; }
        public int? PlanYear { get; set; }
        public DateTime? ProcessingStartTime { get; set; }
        public DateTime? ProcessingEndTime { get; set; }
        public string? AssignedToUserId { get; set; }

        // --- Calculated / Joined Properties (From Stored Procedure) ---
        // These don't exist in the table but are returned by sp_SearchFileLogsPaginated
        public string? PriorityLevel { get; set; }  // "Critical", "Warning", "Normal"
        public string? TemplateName { get; set; }   // "Master Format", "ENAV", etc.
        public string? ProcessingTime { get; set; } // "00:02:15"
        public string? UploadedByName { get; set; } // "John Doe"
        public string? AssignedToName { get; set; } // "Jane Smith"

        // --- Status & Metrics Properties ---
        public string? ValidationStatus { get; set; }
        public string? WorkflowStatus { get; set; }
        public DateTime? AnalysisDate { get; set; }
        public int? TotalRowsValidated { get; set; }
        public int TotalRows => TotalRowsValidated ?? 0;
        public int? FatalErrorCount { get; set; }
        public int? WarningCount { get; set; }
        public decimal? ErrorPercentage { get; set; }
        public int? ValidationStatusId { get; set; }
        public int? WorkflowStatusId { get; set; } = 0;
        // === NEW PERIOD COLUMNS ===
        public DateTime? PeriodStartDate { get; set; }
        public DateTime? PeriodEndDate { get; set; }
        public int Summary_TotalEmployees { get; set; }
        public bool Summary_HasPlanData { get; set; }
        public bool Summary_HasPayrollData { get; set; }
        public string? Summary_ValidationMessage { get; set; }
        public string? FileHash { get; set; } // SHA-256 Hash
        public bool IsArchived { get; set; }
        // Written by sp_ProcessEmployerData between employers during the import.
        // ValidationProgressPercent is decimal(5,2); the row counts are bigint.
        public decimal? ValidationProgressPercent { get; set; }
        public string? ValidationCurrentStep { get; set; }
        public long? ValidationRowsProcessed { get; set; }
        public long? ValidationTotalRows { get; set; }
    }

    // --- Helper Classes (Keep these here) ---

    public class UploadedFileModel
    {
        public List<TemplateViewModel> AvailableTemplates { get; set; } = new List<TemplateViewModel>();
    }

    public class TemplateViewModel
    {
        public int TemplateId { get; set; }
        public string? TemplateName { get; set; }
    }

    public class FileUploadError
    {
        public int ErrorId { get; set; }
        public int FileLogId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ExcelValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class StagingRowError
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
    }
}