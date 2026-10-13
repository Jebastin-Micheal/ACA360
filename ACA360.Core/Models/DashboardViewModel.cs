using System;

namespace ACA360.Core.Models
{
    // The per-role DashboardViewModel and its data classes were removed with the old
    // role dashboards (every role now uses EmployerDashboardPageViewModel). These two
    // are still used by the role tiles.

    /// <summary>An IRS submission that was rejected, for the "rejections to fix" tile.</summary>
    public class IrsRejectionDto
    {
        public int SubmissionId { get; set; }
        public string? EmployerName { get; set; }
        public int TaxYear { get; set; }
        public string? StatusMessage { get; set; }
        public DateTime GeneratedDate { get; set; }
    }

    /// <summary>A person and how many clients / files they carry (team workload).</summary>
    public class StaffLoad { public string? Name { get; set; } public int FileCount { get; set; } }
}
