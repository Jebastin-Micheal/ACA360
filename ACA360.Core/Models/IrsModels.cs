using System;

namespace ACA360.Core.Models
{
    public class TransmitterConfig
    {
        public int ConfigId { get; set; }
        public string? TCC { get; set; }
        public string? TransmitterEIN { get; set; }
        public string? SoftwareId { get; set; }
        public string? CompanyName { get; set; }
        public string? ContactName { get; set; }
        public string? ContactPhone { get; set; }
        public string? ContactEmail { get; set; }
        public string? Environment { get; set; } // "P" (Prod) or "T" (Test)
    }

    public class IrsSubmissionLog
    {
        public int SubmissionId { get; set; }
        public int EmployerId { get; set; }
        public int TaxYear { get; set; }
        public string? FormType { get; set; } = "1094C_1095C";

        public Guid TransmissionId { get; set; }
        public string? ReceiptId { get; set; }

        public string? ManifestFilePath { get; set; }
        public string? FormFilePath { get; set; }
        public string? AckFilePath { get; set; }

        public int StatusId { get; set; }
        public string? StatusMessage { get; set; }
        public int RecordCount { get; set; }

        public string? GeneratedBy { get; set; }
        public DateTime GeneratedDate { get; set; }

        // Helper to display status text
        public string? StatusText => StatusId switch
        {
            0 => "Generated (Ready)",
            1 => "Transmitted",
            2 => "Accepted",
            3 => "Accepted w/ Errors",
            4 => "Rejected",
            _ => "Unknown"
        };

        public string SubmissionType { get; set; }
        public int ParentSubmissionId { get; set; }
    }
    // DTO for the Grid (Inner Class or separate file)
    public class IrsSubmissionLogDto : IrsSubmissionLog
    {
        public string? EmployerName { get; set; }
        public string? EmployerEIN { get; set; }
    }
    public class IrsDashboardItemDto
    {
        public int EmployerId { get; set; }
        public string? EmployerName { get; set; }
        public string? EmployerEIN { get; set; }
        public DateTime? FinalizedDate { get; set; }

        // NEW: Show the form type (1094-C vs 1094-B)
        public string? FormType { get; set; }
        // Submission Info (Nullable - might be empty if not generated yet)
        public int? SubmissionId { get; set; }
        public int? StatusId { get; set; }
        public int? RecordCount { get; set; }
        public DateTime? GeneratedDate { get; set; }

        public bool HasXml => SubmissionId.HasValue;
    }
    public class IrsSubmissionErrorDto
    {
        public int ErrorId { get; set; }
        public int SubmissionId { get; set; }
        public string? RecordId { get; set; } // EmployeeId
        public string? EmployeeName { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsFixed { get; set; }
    }
}