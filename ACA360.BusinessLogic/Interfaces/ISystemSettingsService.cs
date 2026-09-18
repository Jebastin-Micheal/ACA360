using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface ISystemSettingsService
    {
        Task<bool> GetMaintenanceModeAsync();
        Task SetMaintenanceModeAsync(bool enabled);
        Task SaveSecuritySettingsAsync(SecuritySettingsDto model);
        Task<SecuritySettingsDto> GetSecuritySettingsAsync();
        Task<DatabaseSettingsDto> GetDatabaseSettingsAsync();
        Task SaveDatabaseSettingsAsync(DatabaseSettingsDto model, int? userId = null);
        Task<(bool success, string message)> RunDatabaseBackupAsync(int? userId = null);
        Task CleanupOldBackupsAsync(int retentionDays);
        // Add these methods
        Task<GeneralSettingsDto> GetGeneralSettingsAsync();
        Task SaveGeneralSettingsAsync(GeneralSettingsDto model);

        Task<SmtpSettingsDto> GetSmtpSettingsAsync();
        Task SaveSmtpSettingsAsync(SmtpSettingsDto model);

        Task<IrsSettingsDto> GetIrsSettingsAsync();
        Task SaveIrsSettingsAsync(IrsSettingsDto model);

        Task<List<AuditLogDto>> GetAuditLogsAsync(int limit = 100);
        Task<AuditLogListViewModel> GetPagedAuditLogsAsync(AuditLogRequest request);
        // Add this new method for the export feature
        Task<List<AuditLogDto>> GetAllAuditLogsAsync();
        // Add this method
        Task<bool> GetBoolSettingAsync(string key);
        Task UpdateSettingAsync(string key, string value);
        Task<string> GetSettingValueAsync(string key);
        
        Task<ErrorLogListViewModel> GetPagedErrorLogsAsync(ErrorLogRequest request);
        Task<object> GetErrorSummaryStatsAsync();

        #region FilingYears
        Task<(List<FilingYearModel> FilingYears, PaginationViewEntity PageInfo)> GetFilingYearList(PaginationEntity paginationEntity);
        Task<FilingYearModel> GetFilingYearFormData(int? id);
        Task AddOrUpdateFilingYear(FilingYearModel filingYear);        
        #endregion

    }
}
