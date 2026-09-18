using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.Rendering;


namespace ACA360.Core.Models
{
    public class ChangePassword
    {
        public string? User_ID { get; set; }
        public string? OldPassword { get; set; }
        public string? NewPassword { get; set; }
        public string? ConfrimPassword { get; set; }

    }
    public class SecuritySettingsDto
    {
        public int MinPasswordLength { get; set; } = 8;
        public bool RequireUppercase { get; set; } = true;
        public bool RequireDigits { get; set; } = true;

        public int LockoutThreshold { get; set; } = 5;
        public int LockoutDuration { get; set; } = 15;

        public int SessionTimeout { get; set; } = 30;
        public bool AllowConcurrentSessions { get; set; } = false;
    }
    public class DatabaseSettingsDto
    {
        public string? BackupFrequency { get; set; } = "Daily";  // Daily / Weekly / Monthly
        public string? BackupTime { get; set; } = "02:00";       // HH:mm
        public int RetentionDays { get; set; } = 30;

        public List<DatabaseBackupRecordDto> BackupHistory { get; set; } = new List<DatabaseBackupRecordDto>();
    }
    public class DatabaseBackupRecordDto
    {
        public int Id { get; set; }
        public string? FileName { get; set; }
        public string? FilePath { get; set; }
        public decimal? SizeMb { get; set; }
        public DateTime CreatedOn { get; set; }

        public string? Timestamp => CreatedOn.ToString("yyyy-MM-dd HH:mm");
    }
    public class AutomationJobDto
    {
        public int Id { get; set; }
        public string? JobKey { get; set; }
        public string? DisplayName { get; set; }
        public string? CronExpression { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime? LastRun { get; set; }
        public DateTime? NextRun { get; set; }
        public string? LastStatus { get; set; }
    }

}
