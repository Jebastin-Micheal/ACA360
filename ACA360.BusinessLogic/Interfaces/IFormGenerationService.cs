using ACA360.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IFormGenerationService
    {
        Task<List<EmployerGenerationStatusDto>> GetGenerationDashboardAsync(
            string userId, string userRole, int year,
            string filterStatus, string sortColumn, string sortOrder,string searchTerm);

        Task<IEnumerable<dynamic>> GetEmployeesForPreviewAsync(int employerId);

        Task<Dictionary<int, (string City, string Zip)>> GetEmployeeCityAndZipAsync(List<int> employeeIds);
    }
}