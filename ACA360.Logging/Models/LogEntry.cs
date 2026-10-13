using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Logging.Models
{
    public class LogEntry
    {
        public string Level { get; set; } = "Error";
        public string CallSite { get; set; } = "";
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
        public string StackTrace { get; set; } = "";
        public string AdditionalInfo { get; set; } = "";
        public string UserIp { get; set; } = "";
        public DateTime LoggedOnDate { get; set; } = DateTime.UtcNow;
    }
    public class ErrorLogDto
    {
        public int Id { get; set; }
        public string? Level { get; set; }
        public string? CallSite { get; set; }
        public string? Type { get; set; }
        public string? Message { get; set; }
        public string? StackTrace { get; set; }
        public string? AdditionalInfo { get; set; }
        public string? UserIp { get; set; }
        public DateTime LoggedOnDate { get; set; }
    }
    public class ErrorLogPagedResult
    {
        public int TotalCount { get; set; }
        public IEnumerable<ErrorLogDto>? Records { get; set; }
    }
    public class LogChartDto
    {
        public string? Label { get; set; }
        public int Value { get; set; }
    }
    public class LogStatsResult
    {
        public IEnumerable<LogChartDto> Levels { get; set; } = new List<LogChartDto>();
        public IEnumerable<LogChartDto> Trend { get; set; } = new List<LogChartDto>();
    }
}
