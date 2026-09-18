using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class UserShortcutItem
    {
        public int MenuId { get; set; }
        public string MenuName { get; set; }
        public string Icon { get; set; }
        public string Controller { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsFixed { get; set; }
    }
}
