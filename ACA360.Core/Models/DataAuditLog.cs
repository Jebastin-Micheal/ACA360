using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class DataAuditLog
    {
        public Guid Id { get; set; }
        public string Table_Name { get; set; } = string.Empty;
        public string Record_Id { get; set; } = string.Empty;
        public string Action_Type { get; set; } = string.Empty;
        public string? Old_Values { get; set; }
        public string? New_Values { get; set; }
        public string User_Id { get; set; } = string.Empty;
        public string User_Name { get; set; } = string.Empty;
        public string User_Role { get; set; } = string.Empty;
        public string? IP_Address { get; set; }
        public DateTime? Created_at { get; set; }
    }

    public class AuditLogFilter
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? User_Id { get; set; }
        public string? Record_Id { get; set; }
        public string? Action_Type { get; set; }
        public string? Table_Name { get; set; }
    }
}
