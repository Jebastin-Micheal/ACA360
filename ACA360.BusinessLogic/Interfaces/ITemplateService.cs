using ACA360.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface ITemplateService
    {
        // Used by Controller for Dropdown
        Task<List<ImportTemplate>> GetActiveTemplatesAsync();

        // Used by Background Job for Validation
        Task<List<TemplateColumnMap>> GetColumnMapsByTemplateIdAsync(int templateId);
        Task<List<ImportTemplate>> GetAllTemplatesAsync();
        Task<ImportTemplate?> GetTemplateByIdAsync(int templateId);
        Task UpdateMappingAsync(int mapId, string newColumnName, string alternateNames);
        Task<int> CreateTemplateAsync(string name, string description);
        Task CloneMappingsAsync(int sourceTemplateId, int targetTemplateId);
    }
}