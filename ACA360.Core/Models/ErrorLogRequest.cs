using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class ErrorLogRequest
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? Search { get; set; }
        public string? Level { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? SortColumn { get; set; }
        public string? SortDirection { get; set; }
    }
    public class ErrorLogResult
    {
        public int TotalCount { get; set; } // Required for pagination
        public int LogId { get; set; }
        public string Level { get; set; } = string.Empty;
        public string CallSite { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string StackTrace { get; set; } = string.Empty;
        public string AdditionalInfo { get; set; } = string.Empty;
        public string User_Ip { get; set; } = string.Empty;
        public DateTime LoggedOnDate { get; set; }
    }

    public class ErrorLogListViewModel
    {
        public List<ErrorLogResult> Logs { get; set; } = new();
        public ErrorLogRequest CurrentFilter { get; set; } = new();
        public int TotalRecords { get; set; }

        public int TotalPages => TotalRecords == 0 ? 1 : (int)Math.Ceiling((double)TotalRecords / CurrentFilter.PageSize);
    }
}
