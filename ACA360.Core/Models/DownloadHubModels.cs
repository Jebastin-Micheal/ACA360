using System;
using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public class DownloadHubViewModel
    {
        public List<DownloadFileItem>? Files { get; set; } 

        // For Filter Dropdowns
        public List<string>? AvailableYears { get; set; } 
        public List<string>? AvailableEmployers { get; set; } 
    }

    public class DownloadFileItem
    {
        public string? FileName { get; set; } // Physical Name
        public string? FilePath { get; set; } // Full Path

        // Parsed Metadata
        public string? FormType { get; set; } // "1094-C Package"
        public int Year { get; set; }
        public int EmployerId { get; set; }
        public string? EmployerName { get; set; }

        public string? FileSize { get; set; } // "2.5 MB"
        public DateTime CreatedDate { get; set; }

        // Visual Helpers
        public string? IconClass => FileName?.EndsWith(".zip") == true ? "bxs-file-archive text-warning" : "bxs-file-pdf text-danger";
        public string BadgeClass => Year == DateTime.Now.Year ? "bg-label-success" : "bg-label-secondary";

        public bool IsNew => (DateTime.Now - CreatedDate).TotalHours < 24;
    }
}