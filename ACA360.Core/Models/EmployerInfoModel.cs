using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace ACA360.Core.Models
{
    public class EmployerInfoModel
    {
        public string? Stadress { get; set; }
        public string? Stadress2 { get; set; }
        public string? EmployerId { get; set; }
        public string? TrackerEmployerId { get; set; }
        public string? Name { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public int? StateId { get; set; }
        public int? CountryId { get; set; }

        public string? Zip { get; set; }
       
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? PlanYear { get; set; }
        
        public string? AccountManagerName { get; set; }
        public int? AccountManagerId { get; set; }
        public string? Affiliate_Name { get; set; }
        public int? AffiliateId { get; set; }

        public int? BrokerId { get; set; }
        public string? BrokerPhone { get; set; }
        public string? BrokerEmail { get; set; }
        public string? EIN { get; set; }
        
        public string? FundingType { get; set; }
        public int? TotalEmployees { get; set; }
        public int? Total1095C { get; set; }
        public string? Vendor { get; set; }

        public string? SalesRepId { get; set; }
        public string? DataAnalyst { get; set; }

        public string? FormType { get; set; }
        public string? WaitingPeriod { get; set; }
        public string? BandingType { get; set; }
        
        public int? OverProposedFormCount { get; set; }
        public decimal? OverProposedFormCountPrice { get; set; }

        public int? FirmId { get; set; }

        public string? IndustryName { get; set; }
        public int? IndustryId { get; set; }

        public string? DataTypeName { get; set; }
        public int? DataTypeId { get; set; }
        public string? Contact { get; set; }
        public string? ContactName { get; set; }
        public int? ContactId { get; set; }
        public string? ContactPhone { get; set; }
        public string? ContactEmail { get; set; }
        public string? BillingContactName { get; set; }
        public int? BillingContactId { get; set; }
        public string? BillingPhone { get; set; }
        public string? BillingEmail { get; set; }
        public bool IsBilling { get; set; }
      


    }


}
