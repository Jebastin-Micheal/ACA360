using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class ConsentByLocation
    {
        public string WorkLocation { get; set; }
        public string StateCode { get; set; }
        public int TotalEmployees { get; set; }
        public int ElectronicCount { get; set; }
        public int PaperCount { get; set; }
        public int NoPreferenceCount { get; set; }
        public decimal ConsentRate { get; set; }
    }
}
