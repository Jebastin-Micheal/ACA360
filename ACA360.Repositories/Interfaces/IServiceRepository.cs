using ACA360.Core.Models.Tracker;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IServiceRepository
    {
        Task<(IEnumerable<ServiceItem> List, int TotalCount)> GetServiceListAsync(
       string search, string sortCol, string sortOrder, int page, int pageSize);
        Task<ServiceItem> GetServiceByIdAsync(int id);
        Task AddUpdateServiceAsync(ServiceItem service);
        Task DeleteServiceAsync(int id);
    }
}