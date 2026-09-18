namespace ACA360.Web.Models
{
    // Models/Upload/SasInitRequest.cs
    public class SasInitRequest
    {
        public string FileName { get; set; } = "";
        public long FileSize { get; set; }
        public int TemplateId { get; set; }
        public string TaxId { get; set; }
        public string FileHash { get; set; } = "";   // ← add this
        public bool ForceUpload { get; set; }         // ← add this
        public int PlanYear { get; set; }
        public int EmployerId { get; set; }
    }

    // Models/Upload/SasInitResponse.cs
    public class SasInitResponse
    {
        public string SasUrl { get; set; } = "";
        public string BlobName { get; set; } = "";
        public int FileLogId { get; set; }
        public int ChunkSize { get; set; }
    }

    // Models/Upload/FinalizeRequest.cs
    public class FinalizeRequest
    {
        public int FileLogId { get; set; }
        public string BlobName { get; set; } = "";
        public string FileHash { get; set; } = "";  // computed client-side
        public long FileSize { get; set; }

        // Set once the user has acknowledged that the file's periods do not match
        // the filing year they chose. Deliberately separate from ForceUpload, so
        // confirming a duplicate does not also wave through a mismatched year.
        public bool ConfirmYear { get; set; }
    }
}
