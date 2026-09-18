using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACA360.Core.Models;
using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public class FlagListViewModel
    {
        public IEnumerable<FlagDefinition> Flags { get; set; } = new List<FlagDefinition>();
        public string SortColumn { get; set; }
        public string SortOrder { get; set; }
        public PaginationViewEntity Metadata { get; set; } = new PaginationViewEntity();
    }
}
