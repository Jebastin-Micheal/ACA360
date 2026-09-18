using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations.Schema;

namespace ACA360.Core.Models
{
    public class Role
    {
        public int Id { get; set; }
        public int RoleId { get; set; }
        public string? RoleName { get; set; }
        public bool IsActive { get; set; }
        public string? Description { get; set; }
        public DateTime? DateModified { get; set; }
    }

    
    public class RoleFormDataDto
    {
        public Role? Role { get; set; }
        public List<int> SelectedActionIds { get; set; } = new List<int>();  
       
    }   

}
