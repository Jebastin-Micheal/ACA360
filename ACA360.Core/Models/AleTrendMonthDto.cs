using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class AleTrendMonthDto
    {
        public int MonthNum { get; set; }
        public int? FullTimeCount { get; set; }
        public int? TotalCount { get; set; }
        public int? AggregateGroupFlag { get; set; }
        public int? MinimumCoverageOffered { get; set; }
        public string? Section4980HCode { get; set; }
    }
}
