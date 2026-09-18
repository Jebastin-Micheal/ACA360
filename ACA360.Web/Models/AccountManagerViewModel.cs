// AccountManagerViewModel.cs
using ACA360.Core.Models;
using System.Collections.Generic;

namespace ACA360.Web.Models
{
    public class AccountManagerAssignmentViewModel
    {
        public AccountManager AccountManager { get; set; } = new AccountManager();
        public List<Employer> AllEmployers { get; set; } = new List<Employer>();
        public List<Employer> AssignedEmployers { get; set; } = new List<Employer>();
    }
}