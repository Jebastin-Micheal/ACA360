using System;
using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public class DistributionDashboardViewModel
    {
        public int TotalEmployees { get; set; }
        public int ElectronicCount { get; set; }
        public int PaperCount { get; set; }
        public int EmailedCount { get; set; }

        // Lists for Grid
        public List<EmployeeDistributionStatus> Employees { get; set; } = new List<EmployeeDistributionStatus>();



        public PaginationViewEntity? Metadata { get; set; }
        public string? Search { get; set; }
        public int SortColumn { get; set; }
        public string? SortOrder { get; set; }
        public string? StatusFilter { get; set; }
    }

    public class EmployeeDistributionStatus
    {
        public int EmployeeId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public bool HasConsent { get; set; }
        public DateTime? EmailSentDate { get; set; }
        public bool Downloaded { get; set; }
    }
    
}