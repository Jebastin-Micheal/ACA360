using ACA360.Core.Models;
using System.ComponentModel.DataAnnotations;

namespace ACA360.Web.Models
{
    public class SystemSettingsViewModel
    {
        public bool IsApiEnabled { get; set; }
        // System Core State
        public bool MaintenanceMode { get; set; }

        // NEW: Security Settings Section
        public SecuritySettingsDto Security { get; set; } = new SecuritySettingsDto();

        //// NEW: Communication (SMTP/SMS/API Keys) - placeholder
        //public CommunicationSettingsDto Communication { get; set; } = new CommunicationSettingsDto();

        //// NEW: Monitoring Metrics (future use)
        //public SystemHealthDto SystemHealth { get; set; } = new SystemHealthDto();

        // NEW: Database Settings Section
        public DatabaseSettingsDto Database { get; set; } = new DatabaseSettingsDto();

        public IEnumerable<AutomationJobDto> Automation { get; set; } = new List<AutomationJobDto>();

        // Optional: Display system info
        public string? Environment { get; set; }
        public string? Version { get; set; }
        public DateTime LastDeployment { get; set; }
        // NEW TABS
        public GeneralSettingsDto General { get; set; } = new GeneralSettingsDto();
        public SmtpSettingsDto Smtp { get; set; } = new SmtpSettingsDto();
        public IrsSettingsDto Irs { get; set; } = new IrsSettingsDto();
        public List<AuditLogDto> AuditLogs { get; set; } = new List<AuditLogDto>();
    }
    //public class DatabaseSettingsDto
    //{
    //    public string BackupFrequency { get; set; } = "Daily";  // Daily / Weekly / Monthly
    //    public string BackupTime { get; set; } = "02:00";       // HH:mm
    //    public int RetentionDays { get; set; } = 30;

    //    public List<DatabaseBackupRecordDto> BackupHistory { get; set; } = new();

    //    public static implicit operator DatabaseSettingsDto(Core.Models.DatabaseSettingsDto v)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}

    //public class DatabaseBackupRecordDto
    //{
    //    public int Id { get; set; }
    //    public string FileName { get; set; }
    //    public string FilePath { get; set; }
    //    public decimal? SizeMb { get; set; }
    //    public DateTime CreatedOn { get; set; }

    //    public string Timestamp => CreatedOn.ToString("yyyy-MM-dd HH:mm");
    //}

   
}
