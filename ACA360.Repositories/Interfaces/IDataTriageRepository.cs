using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IDataTriageRepository // Ensure this is public
    {
        Task<IEnumerable<TriageError>> GetErrorsForReportAsync(int fileLogId);
        Task<(List<TriageError> Errors, PaginationViewEntity? PageInfo, ErrorCounts Counts)> GetTriageErrorsAsync(int fileLogId, PaginationEntity paginationEntity, string severity);
        Task<StagingEmployee?> GetStagingEmployeeByIdAsync(long stagingEmployeeId);
        Task<IEnumerable<ErrorSummary>> GetErrorSummaryAsync(int fileLogId);
        Task UpdateErrorStatusAsync(long stagingRowErrorId, string status);
        Task UpdateAndRevalidateSingleFieldAsync(UpdateFieldPayload payload);
        Task<IEnumerable<TriageError>> GetTriageErrorsByRowAsync(int fileLogId, string tableName, long rowId);
        Task<StagingEmployer?> GetStagingEmployerByIdAsync(long stagingEmployerId);
        Task<StagingPlan?> GetStagingPlanByIdAsync(long stagingPlanId);
        Task<StagingPremium?> GetStagingPremiumByIdAsync(long stagingPremiumId);
        Task<StagingDependent?> GetStagingDependentByIdAsync(long stagingDependentId);
        Task<IEnumerable<StagingRowError>> GetStagingErrorsByRowIdAsync(int fileLogId, string tableName, long rowId);
        Task UpdateAndRevalidateSingleFieldAsync(int fileLogId, string tableName, long rowId, string columnName, string newValue);
        Task UpdateSingleFieldOnlyAsync(int fileLogId, string tableName, long rowId, string columnName, string newValue);
        Task RunValidationEngineAsync(int fileLogId);
        Task<ErrorCounts> GetErrorCountsAsync(int fileLogId);
    }
}
