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
        Task ClearStagingDataAsync(int fileLogId);
        Task BulkLoadStagingDataAsync(string stagingProcedureName, int fileLogId, DataTable data);
        Task ExecuteValidationAsync(int fileLogId);
        Task<bool> HasValidationErrorsAsync(int fileLogId);
        Task<IEnumerable<StagingRowError>> GetErrorsByFileLogIdAsync(int fileLogId);

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
