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

        public bool RememberMe { get; set; }
    }

}
