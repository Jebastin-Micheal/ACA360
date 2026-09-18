using ACA360.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IACALogicService
    {
        /// <summary>
        /// The Main Engine: Calculates and saves 1095-C codes for an entire employer for a specific year.
        /// </summary>
        Task GenerateCodesForEmployerAsync(int employerId, int year);

        /// <summary>
        /// Retrieves the calculated codes (for the PDF generator or Dashboard).
        /// </summary>
        Task<EmployeeCode?> GetCodesForEmployeeAsync(int employeeId, int year);
        // Add this method
        Task<List<EmployeeCodeReviewDto>> GetEmployeeCodesListAsync(int employerId, int year);
        Task UpdateManualCodesAsync(EmployeeCode updated, string userId);
        Task<List<CoveredIndividualModel>> GetCoveredIndividualsAsync(int employeeId, int year);
        // 1. Risk Calculator
        Task<PenaltyRiskDto> CalculatePenaltyRiskAsync(int employerId, int year);
        Task<PenaltyRiskDto> GetPenaltyRiskAsync(int employerId, int year);
        // 2. Auto-Fixer
        Task<int> AutoFixPlanStartMonthAsync(int employerId, int year, string startMonth);
        // Add this new method signature
        Task<List<AtRiskEmployeeDto>> GetAtRiskEmployeesAsync(int employerId, int year);
        Task<decimal> GetPortfolioRiskAsync(string amUserId, int year);
        Task ApplySafeHarborAsync(int employeeId, string filingYear, string safeHarborCode, bool isLocked, string userId);
        Task<List<EmployeeCodeAuditTimelineDto>> GetAuditTimelineAsync(int employeeId, int year);
        //Task GenerateFlagsAsync(int fileLogId);
        Task RecalculateFlagsAsync(int employerId, int year);
        Task<byte[]> GenerateAffordabilityExportAsync(int employerId, int year);
        Task RecalculateFlagsForEmployeeAsync(int year, int employeeId);
    }
}