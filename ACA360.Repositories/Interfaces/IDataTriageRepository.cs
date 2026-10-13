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
        /// <summary>Sets an issue's status through its file (056); false when the issue is not in that file or cannot be skipped.</summary>
        Task<bool> UpdateErrorStatusAsync(long stagingRowErrorId, string status, int fileLogId, string? changedBy, string? reason = null);
        /// <summary>A file's acknowledged issues with their reasons (script 061).</summary>
        Task<List<IssueSkipReason>> GetIssueSkipReasonsAsync(int fileLogId);
        Task<StagingEmployer?> GetStagingEmployerByIdAsync(long stagingEmployerId);
        Task<StagingPlan?> GetStagingPlanByIdAsync(long stagingPlanId);
        Task<StagingPremium?> GetStagingPremiumByIdAsync(long stagingPremiumId);
        Task<StagingDependent?> GetStagingDependentByIdAsync(long stagingDependentId);
        Task<IEnumerable<StagingRowError>> GetStagingErrorsByRowIdAsync(int fileLogId, string tableName, long rowId);
        /// <summary>Writes one value of a staging row and keeps the change (056); false when the value was the same.</summary>
        Task<bool> UpdateSingleFieldOnlyAsync(int fileLogId, string? tableName, long rowId, string columnName, string? newValue, string? changedBy);
        /// <summary>A file's corrections, automatic and manual: counts by rule and column, and one page of them (056).</summary>
        Task<(List<FileCorrectionGroup> Groups, List<FileCorrection> Page)> GetFileCorrectionsAsync(
            int fileLogId, string? source, int? ruleId, string? columnName, string? search, int pageIndex, int pageSize);
        Task RunValidationEngineAsync(int fileLogId);
        Task<ErrorCounts> GetErrorCountsAsync(int fileLogId);
    }
}
