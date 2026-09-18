using ACA360.Core.Models;

namespace ACA360.Web.Models
{
    public class EmployerInfoMainDisplayViewModel
    {
        public EmployerInfoModel? Employer { get; set; }
        public string? BrokerName { get; set; }
        public string? FirmName { get; set; }
        public string? SalesRepName { get; set; }
        public string? BillingContactName { get; set; }
    }

}
