using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IPdfService
    {
        // --- EMPLOYEE FORMS (1095) ---
        Task<byte[]> Generate1095CForEmployeeAsync(int employeeId, int year, bool suppressSSN = false);
        Task<byte[]> Generate1095BForEmployeeAsync(int employeeId, int year, bool suppressSSN = false);

        // --- TRANSMITTAL FORMS (1094) ---
        // These are generated once per Employer
        Task<byte[]> Generate1094CForEmployerAsync(int employerId, int year);
        Task<byte[]> Generate1094BForEmployerAsync(int employerId, int year);


        // --- BATCH PROCESS ---
        // Generates all 1095s + the specific 1094, zips them, saves, and notifies
        Task ProcessBatchAndNotifyAsync(int employerId, int year, string userId, string formType);

        // In IPdfService.cs
        Task<string> GenerateBatchPdfAsync(int employerId, List<int> employeeIds, int year, string formType = null);

        Task ProcessEmployeeBatchAsync(
    int employerId,
    List<int> employeeIds,
    int year,
    string userId,
    string formType);

        Task GenerateMultiEmployerBatchAsync(List<int> employerIds, List<int> employeeIds, int year, string userId, string formType, bool suppressSSN = false);

        Task<byte[]> GenerateEmployeesPreviewAsync(List<int> employeeIds, List<int> employerIds, int year, string formType, bool suppressSSN = false);


    }
}