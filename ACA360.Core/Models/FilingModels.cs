using System;
using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public class FilingDashboardViewModel
    {
        public int SelectedYear { get; set; }
        public List<EmployerFilingStatusDto> Employers { get; set; } = new List<EmployerFilingStatusDto>();

        // Pagination & State properties
        public PaginationViewEntity? Metadata { get; set; }
        public string? Search { get; set; }
        public int SortColumn { get; set; }
        public string? SortOrder { get; set; }
        public FilingStatusSummary StatusSummary { get; set; }
    }

    public class EmployerFilingStatusDto
    {
        public int EmployerId { get; set; }
        public string? EmployerName { get; set; }
        public string? TaxId { get; set; }

        public int FilingStatusId { get; set; } // 0=Draft, 2=Locked
        public bool IsLocked { get; set; }
        public DateTime? LastGeneratedDate { get; set; }
        public int EmployeeFormsCount { get; set; }
        public bool IsArchived { get; set; }

        // Helper Status Text
        public string StatusText => FilingStatusId switch
        {
            0 => "Not Started",
            1 => "Draft Generated",
            2 => "Finalized / Locked",
            3 => "Filed with IRS",
            _ => "Unknown"
        };

        // Artifacts (Populated by File System Check)
        public bool Has1094 { get; set; }
        public bool HasBatch { get; set; }
    }

    public class Monthly1094Count
    {
        public int MonthNum { get; set; }
        public int FullTimeCount { get; set; }
        public int TotalCount { get; set; }
        public bool OfferedMEC { get; set; }
    }

    public class FilingStatusSummary
    {
        public int TotalCount { get; set; }
        public int NotStartedCount { get; set; }
        public int DraftGeneratedCount { get; set; }
        public int FinalizedCount { get; set; }
    }

    public class FilingAuditLogDto
    {
        public string EventType { get; set; }
        public string EventDescription { get; set; }
        public string PerformedByUserId { get; set; }
        public string PerformedByUserName { get; set; }
        public DateTime EventDate { get; set; }
    }
}