using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class UserModel
    {
        public string? User_ID { get; set; }
        public string? User_Name { get; set; }
        public string? Temp_Password { get; set; }
        public string? User_Password { get; set; }
        public int Role_ID { get; set; }
        public int Flag { get; set; }
        public long? Ref_ID { get; set; }
        public string? Profile_Picture { get; set; }
        public int? IsMFA { get; set; }
        public int? User_LandingPage { get; set; }
        public DateTime? CreatedOn { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? Role_Name { get; set; }

        // Added for ASP.NET Core Identity
        public string? PasswordHash { get; set; }
        public string? SecurityStamp { get; set; }
        public int AccessFailedCount { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
        public bool LockoutEnabled { get; set; }

        [NotMapped] // This ensures EF won't try to map this to database
        public IFormFile? ProfilePictureFile { get; set; }
        public List<PermissionModel> Permissions { get; set; } = new List<PermissionModel>();
        public bool RequirePasswordReset { get; set; } = false;
        public bool IsLockedOut { get; set; } = false;
        public string? Profile_Name { get; set; }
    }
    public class PermissionModel
    {
        public int MenuId { get; set; }
        public string? MenuName { get; set; }
        public string? Controller { get; set; }
        public string? Action { get; set; }
        public string? PermissionType { get; set; }
    }

    public class RoleDto
    {
        public int Id { get; set; }
        public string? RoleName { get; set; }
    }   

    public class UserSelectionModel
    {
        public long User_ID { get; set; } // Changed to long to match bigint
        public string? User_Name { get; set; }
    }
}
