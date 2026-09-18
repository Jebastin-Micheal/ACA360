// AccountManagerViewModel.cs
using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
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

    public class UserAccountsFormViewModel
    {
        public UserAccounts UserAccounts { get; set; } = new UserAccounts();
        public List<SelectListItem> StatusOptions { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> StateOptions { get; set; } = new List<SelectListItem>();
    }
}