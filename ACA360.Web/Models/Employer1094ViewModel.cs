using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Web.Models
{
    public class Employer1094ViewModel
    {
        public string? EmployerId { get; set; }
        public string? FilingYear { get; set; }
        public string? EIN { get; set; }
        public string? Name { get; set; }
        public string? FormType { get; set; }
        public string? OriginCode { get; set; }
        public bool CertA { get; set; }
        public bool CertB { get; set; }
        public bool CertC { get; set; }
        public bool CertD { get; set; }
        public List<SelectListItem> FormTypeOptions { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> OriginCodeOptions { get; set; } = new List<SelectListItem>();
        public int[] MinimumCoverage { get; set; } = new int[13];
        public int[] FullTime { get; set; } = new int[13];
        public int[] Total { get; set; } = new int[13];
        public int[] AggregateGroup { get; set; } = new int[13];
        public int[] Sec4980H { get; set; } = new int[13];
        public bool DisableAutoCounts { get; set; }

    }
}
