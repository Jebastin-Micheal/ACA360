using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Razor.Tokenizer;

namespace ACA360.Core.Models
{
    public class Employer
    {
        public bool IsMailed;
        public bool IsEfiled;
        public string? CurrentStepName;
        public DateTime StepDueDate;
        public int ComplianceStatusId;

        public string? Id { get; set; }
        public int? Aca_Id { get; set; }      
        
        public string? FilingYear { get; set; }
        public int? CompanyId { get; set; }  
        public string? TaxId { get; set; } 
        public string? EIN { get; set; }
        public string? Name { get; set; }
        public string? ContactName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Type { get; set; }

        
        //For employer details
        public string? Address1 { get; set; }          
        public string? Address2 { get; set; }          
        public string? City { get; set; }              
        public string? State { get; set; }             
        public string? StateId { get; set; }             
        public string? ZipCode { get; set; }           
        public string? Country { get; set; }           
        public string? CountryId { get; set; }           
        public bool IsForeignAddress { get; set; }     
        public bool IsCorrected { get; set; }    
        public bool enableCallCenter { get; set; }

        //1094 details
        public string? FormType { get; set; }
        public string? OriginCode { get; set; }

        // FIX: Add this property
        public int TotalNumberForms { get; set; }
        // ALE Checkboxes: Monthly arrays
        public int[] MinimumCoverage { get; set; } = new int[13];
        //public bool[] MinimumCoverage { get; set; } = new bool[13];
        public int[] FullTime { get; set; } = new int[13];
        public int[] Total { get; set; } = new int[13];
        //public int[] AggregateGroup { get; set; } = new int[13];
        public int[] AggregateGroup { get; set; } = new int[13];
        public int[] Sec4980H { get; set; } = new int[13];

        // Certifications A–D
        public bool CertA { get; set; }
        public bool CertB { get; set; }
        public bool CertC { get; set; }
        public bool CertD { get; set; }
        public bool DisableAutoCounts { get; set; }
        // 1094details end


        public bool IsAssigned { get; set; }
        public string? AssignedAM { get; set; }
        public string? AssignedDA { get; set; }
        public EmployerImportantInfo ImportantInfo { get; set; } = new();
    }

    public class EmployerLookupDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool IsPrimary { get; set; }
        public string? Initials { get; set; }
        public string? ColorClass { get; set; }
    }
    public class EmployerDropdownDataModel
    {
        public Dictionary<int, string> Firms { get; set; } = new();
        public Dictionary<int, string> Brokers { get; set; } = new();
        public Dictionary<int, string> SalesReps { get; set; } = new();
        public Dictionary<int, string> Affiliates { get; set; } = new();
        public Dictionary<int, string> AccountManagers { get; set; } = new();
        public Dictionary<int, string> DataAnalysts { get; set; } = new();
        public Dictionary<int, string> Industries { get; set; } = new();
        public Dictionary<int, string> DataTypes { get; set; } = new();
        public List<ContactDropdownItem> Contacts { get; set; } = new();
        public List<ContactDropdownItem> BillingContacts { get; set; } = new();
        public Dictionary<int, string> Country { get; set; } = new();
        public Dictionary<int, string> State { get; set; } = new();
        // Add this specifically for the Broker Contact info (Result #11 in your SP)
        public ContactDropdownItem BrokerEntityDetails { get; set; } = new ContactDropdownItem();
        // --- Add these for the new Affiliate logic ---
        public List<AffiliateSummary> RawAffiliateData { get; set; } = new();

    }
    public class AffiliateSummary
    {
        public int AffiliateId { get; set; }
        public string? AffiliateName { get; set; }
        public string? EIN { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public string? Phone { get; set; }   
        public string? Email { get; set; }   
        public int NumberOfEmployees { get; set; }   
        public bool IsMain { get; set; } // To distinguish Parent from Secondary
    }
    public class ContactDropdownItem
    {
        public string? Key { get; set; }   // The ID (Value)
        public string? Value { get; set; } // The Name (Text)
        public string? Phone { get; set; } // Extra Data
        public string? Email { get; set; } // Extra Data
        public string? Connect_User { get; set; }
    }

    public class EmployerImportantInfo
    {
        public string? EmployerId { get; set; }
        public string? MailMemo { get; set; }
        public DateTime? MostRecentHireDate { get; set; }
        public DateTime? MostRecentTerminationDate { get; set; }

        public int SafeHarbor2F { get; set; }
        public int SafeHarbor2G { get; set; }
        public int SafeHarbor2H { get; set; }


    }
    public class PaginationMetadata
    {
        public int RecordCount { get; set; }
        public int StartPage { get; set; }
        public int EndPage { get; set; }
        public int PageNumber { get; set; }   // Already exists, probably
        public int TotalCount { get; set; }

        // 👇 Add this property
        public int PageIndex => PageNumber; // Or just rename PageNumber to PageIndex if preferred

        public int PageSize { get; set; } // Add this if TotalPages is needed

        // 👇 Optional: If TotalPages is referenced
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);


    }
    
}
