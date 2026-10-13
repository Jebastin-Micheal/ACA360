using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public class PagedResult<T>
    {
        // This matches the '.Logs' usage in your Controller/Repo
        public IEnumerable<T> Logs { get; set; }

        // This matches the '.TotalCount' usage
        public int TotalCount { get; set; }

        /// <summary>Summary-card counts for the whole view, ignoring the status filter.</summary>
        public FileStatusCounts StatusCounts { get; set; } = new();

        public PagedResult()
        {
            Logs = new List<T>();
        }
    }

    public class FileStatusCounts
    {
        public int Total { get; set; }
        public int ActionRequired { get; set; }
        public int Processing { get; set; }
        public int Completed { get; set; }
    }
}
