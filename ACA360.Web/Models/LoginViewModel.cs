using System.ComponentModel.DataAnnotations;

namespace ACA360.Web.Models
{
    public class LoginViewModel
    {
        [Required]
        [EmailAddress]
        public string? Username { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        /// <summary>The page that sent the user to sign in (only addresses on this site are followed).</summary>
        public string? ReturnUrl { get; set; }
    }

}
