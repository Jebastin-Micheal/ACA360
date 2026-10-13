using ACA360.Core.Models;

namespace ACA360.Web.Models
{
    public class FileGroupViewModel
    {
        public string? EmployerName { get; set; }
        public string? TaxId { get; set; }
        public int FileCount => Files?.Count ?? 0;
        public DateTime? LatestUpload => Files?.Max(f => f.UploadedAt);
        public List<UploadedFileLog> Files { get; set; } = new List<UploadedFileLog>();
    }

    //public class GroupedDashboardViewModel
    //{
    //    public List<FileGroupViewModel> FileGroups { get; set; } = new List<FileGroupViewModel>();
    //    public UploadedFileModel UploadModel { get; set; }

    //    // Keep pagination props if you want to retain search/paging functionality
    //    public int TotalCount { get; set; }
    //    public int PageSize { get; set; }
    //    public int PageNumber { get; set; }
    //    public string SearchTerm { get; set; }
    //}
    public class GroupedDashboardViewModel
    {
        // --- Grid Data ---
        public List<FileGroupViewModel> FileGroups { get; set; } = new List<FileGroupViewModel>();
        public UploadedFileModel UploadModel { get; set; }= new UploadedFileModel();

        // --- Pagination ---
        public int TotalCount { get; set; }
        public int PageSize { get; set; }
        public int PageNumber { get; set; }

        // --- Summary cards: same rows as the grid, before the status filter ---
        public FileStatusCounts StatusCounts { get; set; } = new FileStatusCounts();

        // --- Filter State (Keeps filters alive on refresh) ---
        public string? SearchTerm { get; set; }
        public string? SelectedStatus { get; set; } = "All"; // "All", "Attention", "Processing", "Imported"
        public string? DateRange { get; set; } // Raw string from picker: "2024-01-01 to 2024-01-31"
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public bool ShowMyFilesOnly { get; set; }
        public string? ViewMode { get; internal set; }
    }

    // Helper DTO for the Repo
    
}