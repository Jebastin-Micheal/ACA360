using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public sealed class ApplicationInfo
    {
        public const string SectionName = "ApplicationInfo";

        public string ApplicationName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string BuildNumber { get; set; } = string.Empty;
        public string ReleaseDate { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string MaintenanceMessage { get; set; } = string.Empty;
    }
}
