using ACA360.Core.Models.Tracker;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IProcessRepository
    {
        Task<IEnumerable<TrackerCategory>> GetCategoriesAsync();
        Task<(IEnumerable<TrackerProcess> List, int TotalCount)> GetProcessListAsync(
            string search, int? categoryId, string sortCol, string sortOrder, int page, int pageSize);
        Task<TrackerProcess> GetProcessByIdAsync(int id);
        Task AddUpdateProcessAsync(TrackerProcess process);
        Task DeleteProcessAsync(int id);
    }
}