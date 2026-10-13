using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class FaqCategoryModel
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Icon { get; set; } // New Property
    }
    public class FaqModel
    {
        public string? Id { get; set; }
        public string CategoryId { get; set; } = null!; // The new Dropdown selected value
        public string Category { get; set; } = null!; // New field
        public string CategoryIcon { get; set; } = null!; // New Property for UI Display
        public string Question { get; set; } = null!;
        public string Answer { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime? CreatedDate { get; set; }
    }
    public class ContactSettingsViewModel
    {
        public string FAQ_SupportPhone { get; set; } = null!;
        public string FAQ_SupportEmail { get; set; } = null!;
    }
}
