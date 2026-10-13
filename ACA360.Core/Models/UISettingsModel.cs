using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class UISettingsModel
    {
        public int Id { get; set; }
        public string? Theme { get; set; }
        public string? Skin { get; set; }
        public string? HeaderType { get; set; }
        public string? NavigationLike { get; set; }
        public string? PrimaryColor { get; set; }
        public bool SemiDark { get; set; }
        public bool IsCollapsed { get; set; }
        public bool Rtl { get; set; }
        public string? ContentLayout { get; set; }
        public string? SidenavHeader { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public string? ModifiedBy { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

}

