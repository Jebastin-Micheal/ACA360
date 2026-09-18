using ACA360.Core.Models;
using System.Collections.Generic;

namespace ACA360.Web.Models
{
    public class FilingYearViewModel
    {
        public List<FilingYearModel> FilingYears { get; set; }
        public PaginationViewEntity Metadata { get; set; }
        public string Search { get; set; }
        public int SortColumn { get; set; }
        public string SortOrder { get; set; }
    }
    public class FilingYearFormViewModel
    {
        public FilingYearModel FilingYear { get; set; }
    }
}