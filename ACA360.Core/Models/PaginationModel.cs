using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class PaginationEntity
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? Search { get; set; } = "";
        public string? SearchColumn { get; set; }
        public string? SearchFields { get; set; }
        public string? TypeFilter { get; set; }
        public int SortColumn { get; set; } = 0;
        public string? SortOrder { get; set; } = "asc";
        public string? fillingYear { get; set; } = DateTime.Now.Year.ToString();
        public int? StatusFilter { get; set; }
    }
    public class PaginationViewEntity
    {
        public int TotalItems { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public int StartPage { get; set; }
        public int EndPage { get; set; }
        public int SortColumn { get; set; } = 0;
        public string? SortOrder { get; set; } = "asc";
        public string? filingYear { get; set; } = DateTime.Now.Year.ToString();

        public int RecordCount { get; set; } 
        public int PageNumber { get; set; }
        public int TotalCount { get; set; }
    }
}
