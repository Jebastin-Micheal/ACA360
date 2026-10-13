using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class AcaTrackingViewModel
    {
        public int TotalEmployees { get; set; }
        public int ElectronicCount { get; set; }
        public int PaperCount { get; set; }
        public int NoPreferenceCount { get; set; }

        public double ElectronicPercent => TotalEmployees == 0 ? 0 : Math.Round(ElectronicCount * 100.0 / TotalEmployees, 1);
        public double PaperPercent => TotalEmployees == 0 ? 0 : Math.Round(PaperCount * 100.0 / TotalEmployees, 1);
        public double NoPreferencePercent => TotalEmployees == 0 ? 0 : Math.Round(NoPreferenceCount * 100.0 / TotalEmployees, 1);

        public List<ConsentByLocation> LocationBreakdown { get; set; } = new();
        public List<ConsentTrendPoint> Trend { get; set; } = new();
        public List<RecentConsentActivity> RecentActivity { get; set; } = new();
    }

}
