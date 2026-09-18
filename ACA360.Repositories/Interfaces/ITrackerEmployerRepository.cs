using ACA360.Core.Models.Tracker;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface ITrackerEmployerRepository
    {
        Task<(IEnumerable<TrackerEmployerItem>, int)> GetListAsync(EmployerFilterModel filter);
        Task<TrackerEmployerItem> GetByIdAsync(int id);
        //Task SaveAsync(TrackerEmployerItem item);
        Task<int> SaveAsync(TrackerEmployerItem item, int currentUserId = 0);
        Task DeleteAsync(int id);
        // -- Portal Login (tbl_User, Role_ID 13 = Employer, Ref_ID = employer id) --
        Task<(long UserId, string UserName)?> GetPortalUserByEmployerAsync(int employerId);
        Task SavePortalLoginAsync(int employerId, string userName, string? plainPassword, long existingUserId);
        // Services (Right Pane)
        Task<IEnumerable<EmployerServiceItem>> GetServicesAsync(int employerId, string planYear);
        Task<IEnumerable<EmployerServiceItem>> GetServicesGridAsync(int employerId, string planYear);
        Task<int> SaveServiceAsync(EmployerServiceItem item, string planyear);
        Task<IEnumerable<dynamic>> GetServiceDropdownAsync();
        Task<EmployerServiceItem> GetServiceByIdAsync(int id, string planyear);
        Task DeleteServiceAsync(int id);

        // Affiliates — uses [Employer] table (companyId links affiliates to primary)
        Task<IEnumerable<AffiliateEmployerItem>> GetAffiliatesAsync(int employerId);
        Task<AffiliateEmployerItem> GetAffiliateByIdAsync(int id);
        Task SaveAffiliateAsync(AffiliateEmployerItem item);
        Task<bool> DeleteAffiliateAsync(int id);
        // Notes — tbl_Medtracker_Note
        Task<IEnumerable<EmployerNoteItem>> GetNotesAsync(int employerId);
        Task<int> SaveNoteAsync(EmployerNoteItem item, int currentUserId);
        Task DeleteNoteAsync(int noteId, int employerId);
        //Dashboard Dropdown
        Task<EmployerDropdownDataModel> GetDropdownDataAsync(int employerId, int? brokerId = null, int? firmId = null);
        Task<dynamic> GetContactByIdAsync(int id);
        Task DeleteContactAsync(int id);
        Task<dynamic> GetBrokerContactByIdAsync(int id);
        Task DeleteBrokerContactAsync(int id);
        Task<dynamic> GetFirmByIdAsync(int id);
        Task<dynamic> GetBrokerByIdAsync(int id);
        Task<int> SaveContactAsync(TrackerContactDto dto);
        Task<int> SaveBrokerContactAsync(dynamic item);
        Task<int> SaveLookupAsync(string type, string name);

        //ReceiptId
        Task<IEnumerable<ReceiptIdItem>> GetReceiptIdsAsync(int employerServiceId, int employerId = 0);
        Task<int> SaveReceiptIdAsync(ReceiptIdItem item, int userId);
        Task DeleteReceiptIdAsync(int receiptEntryId);

        Task<IEnumerable<ProcessHistoryDto>> GetProcessHistoryAsync(int employerServiceId, int categoryId);

        // Export
        Task<IEnumerable<MasterExportModel>> GetMasterExportDataAsync(string planYear, int? employerId = null);


        Task<IEnumerable<AffiliateEmployerItem>> GetMainEmployersForDropdownAsync();
        Task<IEnumerable<AffiliateEmployerItem>> GetAffiliatesForDropdownAsync(int mainEmployerId);
    }
}