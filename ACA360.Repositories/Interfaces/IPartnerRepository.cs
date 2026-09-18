using ACA360.Core.Models;
using ACA360.Core.Models.Tracker;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IPartnerRepository
    {
        // Firms
        Task<(IEnumerable<FirmItem> List, int TotalCount)> GetFirmsAsync(string search, string sortCol, string sortOrder, int page, int pageSize);
        Task<FirmItem> GetFirmByIdAsync(int id);
        Task<int> SaveFirmAsync(FirmItem firm);
        Task DeleteFirmAsync(int id);
        Task<IEnumerable<dynamic>> GetFirmDropdownAsync(); // For Broker Modal

        // Brokers
        Task<(IEnumerable<BrokerItem> List, int TotalCount)> GetBrokersAsync(string search, int? firmId, string sortCol, string sortOrder, int page, int pageSize);
        Task<BrokerItem> GetBrokerByIdAsync(int id);
        Task<int> SaveBrokerAsync(BrokerItem broker);
        Task DeleteBrokerAsync(int id);
        Task<IEnumerable<StateModel>> GetAllStatesAsync();
    }
}