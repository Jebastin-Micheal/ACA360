// AccountManagerViewModel.cs
using ACA360.Core.Models;
using System.Collections.Generic;

namespace ACA360.Web.Models
{
    public class UserAccountsViewModel
    {
        public List<UserAccounts> UserAccounts { get; set; } = new List<UserAccounts>();
        public PaginationViewEntity Metadata { get; set; } = new PaginationViewEntity();

        public List<RoleSummary> RoleSummary { get; set; } = new List<RoleSummary>();
        public string? Search { get; set; }
        public int SortColumn { get; set; }
        public string? SortOrder { get; set; }
    }
}