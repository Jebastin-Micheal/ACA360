using System.Collections.Generic;

namespace ACA360.Core.Models.ViewModels
{
    public class TemplateConfigurationViewModel
    {
        public int TemplateId { get; set; }
        public string TemplateName { get; set; }
        public string Description { get; set; }

        // Group mappings by their "Target Sheet" (e.g., "Employees", "Plans")
        // Key = Sheet Name, Value = List of Mappings
        public Dictionary<string, List<TemplateColumnMap>> GroupedMappings { get; set; }
            = new Dictionary<string, List<TemplateColumnMap>>();
    }
}