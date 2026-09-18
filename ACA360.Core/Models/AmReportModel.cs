using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class AmReportModel
    {
        public string EmployerName { get; set; }
        public string ServiceName { get; set; }
        public string AccountManager { get; set; }
        public string DataAnalyst { get; set; }
        public int? FilingYear { get; set; }
        public string ProcessStep1095 { get; set; }
        public DateTime? FollowUpDate { get; set; }
    }

    public class AccountManagerModel
    {
        public int StaffId { get; set; }
        public string StaffName { get; set; }
    }

    public class DataAnalystModel
    {
        public int StaffId { get; set; }
        public string StaffName { get; set; }
    }
}
