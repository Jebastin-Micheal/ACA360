using ACA360.Core.Models;

namespace ACA360.Web.Models
{
   // Shared ViewModel for both types
public class EmployerAssignmentViewModel
{
    public StaffMember? StaffMember { get; set; }
    public int StaffType_ID { get; set; } // "AccountManager" or "DataAnalyst"
    public List<Employer> AllEmployers { get; set; } = new List<Employer>();
    public List<Employer> AssignedEmployers { get; set; } = new List<Employer>();
    public List<Employer> AvailableEmployers => AllEmployers.Where(e => !AssignedEmployers.Any(a => a.Id == e.Id)).ToList();
}

// Shared StaffMember class
public class StaffMember
{
    public int Id { get; set; }
    public string? AM_name { get; set; }
}
}
