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
        /// <summary>The template's rules with the employer's remembered headers added as alternate names (script 073).</summary>
        Task<List<TemplateColumnMap>> GetColumnMapsForEmployerAsync(int templateId, string? employerEin);
        Task<List<EmployerColumnMap>> GetEmployerColumnMapsAsync(string employerEin, int? templateId = null);
        /// <summary>False when the template has no such column.</summary>
        Task<bool> SaveEmployerColumnMapAsync(string employerEin, int templateId, string sheetName, string targetColumnName, string sourceHeader, string? createdBy);
        Task<bool> DeleteEmployerColumnMapAsync(int employerColumnMapId, string employerEin);
        Task<List<ImportTemplate>> GetAllTemplatesAsync();
        Task<ImportTemplate?> GetTemplateByIdAsync(int templateId);
        Task UpdateMappingAsync(int mapId, string newColumnName, string? alternateNames);
        Task<int> CreateTemplateAsync(string name, string description);
        Task CloneMappingsAsync(int sourceTemplateId, int targetTemplateId);
    }
}