using ACA360.Logging.Interfaces;
using ACA360.Logging.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Data;

namespace ACA360.Logging.Services
{
    public class LoggerService : ILoggerService
    {
        private readonly string _connectionString;

        public LoggerService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        public void LogError(Exception ex, string callSite, string type, string additionalInfo, string userIp)
        {
            var log = new LogEntry
            {
                CallSite = callSite,
                Type = type,
                Message = ex?.Message ?? "Unknown exception",
                StackTrace = ex?.ToString() ?? "No stack trace",
                AdditionalInfo = additionalInfo ?? "",
                UserIp = string.IsNullOrWhiteSpace(userIp) ? "Unknown" : userIp,
            };

            SaveLogToDatabase(log);
        }
        // NEW: LogInfo Implementation
        public void LogInfo(string message, string callSite, string additionalInfo, string userIp)
        {
            var log = new LogEntry
            {
                CallSite = callSite,
                Type = "Info", // Hardcode type as Info
                Message = message,
                StackTrace = "", // No stack trace for info logs
                AdditionalInfo = additionalInfo ?? "",
                UserIp = string.IsNullOrWhiteSpace(userIp) ? "Unknown" : userIp,
                LoggedOnDate = DateTime.Now // Ensure your DB handles this or set it here
            };

            SaveLogToDatabase(log);
        }
        private void SaveLogToDatabase(LogEntry log)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                connection.Execute(
                    "usp_InsertErrorLog",
                    new
                    {
                        log.Level,
                        log.CallSite,
                        log.Type,
                        log.Message,
                        log.StackTrace,
                        log.AdditionalInfo,
                        User_Ip = log.UserIp,
                        log.LoggedOnDate
                    },
                    commandType: System.Data.CommandType.StoredProcedure
                );
            }
            catch (Exception loggingEx)
            {
                // 🚨 Fallback logging (Safe mode — avoids infinite recursion)
                try
                {
                    System.IO.File.AppendAllText("ErrorLog_Fallback.txt",
                        $"\n[{DateTime.UtcNow}] Logging Error: {loggingEx.Message}\nOriginal Error: {log.Message}\n");
                }
                catch
                {
                    // Optional: swallow silently if file write also fails
                }
            }
        }

        public async Task<ErrorLogPagedResult> GetLogsAsync(
    int page, int pageSize, string search, string level, DateTime? startDate, DateTime? endDate)
        {
            using var conn = new SqlConnection(_connectionString);

            var result = await conn.QueryAsync<ErrorLogDto, int, (ErrorLogDto, int)>(
                "usp_GetErrorLogs",
                (log, total) => (log, total),
                new { Page = page, PageSize = pageSize, Search = search, Level = level, StartDate = startDate, EndDate = endDate },
                splitOn: "TotalCount", // only works if SP was updated
                commandType: CommandType.StoredProcedure
            );

            var list = result.ToList();
            return new ErrorLogPagedResult
            {
                TotalCount = list.Count > 0 ? list.First().Item2 : 0,
                Records = list.Select(x => x.Item1)
            };
        }


        public async Task<LogStatsResult> GetLogStatsAsync()
        {
            using var conn = new SqlConnection(_connectionString);

            using var multi = await conn.QueryMultipleAsync(
                "usp_GetLogStats",
                commandType: CommandType.StoredProcedure
            );

            var levelStats = (await multi.ReadAsync()).Select(x => new LogChartDto
            {
                Label = x.Level,
                Value = (int)x.Count
            });

            var trendStats = (await multi.ReadAsync()).Select(x => new LogChartDto
            {
                Label = Convert.ToDateTime(x.LogDate).ToString("yyyy-MM-dd"),
                Value = (int)x.Count
            });

            return new LogStatsResult
            {
                Levels = levelStats,
                Trend = trendStats
            };
        }



        public async Task CleanupOldLogsAsync(int days = 90)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync("usp_CleanupOldLogs", new { Days = days }, commandType: CommandType.StoredProcedure);
        }

    }
}
