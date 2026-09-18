using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class SystemSettingsService : ISystemSettingsService
    {
        private readonly string _connectionString;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SystemSettingsService> _logger;

        public SystemSettingsService(IConfiguration config, IMemoryCache cache, ILogger<SystemSettingsService> logger)
        {
            // FIX: GetConnectionString() can return null — fall back to empty string to satisfy non-nullable field
            _connectionString = config.GetConnectionString("DefaultConnection") ?? string.Empty;
            _cache = cache;
            _logger = logger;
        }

        public async Task<bool> GetMaintenanceModeAsync()
        {
            try
            {
                return await _cache.GetOrCreateAsync("System_MaintenanceMode", async entry =>
                {
                    try
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

                        using var conn = new SqlConnection(_connectionString);
                        var value = await conn.ExecuteScalarAsync<string>(
                            "sp_GetMaintenanceModeValue",
                            new { Key = "MaintenanceMode" },
                            commandType: CommandType.StoredProcedure);

                        return string.Equals(value, "On", StringComparison.OrdinalIgnoreCase) ||
                               value == "1" ||
                               string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
                    }
                    catch (SqlException ex)
                    {
                        // FIX: template now has 3 placeholders matching 3 arguments
                        _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetMaintenanceModeAsync), "Database error checking maintenance mode");
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetMaintenanceModeAsync), "Unexpected error checking maintenance mode");
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetMaintenanceModeAsync), "Cache failure checking maintenance mode");
                return false;
            }
        }

        public async Task SetMaintenanceModeAsync(bool enabled)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync(
                    "sp_SetMaintenanceModeValue",
                    new { Key = "MaintenanceMode", Value = enabled ? "On" : "Off" },
                    transaction: transaction,
                    commandType: CommandType.StoredProcedure);

                transaction.Commit();
            }
            catch (SqlException ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(SetMaintenanceModeAsync), $"Database error setting maintenance mode to {enabled}");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(SetMaintenanceModeAsync), $"Unexpected error setting maintenance mode to {enabled}");
            }
        }

        public async Task SaveSecuritySettingsAsync(SecuritySettingsDto model)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();
            try
            {
                var parameters = new DynamicParameters();
                parameters.Add("@Json", JsonConvert.SerializeObject(model));

                await conn.ExecuteAsync(
                    "usp_SaveSecuritySettings",
                    parameters,
                    transaction: transaction,
                    commandType: CommandType.StoredProcedure);

                transaction.Commit();
            }
            catch (SqlException ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(SaveSecuritySettingsAsync), "Database error saving security settings");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(SaveSecuritySettingsAsync), "Unexpected error saving security settings");
            }
        }

        public async Task<SecuritySettingsDto> GetSecuritySettingsAsync()
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);

                var settings = await conn.QueryAsync<(string SettingKey, string SettingValue)>(
                    "usp_GetSecuritySettings",
                    commandType: CommandType.StoredProcedure
                );

                var dto = new SecuritySettingsDto();

                foreach (var setting in settings)
                {
                    switch (setting.SettingKey)
                    {
                        case "MinPasswordLength":
                            dto.MinPasswordLength = int.TryParse(setting.SettingValue, out var minLen) ? minLen : 8;
                            break;
                        case "RequireUppercase":
                            dto.RequireUppercase = bool.TryParse(setting.SettingValue, out var uppercase) && uppercase;
                            break;
                        case "RequireDigits":
                            dto.RequireDigits = bool.TryParse(setting.SettingValue, out var digits) && digits;
                            break;
                        case "LockoutThreshold":
                            dto.LockoutThreshold = int.TryParse(setting.SettingValue, out var lockout) ? lockout : 5;
                            break;
                        case "LockoutDuration":
                            dto.LockoutDuration = int.TryParse(setting.SettingValue, out var duration) ? duration : 15;
                            break;
                        case "SessionTimeout":
                            dto.SessionTimeout = int.TryParse(setting.SettingValue, out var timeout) ? timeout : 30;
                            break;
                        case "AllowConcurrentSessions":
                            dto.AllowConcurrentSessions = bool.TryParse(setting.SettingValue, out var allow) && allow;
                            break;
                    }
                }

                return dto;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetSecuritySettingsAsync), "Database error fetching security settings");
                return new SecuritySettingsDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetSecuritySettingsAsync), "Unexpected error fetching security settings");
                return new SecuritySettingsDto();
            }
        }

        public async Task<DatabaseSettingsDto> GetDatabaseSettingsAsync()
        {
            try
            {
                var dto = new DatabaseSettingsDto();
                using var conn = new SqlConnection(_connectionString);

                var settings = await conn.QueryAsync<(string SettingKey, string SettingValue)>(
                    "usp_GetDatabaseSettings",
                    commandType: CommandType.StoredProcedure);

                foreach (var s in settings)
                {
                    switch (s.SettingKey)
                    {
                        case "BackupFrequency":
                            dto.BackupFrequency = s.SettingValue;
                            break;
                        case "BackupTime":
                            dto.BackupTime = s.SettingValue;
                            break;
                        case "RetentionDays":
                            if (int.TryParse(s.SettingValue, out var days))
                                dto.RetentionDays = days;
                            break;
                    }
                }

                var history = await conn.QueryAsync<DatabaseBackupRecordDto>(
                    "usp_GetBackupHistory",
                    commandType: CommandType.StoredProcedure);

                dto.BackupHistory = history.ToList();

                return dto;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetDatabaseSettingsAsync), "Database error fetching database settings");
                return new DatabaseSettingsDto { BackupHistory = new List<DatabaseBackupRecordDto>() };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetDatabaseSettingsAsync), "Unexpected error fetching database settings");
                return new DatabaseSettingsDto { BackupHistory = new List<DatabaseBackupRecordDto>() };
            }
        }

        public async Task SaveDatabaseSettingsAsync(DatabaseSettingsDto model, int? userId = null)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync(
                    "usp_SaveDatabaseSettings",
                    new
                    {
                        BackupFrequency = model.BackupFrequency,
                        BackupTime = model.BackupTime,
                        RetentionDays = model.RetentionDays,
                        UpdatedBy = userId
                    },
                    transaction: transaction,
                    commandType: CommandType.StoredProcedure);

                transaction.Commit();
            }
            catch (SqlException ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(SaveDatabaseSettingsAsync), "Database error saving database backup settings");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(SaveDatabaseSettingsAsync), "Unexpected error saving database backup settings");
            }
        }

        public async Task<(bool success, string message)> RunDatabaseBackupAsync(int? userId = null)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);

                var result = await conn.QueryFirstOrDefaultAsync(
                    "usp_RunDatabaseBackup",
                    new { RequestedBy = userId },
                    commandType: CommandType.StoredProcedure);

                if (result == null)
                    return (false, "No response from backup procedure.");

                bool success = Convert.ToInt32(result.Success) == 1;
                string message = (string)result.Message;

                return (success, message);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(RunDatabaseBackupAsync), "Database error running on-demand backup");
                return (false, $"Database error during backup: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(RunDatabaseBackupAsync), "Unexpected error running on-demand backup");
                return (false, $"Unexpected error during backup: {ex.Message}");
            }
        }

        public async Task CleanupOldBackupsAsync(int retentionDays)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync(
                    "usp_CleanupOldBackups",
                    new { RetentionDays = retentionDays },
                    transaction: transaction,
                    commandType: CommandType.StoredProcedure);

                transaction.Commit();
            }
            catch (SqlException ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(CleanupOldBackupsAsync), $"Database error cleaning up backups older than {retentionDays} days");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(CleanupOldBackupsAsync), $"Unexpected error cleaning up backups older than {retentionDays} days");
            }
        }

        public async Task<GeneralSettingsDto> GetGeneralSettingsAsync()
        {
            try
            {
                var dict = await GetConfigDictionaryAsync();
                return new GeneralSettingsDto
                {
                    AppName = dict.GetValueOrDefault("App_Name"),
                    SupportEmail = dict.GetValueOrDefault("Support_Email")
                };
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetGeneralSettingsAsync), "Database error fetching general settings");
                return new GeneralSettingsDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetGeneralSettingsAsync), "Unexpected error fetching general settings");
                return new GeneralSettingsDto();
            }
        }

        public async Task SaveGeneralSettingsAsync(GeneralSettingsDto model)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync(
                    "sp_SaveGeneralSettings",
                    new
                    {
                        AppName = model.AppName,
                        SupportEmail = model.SupportEmail
                    },
                    transaction: transaction,
                    commandType: CommandType.StoredProcedure);

                transaction.Commit();
            }
            catch (SqlException ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(SaveGeneralSettingsAsync), "Database error saving general settings");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(SaveGeneralSettingsAsync), "Unexpected error saving general settings");
            }
        }

        public async Task<SmtpSettingsDto> GetSmtpSettingsAsync()
        {
            try
            {
                var dict = await GetConfigDictionaryAsync();
                return new SmtpSettingsDto
                {
                    Host = dict.GetValueOrDefault("SMTP_Host"),
                    Port = int.TryParse(dict.GetValueOrDefault("SMTP_Port"), out int p) ? p : 587,
                    Username = dict.GetValueOrDefault("SMTP_User"),
                    Password = dict.GetValueOrDefault("SMTP_Pass")
                };
            }
            catch (SqlException ex)
            {
                // FIX: was "GetSmtpSettingsAsync", "Service",) — trailing comma caused "Invalid expression term ')'"
                // FIX: template now has matching placeholders
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetSmtpSettingsAsync), "Database error fetching SMTP settings");
                return new SmtpSettingsDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetSmtpSettingsAsync), "Unexpected error fetching SMTP settings");
                return new SmtpSettingsDto();
            }
        }

        public async Task SaveSmtpSettingsAsync(SmtpSettingsDto model)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync(
                    "sp_SaveSmtpSettings",
                    new
                    {
                        Host = model.Host,
                        Port = model.Port,
                        Username = model.Username,
                        Password = model.Password,
                        EnableSsl = model.EnableSsl
                    },
                    transaction: transaction,
                    commandType: CommandType.StoredProcedure);

                transaction.Commit();
            }
            catch (SqlException ex)
            {
                transaction.Rollback();
                // FIX: was using string literals instead of nameof(), and template had no placeholders
                _logger.LogError(ex, "[{Class}.{Method}] {Description} (Username: {Username})", nameof(SystemSettingsService), nameof(SaveSmtpSettingsAsync), "Database error saving SMTP settings", model.Username);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(SaveSmtpSettingsAsync), "Unexpected error saving SMTP settings");
            }
        }

        public async Task<IrsSettingsDto> GetIrsSettingsAsync()
        {
            try
            {
                var dict = await GetConfigDictionaryAsync();
                return new IrsSettingsDto
                {
                    Tcc = dict.GetValueOrDefault("IRS_TCC"),
                    TransmitterEin = dict.GetValueOrDefault("IRS_EIN"),
                    SoftwareId = dict.GetValueOrDefault("IRS_SoftwareId")
                };
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetIrsSettingsAsync), "Database error fetching IRS settings");
                return new IrsSettingsDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetIrsSettingsAsync), "Unexpected error fetching IRS settings");
                return new IrsSettingsDto();
            }
        }

        public async Task SaveIrsSettingsAsync(IrsSettingsDto model)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync(
                    "sp_SaveIrsSettings",
                    new
                    {
                        Tcc = model.Tcc,
                        TransmitterEin = model.TransmitterEin,
                        SoftwareId = model.SoftwareId
                    },
                    transaction: transaction,
                    commandType: CommandType.StoredProcedure);

                transaction.Commit();
            }
            catch (SqlException ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(SaveIrsSettingsAsync), "Database error saving IRS settings");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(SaveIrsSettingsAsync), "Unexpected error saving IRS settings");
            }
        }

        public async Task<List<AuditLogDto>> GetAuditLogsAsync(int limit = 100)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                return (await conn.QueryAsync<AuditLogDto>(
                    "sp_GetTopAuditLogs",
                    new { Limit = limit },
                    commandType: CommandType.StoredProcedure)).ToList();
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetAuditLogsAsync), "Database error fetching audit logs");
                return new List<AuditLogDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetAuditLogsAsync), "Unexpected error fetching audit logs");
                return new List<AuditLogDto>();
            }
        }

        private async Task<Dictionary<string, string>> GetConfigDictionaryAsync()
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                var rows = await conn.QueryAsync<dynamic>(
                    "sp_GetAllConfigSettings",
                    commandType: CommandType.StoredProcedure);
                return rows.ToDictionary(r => (string)r.ConfigKey, r => (string)r.ConfigValue);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetConfigDictionaryAsync), "Database error fetching config dictionary");
                return new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetConfigDictionaryAsync), "Unexpected error fetching config dictionary");
                return new Dictionary<string, string>();
            }
        }

        public async Task<AuditLogListViewModel> GetPagedAuditLogsAsync(AuditLogRequest request)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                string? searchTerm = !string.IsNullOrEmpty(request.Search) ? $"%{request.Search}%" : null;

                var logs = (await conn.QueryAsync<AuditLogResult>(
                    "sp_GetUnifiedAuditLogs",
                    new
                    {
                        Search = searchTerm,
                        ActivityType = request.ActivityType,
                        StartDate = request.StartDate,
                        EndDate = request.EndDate,
                        SortColumn = request.SortColumn,
                        SortDirection = request.SortDirection,
                        PageNumber = request.Page,
                        PageSize = request.PageSize
                    },
                    commandType: CommandType.StoredProcedure
                )).ToList();

                int total = logs.FirstOrDefault()?.TotalCount ?? 0;

                return new AuditLogListViewModel
                {
                    Logs = logs,
                    CurrentFilter = request,
                    TotalRecords = total
                };
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetPagedAuditLogsAsync), "Database error fetching paged audit logs");
                return new AuditLogListViewModel { Logs = new List<AuditLogResult>(), CurrentFilter = request, TotalRecords = 0 };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetPagedAuditLogsAsync), "Unexpected error fetching paged audit logs");
                return new AuditLogListViewModel { Logs = new List<AuditLogResult>(), CurrentFilter = request, TotalRecords = 0 };
            }
        }

        public async Task<List<AuditLogDto>> GetAllAuditLogsAsync()
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                return (await conn.QueryAsync<AuditLogDto>(
                    "sp_GetAllAuditLogs",
                    commandType: CommandType.StoredProcedure)).ToList();
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetAllAuditLogsAsync), "Database error fetching all audit logs");
                return new List<AuditLogDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetAllAuditLogsAsync), "Unexpected error fetching all audit logs");
                return new List<AuditLogDto>();
            }
        }

        public async Task<bool> GetBoolSettingAsync(string key)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                string? val = await conn.QueryFirstOrDefaultAsync<string>(
                    "sp_GetSettingByKey",
                    new { Key = key },
                    commandType: CommandType.StoredProcedure);

                return bool.TryParse(val, out bool result) && result;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description} (Key: {Key})", nameof(SystemSettingsService), nameof(GetBoolSettingAsync), "Database error fetching boolean setting", key);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description} (Key: {Key})", nameof(SystemSettingsService), nameof(GetBoolSettingAsync), "Unexpected error fetching boolean setting", key);
                return false;
            }
        }

        public async Task UpdateSettingAsync(string key, string value)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync(
                    "sp_UpdateSettingByKey",
                    new { Key = key, Val = value },
                    transaction: transaction,
                    commandType: CommandType.StoredProcedure);

                transaction.Commit();
            }
            catch (SqlException ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description} (Key: {Key})", nameof(SystemSettingsService), nameof(UpdateSettingAsync), "Database error updating setting", key);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "[{Class}.{Method}] {Description} (Key: {Key})", nameof(SystemSettingsService), nameof(UpdateSettingAsync), "Unexpected error updating setting", key);
            }
        }

        public async Task<string> GetSettingValueAsync(string key)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                // FIX: QueryFirstOrDefaultAsync<string> can return null — use ?? string.Empty to satisfy non-nullable return type
                return await conn.QueryFirstOrDefaultAsync<string>(
                    "sp_GetSettingByKey",
                    new { Key = key },
                    commandType: CommandType.StoredProcedure) ?? string.Empty;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description} (Key: {Key})", nameof(SystemSettingsService), nameof(GetSettingValueAsync), "Database error fetching setting value", key);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description} (Key: {Key})", nameof(SystemSettingsService), nameof(GetSettingValueAsync), "Unexpected error fetching setting value", key);
                return string.Empty;
            }
        }       

        #region Error Logs
        public async Task<ErrorLogListViewModel> GetPagedErrorLogsAsync(ErrorLogRequest request)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                // Wrap the search term in wildcards if it exists
                string? searchTerm = !string.IsNullOrEmpty(request.Search) ? $"%{request.Search}%" : null;

                var logs = (await conn.QueryAsync<ErrorLogResult>(
                    "sp_GetPagedErrorLogs",
                    new
                    {
                        Search = searchTerm,
                        Level = request.Level,
                        StartDate = request.StartDate,
                        EndDate = request.EndDate,
                        SortColumn = request.SortColumn,
                        SortDirection = request.SortDirection,
                        PageNumber = request.Page,
                        PageSize = request.PageSize
                    },
                    commandType: CommandType.StoredProcedure
                )).ToList();

                // Extract the total row count from the first record for pagination
                int total = logs.FirstOrDefault()?.TotalCount ?? 0;

                return new ErrorLogListViewModel
                {
                    Logs = logs,
                    CurrentFilter = request,
                    TotalRecords = total
                };
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetPagedErrorLogsAsync), "Database error fetching paged error logs");
                return new ErrorLogListViewModel { Logs = new List<ErrorLogResult>(), CurrentFilter = request, TotalRecords = 0 };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetPagedErrorLogsAsync), "Unexpected error fetching paged error logs");
                return new ErrorLogListViewModel { Logs = new List<ErrorLogResult>(), CurrentFilter = request, TotalRecords = 0 };
            }
        }

        public async Task<object> GetErrorSummaryStatsAsync()
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);

                var stats = await conn.QueryFirstOrDefaultAsync(
                    "usp_GetErrorSummaryStats",
                    commandType: CommandType.StoredProcedure
                );

                if (stats != null)
                {
                    return new
                    {
                        totalLogs = stats.TotalLogs,
                        totalErrors = stats.TotalErrors ?? 0,
                        todayErrors = stats.TodayErrors ?? 0
                    };
                }

                return new { totalLogs = 0, totalErrors = 0, todayErrors = 0 };
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetErrorSummaryStatsAsync), "Database error fetching error log summary counts");
                return new { totalLogs = 0, totalErrors = 0, todayErrors = 0 };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Class}.{Method}] {Description}", nameof(SystemSettingsService), nameof(GetErrorSummaryStatsAsync), "Unexpected error fetching error log summary counts");
                return new { totalLogs = 0, totalErrors = 0, todayErrors = 0 };
            }
        }

        #endregion


        #region FilingYears
        public async Task<(List<FilingYearModel> FilingYears, PaginationViewEntity PageInfo)> GetFilingYearList(PaginationEntity paginationEntity)
        {
            var list = new List<FilingYearModel>();
            PaginationViewEntity paginationMeta = null;

            try
            {
                using var db = new SqlConnection(_connectionString);
                var parameters = new
                {
                    paginationEntity.PageIndex,
                    paginationEntity.PageSize,
                    Search = paginationEntity.Search,
                    paginationEntity.SortColumn,
                    SortOrder = paginationEntity.SortOrder ?? "asc"
                };

                using var multi = await db.QueryMultipleAsync("sp_FilingYear_List", parameters, commandType: CommandType.StoredProcedure);

                var records = await multi.ReadAsync<dynamic>();
                foreach (var r in records)
                {
                    list.Add(new FilingYearModel
                    {
                        Id = r.id ?? 0,
                        Year = r.filingYear ?? 0,
                        FpgPremium = r.fpgPremium ?? 0m,
                        FpgPercent = r.fpgPercent ?? 0m,
                        RopHours = r.ropHours ?? 0,
                        OfferPercent = r.offerPercent ?? 0m,
                        IsActive = (bool?)r.IsActive ?? true
                    });
                }

                if (!multi.IsConsumed)
                {
                    int totalItems = await multi.ReadSingleOrDefaultAsync<int>();
                    int pageSize = paginationEntity.PageSize;
                    int currentPage = paginationEntity.PageIndex;

                    paginationMeta = new PaginationViewEntity
                    {
                        TotalItems = totalItems,
                        CurrentPage = currentPage,
                        PageSize = pageSize,
                        TotalPages = (int)Math.Ceiling((double)totalItems / pageSize),
                        StartPage = (currentPage - 1) * pageSize + 1,
                        EndPage = Math.Min((currentPage - 1) * pageSize + pageSize, totalItems),
                        SortColumn = paginationEntity.SortColumn,
                        SortOrder = paginationEntity.SortOrder,
                        RecordCount = totalItems,
                        PageNumber = currentPage,
                        TotalCount = totalItems
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetFilingYearList", "FilingYearService", "Failed to retrieve Filing Year list", "DB");
                throw;
            }

            return (list, paginationMeta);
        }

        public async Task<FilingYearModel> GetFilingYearFormData(int? id)
        {
            var model = new FilingYearModel();
            try
            {
                using var db = new SqlConnection(_connectionString);

                // Fetching the dynamic record
                var record = await db.QueryFirstOrDefaultAsync<dynamic>(
                    "sp_GetFilingYearFormData",
                    new { Id = id },
                    commandType: CommandType.StoredProcedure);

                if (record != null)
                {
                    // Explicitly cast to nullable types first to prevent RuntimeBinderException
                    model.Id = (int?)record.id ?? 0;
                    model.Year = (int?)record.filingYear ?? 0;

                    // SQL 'money' and 'decimal' fields cast safely to C# decimal?
                    model.FpgPremium = (decimal?)record.fpgPremium ?? 0m;
                    model.FpgPercent = (decimal?)record.fpgPercent ?? 0m;
                    model.OfferPercent = (decimal?)record.offerPercent ?? 0m;
                    model.PenaltyAAnnual = (decimal?)record.PenaltyAAnnual ?? 0m;
                    model.PenaltyBAnnual = (decimal?)record.penaltyBAnnual ?? 0m;
                    model.RopHours = (int?)record.ropHours ?? 0;
                    model.IsActive = (bool?)record.IsActive ?? true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetFilingYearFormData", "FilingYearService", "Failed to retrieve Filing Year form data", "DB");
                throw;
            }

            return model;
        }

        public async Task AddOrUpdateFilingYear(FilingYearModel filingYear)
        {
            try
            {
                using var db = new SqlConnection(_connectionString);
                var parameters = new
                {
                    id = filingYear.Id,
                    filingYear = filingYear.Year,
                    fpgPremium = filingYear.FpgPremium,
                    fpgPercent = filingYear.FpgPercent,
                    ropHours = filingYear.RopHours,
                    offerPercent = filingYear.OfferPercent,
                    PenaltyAAnnual = filingYear.PenaltyAAnnual,
                    PenaltyBAnnual = filingYear.PenaltyBAnnual,
                    isActive = filingYear.IsActive

                };

                await db.ExecuteAsync("sp_Action_FilingYear", parameters, commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddOrUpdateFilingYear", "FilingYearService", "Failed to insert/update Filing Year", "DB");
                throw;
            }
        }

        public async Task DeleteFilingYear(int id)
        {
            try
            {
                using var db = new SqlConnection(_connectionString);
                await db.ExecuteAsync("sp_FilingYear_Delete", new { id = id }, commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteFilingYear", "FilingYearService", "Failed to delete Filing Year", "DB");
                throw;
            }
        }
        #endregion

    }
}