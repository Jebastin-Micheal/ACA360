using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public class PagedResult<T>
    {
        // This matches the '.Logs' usage in your Controller/Repo
        public IEnumerable<T> Logs { get; set; }

        // This matches the '.TotalCount' usage
        public int TotalCount { get; set; }

        public PagedResult()
        {
            Logs = new List<T>();
        }
    }
}