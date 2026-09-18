using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.Rendering;


namespace ACA360.Core.Models
{
    public class Plan
    {
        public long RowNum { get; set; }
        public string? id { get; set; }
        public string? ide { get; set; }

        public String? EmployerId { get; set; }

        public string? Name { get; set; }
        public string? MedicalPlan { get; set; }
        public string? BandingType { get; set; }
        
        public string? EligibleFirstOfMonth { get; set; }
        public string? FundingType { get; set; }
        public int? WaitingDays { get; set; }

        public int? PlanRenewal { get; set; }
        public float? PremiumCap { get; set; }

        public bool OfferedSpouse { get; set; }
        public bool ConditionallyOffSpouse { get; set; }
        public bool OfferedDependents { get; set; }
        public bool PlanTermTermination { get; set; }
        public bool MinimumValue { get; set; }
        public bool CodeOneA { get; set; }
        public bool CodeTwoF { get; set; }
        public bool CodeTwoG { get; set; }
        public bool CodeTwoH { get; set; }
        // ICHRA (Individual Coverage HRA) — drives Line 14 codes 1L-1U
        public bool IsIchra { get; set; }
        public string? IchraLocationBasis { get; set; }   // "R" = residence ZIP, "W" = work-site ZIP
        public decimal? IchraSelfOnlyAllow { get; set; }  // employer's monthly self-only ICHRA allowance
        public List<PlanBenefit>? Benefits { get; set; }
        public IEnumerable<PlanBenefit>? Benefit { get; set; } // For DataAuditlog comparison
        public List<SelectListItem> Plan_Banding_Type { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Plan_Waiting_Period { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Plan_Type { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Plan_Funding_Type { get; set; } = new List<SelectListItem>();


    }


    public class PlanBenefit
    {
        public string? Plan_start_value { get; set; }
        public string? Plan_end_value { get; set; }
        public DateTime? Premium_start { get; set; }
        public DateTime? Premium_end { get; set; }
        public decimal? Amount { get; set; }
    }


    public class Plan_Banding_Type
      {
        public int Plan_Banding_Type_ID { get; set; }
        public string? Plan_Banding_Type_Name { get; set; }
        
      }
    public class Plan_Type
    {
        public int Plan_Type_ID { get; set; }
        public string? Plan_Type_Name { get; set; }

    }
    public class Plan_Waiting_Period
    {
        public int Plan_Waiting_Period_ID { get; set; }
        public string? Plan_Waiting_Period_Name { get; set; }

    }
    public class Plan_Funding_Type
    {
        public int Plan_Funding_Type_ID { get; set; }
        public string? Plan_Funding_Type_Name { get; set; }

    }
}
