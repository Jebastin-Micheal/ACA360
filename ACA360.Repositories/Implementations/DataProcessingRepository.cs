using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class DataProcessingRepository : IDataProcessingRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<DataProcessingRepository> _logger;
      //  private readonly IConfiguration _configuration;
        /// <summary>
        /// Purpose: Initializes the DataProcessingRepository with required dependencies.
        /// Input parameters: string connectionString, ILogger logger
        /// Output/return value: None
        /// </summary>
        public DataProcessingRepository(string connectionString, ILogger<DataProcessingRepository> logger)
        {
            try
            {
                _connectionString = connectionString;
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "Error occurred during initialization of DataProcessingRepository.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Clears previously loaded staging data for a given file log ID.
        /// Input parameters: int fileLogId
        /// Output/return value: Task
        /// </summary>
        public async Task ClearStagingDataAsync(int fileLogId)
        {
            try
            {
                using var db = Connection;
                await db.ExecuteAsync(
                    "sp_ClearStagingData",
                    new { FileLogId = fileLogId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in ClearStagingDataAsync for FileLogId: {FileLogId}", fileLogId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Loads batches of staging data into the database using Table-Valued Parameters.
        /// Input parameters: string procedureName, int fileLogId, DataTable data
        /// Output/return value: Task
        /// </summary>
        public async Task BulkLoadStagingDataAsync(string procedureName, int fileLogId, DataTable data)
        {
            try
            {
                string typeName = "";
                switch (procedureName)
                {
                    case "sp_LoadStagingEmployers":
                        typeName = "dbo.ut_Staging_Employers";
                        break;
                    case "sp_LoadStagingPlans":
                        typeName = "dbo.ut_Staging_Plans";
                        break;
                    case "sp_LoadStagingPremiums":
                        typeName = "dbo.ut_Staging_Premiums";
                        break;
                    case "sp_LoadStagingEmployees":
                        typeName = "dbo.ut_Staging_Employee";
                        break;
                    case "sp_LoadStagingDependents":
                        typeName = "dbo.ut_Staging_Dependents";
                        break;
                    default:
                        throw new ArgumentException("Unknown procedure name provided for bulk load.", nameof(procedureName));
                }

                using var db = Connection;
                await db.ExecuteAsync(procedureName,
                    new { FileLogId = fileLogId, Data = data.AsTableValuedParameter(typeName) },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 120);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in BulkLoadStagingDataAsync for Procedure: {ProcedureName}, FileLogId: {FileLogId}", procedureName, fileLogId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Executes the business rules validation engine against the staging data.
        /// Input parameters: int fileLogId
        /// Output/return value: Task
        /// </summary>
        public async Task ExecuteValidationAsync(int fileLogId)
        {
            try
            {
                using var db = Connection;
                await db.ExecuteAsync(
                    "sp_ExecuteValidationEngine",
                    new { FileLogId = fileLogId },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 3600);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in ExecuteValidationAsync for FileLogId: {FileLogId}", fileLogId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Checks if the rule engine flagged any errors for the uploaded file.
        /// Input parameters: int fileLogId
        /// Output/return value: Task of bool
        /// </summary>
        public async Task<bool> HasValidationErrorsAsync(int fileLogId)
        {
            try
            {
                using var db = Connection;
                var errorCount = await db.QuerySingleAsync<int>(
                    "sp_CheckForRuleEngineErrors",
                    new { FileLogId = fileLogId },
                    commandType: CommandType.StoredProcedure);
                return errorCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in HasValidationErrorsAsync for FileLogId: {FileLogId}", fileLogId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves all specific staging row errors identified for a file.
        /// Input parameters: int fileLogId
        /// Output/return value: Task of IEnumerable of StagingRowError
        /// </summary>
        public async Task<IEnumerable<StagingRowError>> GetErrorsByFileLogIdAsync(int fileLogId)
        {
            try
            {
                using var db = Connection;
                return await db.QueryAsync<StagingRowError>(
                    "sp_GetStagingErrorsByFileLogId",
                    new { FileLogId = fileLogId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetErrorsByFileLogIdAsync for FileLogId: {FileLogId}", fileLogId);
                throw;
            }
        }
        /// <summary>
        /// Releases files stranded at WS.Importing. Returns how many were released.
        /// </summary>
        public async Task<int> RecoverStuckImportsAsync(int staleMinutes)
        {
            using var db = Connection;
            return await db.ExecuteScalarAsync<int>(
                "sp_RecoverStuckImports",
                new { StaleMinutes = staleMinutes },
                commandType: CommandType.StoredProcedure);
        }
        /* F-24: GetInvalidStagingEmployeesAsync, UpdateStagingEmployeeAsync,
           GetInvalidStagingEmployersAsync and UpdateStagingEmployerAsync were
           removed here. Each called a stored procedure that does not exist —
           sp_GetInvalidStagingEmployees, sp_UpdateStagingEmployeeRow,
           sp_GetInvalidStagingEmployers, sp_UpdateStagingEmployerRow — so any
           call would have thrown "Could not find stored procedure". None had a
           caller outside this class and its interface; the live triage path uses
           IDataTriageRepository. Deleting them is the honest fix: creating four
           procedures to satisfy code nothing invokes would only add dead SQL. */

        /// <summary>
        /// Purpose: Processes staging data into core live tables (Simple Import).
        /// Input parameters: int fileLogId
        /// Output/return value: Task
        /// </summary>
        //public async Task ImportDataAsync(int fileLogId)
        //{
        //    try
        //    {
        //        using var db = Connection;
        //        await db.ExecuteAsync(
        //            "sp_ProcessEmployerData",
        //            new { FileLogId = fileLogId },
        //            commandType: CommandType.StoredProcedure,
        //            commandTimeout: 1200); // ⏱️ FIX: Added 20-minute timeout
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error occurred in ImportDataAsync for FileLogId: {FileLogId}", fileLogId);
        //        throw;
        //    }
        //}

        /// <summary>
        /// Purpose: Processes staging data into core tables based on the selected import mode.
        /// Input parameters: int fileLogId, string importMode
        /// Output/return value: Task of ImportResult
        /// </summary>
        public async Task<ImportResult> ImportDataAsync(int fileLogId, string importMode)
        {
            try
            {
                using var db = Connection;

                // Execute the Import Stored Procedure
                await db.ExecuteAsync(
                    "sp_ProcessEmployerData",
                    new
                    {
                        FileLogId = fileLogId,
                        ImportMode = importMode
                    },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 1200); // ⏱️ FIX: Added 20-minute timeout

                // Fetch the summary generated by the SP
                var summary = await GetImportSummaryAsync(fileLogId);

                if (summary != null && summary.IsImportComplete)
                {
                    return new ImportResult
                    {
                        Success = true,
                        Message = "Import successful.",
                        Data = summary
                    };
                }
                else
                {
                    return new ImportResult
                    {
                        Success = false,
                        Message = summary?.ValidationMessage ?? "Import failed with unknown error.",
                        Data = summary
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in ImportDataAsync(mode) for FileLogId: {FileLogId}", fileLogId);
                return new ImportResult { Success = false, Message = "System Error: " + ex.Message };
            }
        }

        /// <summary>
        /// Purpose: Fetches the validation summary associated with an import process.
        /// Input parameters: int fileLogId
        /// Output/return value: Task of ImportValidationSummary
        /// </summary>
        public async Task<ImportValidationSummary?> GetImportSummaryAsync(int fileLogId)
        {
            try
            {
                using var db = Connection;
                return await db.QueryFirstOrDefaultAsync<ImportValidationSummary>(
                    "sp_GetImportValidationSummary",
                    new { FileLogId = fileLogId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetImportSummaryAsync for FileLogId: {FileLogId}", fileLogId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Wrapper for BulkLoadStagingDataAsync for batching contexts.
        /// Input parameters: string procedureName, int fileLogId, DataTable batchData
        /// Output/return value: Task
        /// </summary>
        public async Task BulkLoadBatchAsync(string procedureName, int fileLogId, DataTable batchData)
        {
            try
            {
                await BulkLoadStagingDataAsync(procedureName, fileLogId, batchData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in BulkLoadBatchAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves the defined columns of a SQL Server User-Defined Table Type.
        /// Input parameters: string typeName
        /// Output/return value: Task of List of string
        /// </summary>
        public async Task<List<string>> GetTvpColumnsAsync(string typeName)
        {
            try
            {
                using var db = Connection;

                var cleanName = typeName.Contains(".") ? typeName.Split('.')[1] : typeName;

                var cols = await db.QueryAsync<string>(
                    "sp_GetTvpColumns",
                    new { TypeName = cleanName },
                    commandType: CommandType.StoredProcedure);

                return cols.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetTvpColumnsAsync for TypeName: {TypeName}", typeName);
                throw;
            }
        }
    }
}