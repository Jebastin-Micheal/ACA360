using ACA360.Core.Models;
using ACA360.Core.Models.Tracker;
using System.Collections.Generic;

namespace ACA360.Core.ViewModels
{
    public class ServiceListViewModel
    {
        public IEnumerable<ServiceItem> Services { get; set; } = new List<ServiceItem>();
        public string SortColumn { get; set; }
        public string SortOrder { get; set; }
        public PaginationViewEntity Metadata { get; set; } = new PaginationViewEntity();
    }
}