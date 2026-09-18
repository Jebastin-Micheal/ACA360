using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace ACA360.Core.Models
{
    public class UserProfileViewModel
    {
        public string? UserId { get; set; }
        public int RoleId { get; set; }
        public string? RoleName { get; set; }
        public int RefId { get; set; } // Links to Staff_ID, Employer_ID, etc.

        // Identity
        [Display(Name = "Username")]
        public string? UserName { get; set; } // From tbl_User (Read Only)

        [Display(Name = "First Name")]
        [Required]
        public string? FirstName { get; set; }

        [Display(Name = "Last Name")]
        [Required]
        public string? LastName { get; set; }

        [EmailAddress]
        [Required]
        public string? Email { get; set; }

        [Phone]
        public string? PhoneNumber { get; set; }

        // Location (From Staff/Employer/Broker tables)
        [Display(Name = "Address 1")]
        public string? Address1 { get; set; }

        [Display(Name = "Address 2")]
        public string? Address2 { get; set; }

        public string? City { get; set; }
        public string? State { get; set; }

        [Display(Name = "Zip Code")]
        public string? ZipCode { get; set; }

        public bool IsMFAEnabled { get; set; }
        public string? ActiveTab { get; set; } = "account"; // Default to 'account'
        public ChangePasswordViewModel PasswordModel { get; set; } = new ChangePasswordViewModel();
        public string? ProfilePicture { get; set; } // Stores the path string (for DB)
        public IFormFile? ProfileImage { get; set; }

        public string? Profile_Name { get; set; }

        public int ProfileHealthPercentage { get; set; }
    }

    public class ChangePasswordViewModel
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string? CurrentPassword { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6)]
        [Display(Name = "New Password")]
        public string? NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string? ConfirmPassword { get; set; }
    }
}