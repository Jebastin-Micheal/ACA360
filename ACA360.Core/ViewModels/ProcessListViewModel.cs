using ACA360.Core.Models;
using ACA360.Core.Models.Tracker;
using System.Collections.Generic;

namespace ACA360.Core.ViewModels
{
    public class ProcessListViewModel
    {
        public IEnumerable<TrackerProcess> Processes { get; set; } = new List<TrackerProcess>();
        public string SortColumn { get; set; }
        public string SortOrder { get; set; }
        public PaginationViewEntity Metadata { get; set; } = new PaginationViewEntity();
    }
}
