    using ACA360.Core.Models;
    using Microsoft.AspNetCore.Mvc.Rendering;

    namespace ACA360.Web.Models
    {
        public class EmployerDetailsViewModel
        {
            public string? EmployerId { get; set; }     
            public int ACA_EmployerId { get; set; }     
       
            public string? EIN { get; set; }                  
            public string? Name { get; set; }                
            public string? Type { get; set; }                 
            public string? FilingYear { get; set; }           

            public string? Address1 { get; set; }             
            public string? Address2 { get; set; }             
            public string? City { get; set; }                 
            public string? State { get; set; }
            public string? StateId { get; set; }
            public string? ZipCode { get; set; }              
            public string? Country { get; set; }              

            public bool IsForeignAddress { get; set; }        
            public bool IsCorrected { get; set; }
            public bool enableCallCenter { get; set; }
            public EmployerInfoModel? Employer { get; set; }
            public string? BrokerName { get; set; }
            public string? FirmName { get; set; }
            public string? SalesRepName { get; set; }
            public string? BillingContactName { get; set; }
            public List<SelectListItem>? FirmList { get; set; }
            public List<SelectListItem>? BrokerList { get; set; }
            public List<SelectListItem>? SalesRepList { get; set; }
            public List<SelectListItem>? AccountManagerList { get; set; }
            public List<SelectListItem>? DataAnalystList { get; set; }
            public List<SelectListItem>? ContactsList { get; set; }
            public List<SelectListItem>? BillingContactsList { get; set; }
        public List<ContactDropdownItem> RawContacts { get; set; } = new List<ContactDropdownItem>();
        public List<ContactDropdownItem> RawBillingContacts { get; set; } = new List<ContactDropdownItem>();
        public List<SelectListItem>? StateList { get; set; }
        public List<SelectListItem>? CountryList { get; set; }
        public EmployerServiceInfoViewModel ServiceInfo { get; set; } = new EmployerServiceInfoViewModel();
        public Employer1094ViewModel? Employer1094ViewModel { get; set; } = new Employer1094ViewModel();
        public Employer Employer_otherdetails { get; set; } = new Employer();
        public List<SelectListItem>? AffiliateList { get; set; }
        public EmployerDropdownDataModel DropdownData { get; set; } = new EmployerDropdownDataModel();
    }
    }
