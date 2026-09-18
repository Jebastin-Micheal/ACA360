using System;
using System.Collections.Generic;

namespace ACA360.Core.Models
{
    // 1. General Settings DTO
    public class GeneralSettingsDto
    {
        public string? AppName { get; set; }
        public string? SupportEmail { get; set; }
    }

    // 2. SMTP DTO
    public class SmtpSettingsDto
    {
        public string? Host { get; set; }
        public int Port { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool EnableSsl { get; set; }
    }

    // 3. IRS DTO
    public class IrsSettingsDto
    {
        public string? Tcc { get; set; }
        public string? TransmitterEin { get; set; }
        public string? SoftwareId { get; set; }
    }

    // 4. Audit Log Row
    public class AuditLogDto
    {
        public string? ActivityType { get; set; }
        public string? ActionDetail { get; set; }
        public string? UserName { get; set; }
        public DateTime ActivityDate { get; set; }
    }
    public class AuditLogRequest
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Search { get; set; }
        public string? ActivityType { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? SortColumn { get; set; } = "Time";
        public string? SortDirection { get; set; } = "DESC";
    }

    public class AuditLogResult : AuditLogDto
    {
        public int TotalCount { get; set; } // From SQL "COUNT(*) OVER()"
    }

    public class AuditLogListViewModel
    {
        public List<AuditLogResult> Logs { get; set; } = new List<AuditLogResult>();
        public AuditLogRequest CurrentFilter { get; set; } = new AuditLogRequest();
        public int TotalRecords { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalRecords / CurrentFilter.PageSize);
    }   
}