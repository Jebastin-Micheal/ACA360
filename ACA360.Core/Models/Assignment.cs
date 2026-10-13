using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{


    public class AccountManager
    {
        public int Id { get; set; }
        [Required]
        [DisplayName("Account Manager Name")]
        public string? AM_name { get; set; }
        public DateTime CreatedOn { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? ModifiedBy { get; set; }
        // Additional property for count
        public int EmployerCount { get; set; }
        public bool IsActive { get; set; }

    }
   
    public class AccountManagerAssignmentViewModel
    {
        public AccountManager AccountManager { get; set; } = new AccountManager();
        public List<Employer> AllEmployers { get; set; }   =new List<Employer>();
        public List<Employer> AssignedEmployers { get; set; } = new List<Employer>();
        public string? CurrentAdminId { get; set; }   // The admin making the assignment
    }

    public class DataAnalyst
    {
        public int Id { get; set; }
        public string? AnalystName { get; set; }
        public string? AM_name { get; set; }

        // Add other staff properties as needed
    }

    public class DataAnalystAssignment
    {
        public int AssignmentID { get; set; }
        public int EmployerID { get; set; }
        public int Analyst_UserID { get; set; }
        public int AssignedBy_AdminID { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedOn { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public int? ModifiedBy { get; set; }

        // Navigation properties
        public Employer Employer { get; set; } = new Employer();
        public DataAnalyst DataAnalyst { get; set; } = new DataAnalyst();
        public string? AssignedByAdmin { get; set; }
        public string? DataAnalystName { get; set; }
    }

    public class DataAnalystAssignmentViewModel
    {
        public DataAnalyst DataAnalyst { get; set; } = new DataAnalyst();
        public List<Employer> AllEmployers { get; set; } = new List<Employer>();
        public List<Employer> AssignedEmployers { get; set; } = new List<Employer>();
        public string CurrentAdminId { get; set; } = string.Empty;
        public string? CurrentUserId { get; set; } // For CreatedBy/ModifiedBy
    }

    // One row of tbl_AM_Assignment_History / tbl_DA_Assignment_History
    public class EmployerAssignmentHistoryItem
    {
        public int HistoryID { get; set; }
        public int EmployerID { get; set; }
        public int StaffUserID { get; set; }
        public string? StaffName { get; set; }
        public string? AssignedByName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsActive { get; set; }
    }

    public class EmployerAssignmentHistoryModel
    {
        public int EmployerId { get; set; }
        public string EmployerName { get; set; } = string.Empty;
        public List<EmployerAssignmentHistoryItem> AccountManagerHistory { get; set; } = new List<EmployerAssignmentHistoryItem>();
        public List<EmployerAssignmentHistoryItem> DataAnalystHistory { get; set; } = new List<EmployerAssignmentHistoryItem>();

        // Which part the popup shows: "AM", "DA", or null/empty for both
        public string? Section { get; set; }
    }

    // One employer that is already actively assigned to a different AM / DA
    public class AssignmentConflictItem
    {
        public int EmployerId { get; set; }
        public string EmployerName { get; set; } = string.Empty;
        public string? CurrentStaffName { get; set; }
    }
}
