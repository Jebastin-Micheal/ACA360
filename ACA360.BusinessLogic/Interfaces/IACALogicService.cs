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
        /// <summary>"Why this code?": each month's codes and the reasons for them (script 058).</summary>
        Task<CodeExplanation?> GetCodeExplanationAsync(int employeeId, int year);
        /// <summary>This year's and last year's figures for an employer (last year null when it has none). Script 064.</summary>
        Task<(EmployerYearFigures ThisYear, EmployerYearFigures? LastYear)> GetYearOverYearAsync(int employerId, int year);
        /// <summary>The employers among these whose estimated penalty grew most since last year. Script 064.</summary>
        Task<List<ExposureGrowthRow>> GetExposureGrowthAsync(int year, IEnumerable<int> employerIds, int top = 5);
        /// <summary>Which line 16 safe harbor covers the most months, per employee and overall. Advice only (script 071).</summary>
        Task<SafeHarborAdvice> GetSafeHarborAdviceAsync(int employerId, int year);
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
        Task ApplySafeHarborAsync(int employeeId, string filingYear, string safeHarborCode, bool isLocked, string userId);
        Task<List<EmployeeCodeAuditTimelineDto>> GetAuditTimelineAsync(int employeeId, int year);
        //Task GenerateFlagsAsync(int fileLogId);
        Task RecalculateFlagsAsync(int employerId, int year);
        Task<byte[]> GenerateAffordabilityExportAsync(int employerId, int year);
        Task RecalculateFlagsForEmployeeAsync(int year, int employeeId);
    }
}