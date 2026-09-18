using System;

namespace ACA360.Core.Models.Tracker
{
    public class MasterExportModel
    {
        // Employer Core
        public string EmployerName { get; set; }
        public string EIN { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string ContactName { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string FirmName { get; set; }
        
        // Employer Details
        public string FundingType { get; set; }
        public string FormType { get; set; }
        public string PlanYear { get; set; }
        public string LevelOfComplexity { get; set; }
        
        // Assigned Staff
        public string AccountManager { get; set; }
        public string DataAnalyst { get; set; }
        public string SalesRep { get; set; }
        
        // Service Details
        public string ServiceName { get; set; }
        public string ServiceStatus { get; set; }
        public int? ServicePlanYear { get; set; }
        public string ServiceNotes { get; set; }
        public string ReceiptID { get; set; }
    }
}
