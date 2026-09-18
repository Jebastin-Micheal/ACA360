using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IEmployerService
    {
        Task<IEnumerable<Employer>> GetEmployersForUserAsync(string userId, string roleName);
        Task<(List<Employer> Employers, PaginationViewEntity PageRequest)> GetEmployersAsync(string empId, PaginationEntity paginationEntity, string searchFields);
       
        Task<List<Employer>> SearchEmployersAsync(string searchTerm, int pageSize, int filingYear);
        Task<Employer?> GetEmployerByIdAsync(string id);

        Task<(List<Employer> employerListEntity, PaginationMetadata pagination)> GetEmployersListViewAsync(PaginationEntity pagination, string userId = "", string roleName = "");
        Task<Employer?> GetEmployerByEinAsync(string ein);
        Task<Employer?> GetEmployerDetailsByIdAsync(string employerId);
        Task<string?> GetAssignedDataAnalystForEmployerAsync(int employerId);

       
        Task<Employer?> GetCommonEmployerDetailsAsync(string employer_Id, string filingYear);

        // Split-screen filing-year comparison: resolve the same business (by EIN) in
        // another filing year and return its common details (null if none for that year).
        Task<Employer?> GetEmployerDetailsForCompareAsync(string currentEmployerId, string filingYear);

        Task<EmployerInfoModel?> GetEmployerInfoAsync(string employerId);
        Task<EmployerDropdownDataModel> GetEmployerDropdownDataAsync(string? employerId, int? firmId, int? brokerId, bool IsBilling);

        // for Service Info tab
        Task<List<ServiceListItem>> GetServiceList(string employerId, string planYear);
        Task<List<ServiceListDropdown>> GetServiceDropdownList();
        Task<ServiceDetail> GetServiceDetail(string employerId, string planYear, string serviceId);
        Task<List<AuditUser>> GetAuditByDropdownAsync();
        Task<List<Process_steps>> GetProcessesAsync(string category);
        Task<List<Process_steps>> GetWebProcessesAsync(string category);
        //Task<List<ImplementationProcess>> Get1094processByDropdownAsync();
        //Task<List<ProcessStep1094>> GetFTEProcessByDropdownAsync();
        //Task<List<StateFilingProcessStep>> GetStaeFilingprocessDropdownAsync();
        Task<Employer> GetEmployer1094DetailsAsync(string employerId);

        Task<List<AuditLog>> GetEmployerAuditLogAsync(int employerId);

        Task<EmployerImportantInfo> GetEmployerImportantInfoAsync(string employerId);
        // =============================================
        // AFFILIATE
        // =============================================
        Task<int> AddAffiliateAsync(AffiliateDto affiliate);
        Task SaveAffiliateAsync(AffiliateDto affiliate);
        Task<AffiliateDto?> GetAffiliateByIdAsync(int id);
        Task<bool> DeleteAffiliateAsync(int id);
        Task<AffiliateDto?> GetPrimaryEmployerDataAsync(int employerId);
        Task<int> AddContactAsync(ContactDto contact);
        Task<int?> GetPrimaryEmployerIdAsync(string employerId);
        Task<List<Employer>> GetEmployerFamilyAsync(string employerId);
        Task<(int EmployerId, int TrackerEmployerId)> AddEmployerCommonAsync(Employer employer, string? companyIdFromSession);

        //Task<int> AddEmployerInfo_TrackerAsync(EmployerInfoModel employer);
        Task<int> AddEmployerInfo_TrackerAsync(EmployerInfoModel employer, int currentUserId = 0);
        Task<int> SaveEmployer1094DetailsAsync(Employer employer); 
        Task<bool> SaveEmployerImportantInfoAsync(EmployerImportantInfo employer); 
        
      Task<int> AddEmployerServiceAsync(ServiceDetail service);
      Task<bool> UpdateEmployerCommonAsync(Employer employer);
        Task<int> GetFollowUpDaysAsync(string processName, int categoryId);
        Task<IEnumerable<ProcessHistoryDto>> GetProcessHistoryAsync(int employerServiceId, string categoryName);
        // Task<bool> UpdateEmployerServiceAsync(ServiceDetail service);
        Task<(bool Success, string Message, DateTime? NewFollowUpDate)> UpdateEmployerServiceAsync(ServiceDetail service, string updatedByUserId);
        Task<List<AmClientPortfolioDto>> GetAmClientPortfolioAsync(string amUserId);
        Task<List<AmClientPortfolioDto>> GetBrokerClientPortfolioAsync(string brokerUserId);
        Task<List<EmployerAggregatedMember>> GetAggregatedMembersAsync(int employerId);
        Task<Employer1094BViewModel> GetEmployer1094BDetailsAsync(string employerId);
        Task<List<Employer>> GetAllEmployersAsync(string userId);
        Task<bool> DeleteEmployerAsync(int employerId);
        Task<Employer360ViewModel> GetEmployer360Async(int employerId, int year);
        Task<string?> GetEmployerNameAsync(int employerId);
        Task<string?> GetEmployerIdForYearAsync(string currentEmployerId, string filingYear);
    }
}
