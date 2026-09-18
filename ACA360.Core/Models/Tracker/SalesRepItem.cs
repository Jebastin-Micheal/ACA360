using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models.Tracker
{
    public class SalesRepItem
    {
        [Key]
        public int SalesRepId { get; set; }

        [Required(ErrorMessage = "Sales Rep Name is required.")]
        [StringLength(150, ErrorMessage = "Name cannot exceed 150 characters.")]
        [Display(Name = "Sales Rep Name")]
        public string SalesRepName { get; set; }
        public bool IsActive { get; set; }
        public int TotalCount { get; set; }
    }
}