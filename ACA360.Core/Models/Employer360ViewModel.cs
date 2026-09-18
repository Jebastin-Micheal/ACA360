using System;
using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public class Employer360ViewModel
    {
        public Employer Employer { get; set; } = new Employer();
        public int FilingYear { get; set; }

        // Alias to match the new UI binding without breaking existing Dapper maps
        public int ReportingYear => FilingYear;

        // Existing Stats
        public int TotalEmployees { get; set; }
        public int FullTimeCount { get; set; }

        // Alias for the ALE display in the new UI
        public int AcaFullTimeCount => FullTimeCount;

        public int FailedFilesCount { get; set; }
        public DateTime? LastImportDate { get; set; }

        // --- NEW FIELDS FOR ADVANCED WIDGETS ---
        public DateTime? LastCalculationDate { get; set; } // <--- NEW: For the timeline
        public int PartTimeCount { get; set; }
        public int VariableHoursCount { get; set; }
        public int COBRACount { get; set; }

        // Readiness Score (0-100)
        public int ReadinessScore { get; set; }

        public Dictionary<int, string> MonthlyCoverage { get; set; } = new Dictionary<int, string>();

        // Already exists in your model, perfect for the ingestion history table
        public List<UploadedFileLog> RecentFiles { get; set; } = new List<UploadedFileLog>();
        public List<AleTrendMonthDto> AleTrend { get; set; } = new();
    }
}