using ACA360.Logging.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Logging.Interfaces
{
    public interface ILoggerService
    {
        void LogError(Exception ex, string callSite, string type, string additionalInfo, string? userIp = null);
        public Task<ErrorLogPagedResult> GetLogsAsync(int page, int pageSize, string search, string level, DateTime? startDate, DateTime? endDate);
        public Task CleanupOldLogsAsync(int days = 90);
        Task<LogStatsResult> GetLogStatsAsync();
        void LogInfo(string message, string callSite, string additionalInfo, string userIp);
    }
}
