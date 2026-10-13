using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Web.Models
{
    public class SetNewPasswordViewModel
    {
        public string? Username { get; set; }

        [Required(ErrorMessage = "New password is required.")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        public string? NewPassword { get; set; }

        [Required(ErrorMessage = "Please confirm your password.")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string? ConfirmPassword { get; set; }
    }
}
