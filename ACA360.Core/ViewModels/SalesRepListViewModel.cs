using ACA360.Core.Models;
using ACA360.Core.Models.Tracker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.ViewModels
{
    public class SalesRepListViewModel
    {
        public IEnumerable<SalesRepItem> SalesReps { get; set; } = new List<SalesRepItem>();
        public string SortColumn { get; set; }
        public string SortOrder { get; set; }
        public PaginationViewEntity Metadata { get; set; } = new PaginationViewEntity();
    }
}