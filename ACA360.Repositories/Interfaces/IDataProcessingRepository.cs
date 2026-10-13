using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IDataProcessingRepository
    {
        IAsyncEnumerable<IDictionary<string, object?>> ReadImportAnalysisExportRowsAsync(int fileLogId, string entity, string importMode, System.Threading.CancellationToken cancellationToken = default);
        Task<ImportAnalysisPage> GetImportAnalysisRowsAsync(ImportAnalysisRequest request, System.Threading.CancellationToken cancellationToken = default);
        Task<ImportAnalysisSummary> GetImportAnalysisSummaryAsync(int fileLogId, string importMode, System.Threading.CancellationToken cancellationToken = default);
        Task<ImportChanges> GetImportChangesAsync(int fileLogId, string? kind, string? search, int pageIndex, int? pageSize, System.Threading.CancellationToken cancellationToken = default);
        Task ClearStagingDataAsync(int fileLogId);
        Task BulkLoadStagingDataAsync(string stagingProcedureName, int fileLogId, DataTable data);
        Task ExecuteValidationAsync(int fileLogId);

        // F-24: GetInvalidStagingEmployeesAsync, UpdateStagingEmployeeAsync,
        // GetInvalidStagingEmployersAsync and UpdateStagingEmployerAsync were
        // removed. All four called stored procedures that do not exist in the
        // database, so any use would have thrown at runtime — and none had a
        // caller outside this interface and its implementation. The live triage
        // path goes through IDataTriageRepository instead.

        Task<ImportValidationSummary?> GetImportSummaryAsync(int fileLogId);
        Task<ImportResult> ImportDataAsync(int fileLogId, string importMode);
        // NEW: Batch Loading Method
        Task BulkLoadBatchAsync(string procedureName, int fileLogId, DataTable batchData);
        Task<List<string>> GetTvpColumnsAsync(string typeName);
        Task<int> RecoverStuckImportsAsync(int staleMinutes);
    }
}
