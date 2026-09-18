using ACA360.Core.Models;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


// AccountManager.cs
namespace ACA360.Core.Models
{
    public class UserAccounts
    {
        public long UserAccounts_ID { get; set; }

        [Required]
        [DisplayName("First Name")]
        public string? UserAccounts_FName { get; set; }

        [Required]
        [DisplayName("Last Name")]
        public string? UserAccounts_LName { get; set; }
       
        [DisplayName("Name")]
        public string? UserAccounts_Full_Name { get; set; }
        [DisplayName("Role")]
        public string? UserAccounts_Role_Name { get; set; }

        [DisplayName("Address 1")]
        public string? UserAccounts_Addr1 { get; set; }

        [DisplayName("Address 2")]
        public string? UserAccounts_Addr2 { get; set; }

        [DisplayName("City")]
        public string? UserAccounts_City { get; set; }

        [DisplayName("State")]
        public long? UserAccounts_State { get; set; }

        [DisplayName("Zip Code")]
        public string? UserAccounts_Zip { get; set; }

        [DisplayName("Phone")]
        public string? UserAccounts_Phone { get; set; }

        [Required]
        [EmailAddress]
        [DisplayName("Email")]
        public string? UserAccounts_Email { get; set; }
        [Required]
        [DisplayName("Password")]
        public string? UserAccounts_Password { get; set; }
        [Required]
        [DisplayName("Status")]
        public long? UserAccounts_Status { get; set; }
        [DisplayName("Role")]

        [Required]
        public int? UserAccounts_Role { get; set; }

        public DateTime? createdon { get; set; }
        public long? createdby { get; set; }
        public DateTime? modifiedon { get; set; }
        public long? modifiedby { get; set; }
        public int AssginCount { get; set; }

        [DisplayName("Profile Picture")]
        public string? ProfilePicture { get; set; }

        [DisplayName("MFA Status")]
        public int? IsMFA { get; set; }

        [DisplayName("Upload Profile Picture")]
        public IFormFile? ProfilePictureFile { get; set; }
    }


    public class RoleSummary
    {
        public long? UserAccounts_Role { get; set; }
        public string? RoleName { get; set; }
        public int TotalUsers { get; set; }
        public string? AllUsers { get; set; }
        public string? AllProfilePictures { get; set; }
    }

}