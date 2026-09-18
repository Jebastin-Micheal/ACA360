using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IAssignmentService
    {
        Task AssignEmployersToAccountManagerAsync(int amId, List<int> employerIds, int assignedByAdminId);
        Task AssignEmployersToDataAnalystAsync(int analystId, List<int> employerIds, int assignedByAdminId);
        Task<DataAnalyst?> GetDataAnalystByIdAsync(int id);
        Task<List<Employer>> GetEmployersByDataAnalystIdAsync(int analystId,int roleId);
        Task<AccountManager?> GetAccountManagerByIdAsync(int id);
        Task<List<Employer>> GetEmployersByAccountManagerIdAsync(int amId, int roleId);
        Task<AccountManager?> GetBrokerByIdAsync(int id);
        Task<List<Employer>> GetEmployersByBrokerIdAsync(int amId, int roleId);

        Task AssignEmployersToBrokerAsync(int analystId, List<int> employerIds, int assignedByAdminId);
        Task<EmployerAssignmentHistoryModel?> GetEmployerAssignmentHistoryAsync(int employerId);
        Task<List<AssignmentConflictItem>> GetAssignmentConflictsAsync(List<int> employerIds, int staffId, string side);

    }
}
