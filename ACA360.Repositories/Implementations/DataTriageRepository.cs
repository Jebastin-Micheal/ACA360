using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class DataTriageRepository : IDataTriageRepository
    {
        private readonly string _connectionString;

        public DataTriageRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task<IEnumerable<TriageError>> GetErrorsForReportAsync(int fileLogId)
        {
            try
            {
                using var db = Connection;
                return await db.QueryAsync<TriageError>(
                    "sp_GetStagingErrorsForTriage",
                    new { FileLogId = fileLogId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (SqlException ex)
            {
                throw new Exception("A database error occurred while retrieving the error report.", ex);
            }
        }

        public async Task<IEnumerable<StagingRowError>> GetStagingErrorsByRowIdAsync(int fileLogId, string tableName, long rowId)
        {
            try
            {
                using var db = Connection;
                return await db.QueryAsync<StagingRowError>(
                    "sp_GetStagingErrorsByRowId",
                    new
                    {
                        FileLogId = fileLogId,
                        StagingTableName = tableName,
                        StagingRowId = rowId
                    },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to retrieve staging errors for table {tableName} at row {rowId}.", ex);
            }
        }

        // 1. New method that ONLY updates the data. The procedure accepts only a data column of a
        //    staging row in this file, and keeps the change with who made it (script 056).
        public async Task<bool> UpdateSingleFieldOnlyAsync(int fileLogId, string? tableName, long rowId, string columnName, string? newValue, string? changedBy)
        {
            try
            {
                using var db = Connection;
                return await db.ExecuteScalarAsync<bool>(
                    "sp_UpdateStagingField_NoValidation",
                    new
                    {
                        FileLogId = fileLogId,
                        StagingTableName = tableName,
                        StagingRowId = rowId,
                        ColumnName = columnName,
                        NewValue = newValue,
                        ChangedBy = changedBy
                    },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw new Exception($"Update only failed for {tableName}.{columnName}", ex);
            }
        }

        public async Task<(List<FileCorrectionGroup> Groups, List<FileCorrection> Page)> GetFileCorrectionsAsync(
            int fileLogId, string? source, int? ruleId, string? columnName, string? search, int pageIndex, int pageSize)
        {
            using var db = Connection;
            using var multi = await db.QueryMultipleAsync(
                "sp_GetFileCorrections",
                new
                {
                    FileLogId = fileLogId,
                    Source = string.IsNullOrWhiteSpace(source) ? null : source,
                    RuleId = ruleId,
                    ColumnName = string.IsNullOrWhiteSpace(columnName) ? null : columnName,
                    Search = search,
                    PageIndex = pageIndex,
                    PageSize = pageSize
                },
                commandType: CommandType.StoredProcedure);
            var groups = (await multi.ReadAsync<FileCorrectionGroup>()).ToList();
            var page = (await multi.ReadAsync<FileCorrection>()).ToList();
            return (groups, page);
        }

        // 2. Separate method to run the engine once
        public async Task RunValidationEngineAsync(int fileLogId)
        {
            try
            {
                using var db = Connection;
                await db.ExecuteAsync(
                    "sp_ExecuteValidationEngine",
                    new { FileLogId = fileLogId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to execute validation engine for file {fileLogId}.", ex);
            }
        }

        public async Task<(List<TriageError> Errors, PaginationViewEntity? PageInfo, ErrorCounts Counts)> GetTriageErrorsAsync(int fileLogId, PaginationEntity paginationEntity, string severity)
        {
            var errors = new List<TriageError>();
            ErrorCounts? counts = null;
            PaginationViewEntity? paginationMeta = null;

            try
            {
                using var db = Connection;
                using var multi = await db.QueryMultipleAsync(
                    "sp_GetStagingErrorsForTriage_with_filter",
                    new
                    {
                        FileLogId = fileLogId,
                        paginationEntity.PageIndex,
                        paginationEntity.PageSize,
                        paginationEntity.Search,
                        paginationEntity.SortColumn,
                        SortOrder = paginationEntity.SortOrder ?? "asc",
                        severity,
                        paginationEntity.SearchColumn
                    },
                    commandType: CommandType.StoredProcedure);

                errors = (await multi.ReadAsync<TriageError>()).ToList();

                var meta = await multi.ReadFirstOrDefaultAsync<PaginationMetadata>();
                if (meta != null)
                {
                    paginationMeta = new PaginationViewEntity
                    {
                        TotalItems = meta.TotalCount,
                        CurrentPage = paginationEntity.PageIndex,
                        PageSize = paginationEntity.PageSize,
                        TotalPages = (int)Math.Ceiling((double)meta.TotalCount / paginationEntity.PageSize),
                        StartPage = (paginationEntity.PageIndex - 1) * paginationEntity.PageSize + 1,
                        EndPage = Math.Min(paginationEntity.PageIndex * paginationEntity.PageSize, meta.TotalCount),
                        SortColumn = paginationEntity.SortColumn,
                        SortOrder = paginationEntity.SortOrder,
                        RecordCount = meta.TotalCount,
                        PageNumber = paginationEntity.PageIndex,
                        TotalCount = meta.TotalCount
                    };
                }

                counts = await multi.ReadFirstOrDefaultAsync<ErrorCounts>();
            }
            catch (Exception ex)
            {
                // Fixed unreachable throw and copy-pasted message here
                throw new Exception($"Critical failure in GetTriageErrorsAsync for File {fileLogId}", ex);
            }

            return (errors, paginationMeta, counts ?? new ErrorCounts());
        }

        public async Task<StagingEmployee?> GetStagingEmployeeByIdAsync(long stagingEmployeeId)
        {
            try
            {
                using var db = Connection;
                return await db.QuerySingleOrDefaultAsync<StagingEmployee>(
                    "sp_GetStagingEmployeeById",
                    new { StagingEmployeeId = stagingEmployeeId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error fetching employee with ID {stagingEmployeeId}.", ex);
            }
        }

        public async Task<bool> UpdateErrorStatusAsync(long stagingRowErrorId, string status, int fileLogId, string? changedBy, string? reason = null)
        {
            try
            {
                using var db = Connection;
                return await db.ExecuteScalarAsync<int>(
                    "sp_UpdateStagingRowErrorStatus",
                    new { StagingRowErrorId = stagingRowErrorId, Status = status, FileLogId = fileLogId, ChangedBy = changedBy, Reason = reason },
                    commandType: CommandType.StoredProcedure) == 1;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Failed to update error status to {status} for ErrorId {stagingRowErrorId}.", ex);
            }
        }

        public async Task<List<IssueSkipReason>> GetIssueSkipReasonsAsync(int fileLogId)
        {
            using var db = Connection;
            return (await db.QueryAsync<IssueSkipReason>("sp_GetIssueSkipReasons", new { FileLogId = fileLogId },
                commandType: CommandType.StoredProcedure)).ToList();
        }

        public async Task<StagingEmployer?> GetStagingEmployerByIdAsync(long stagingEmployerId)
        {
            try
            {
                using var db = Connection;
                return await db.QuerySingleOrDefaultAsync<StagingEmployer>(
                    "sp_GetStagingEmployerById",
                    new { StagingEmployerId = stagingEmployerId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error fetching employer ID {stagingEmployerId}.", ex);
            }
        }

        public async Task<StagingPlan?> GetStagingPlanByIdAsync(long stagingPlanId)
        {
            try
            {
                using var db = Connection;
                return await db.QuerySingleOrDefaultAsync<StagingPlan>(
                    "sp_GetStagingPlanById",
                    new { StagingPlanId = stagingPlanId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error fetching plan ID {stagingPlanId}.", ex);
            }
        }

        public async Task<StagingPremium?> GetStagingPremiumByIdAsync(long stagingPremiumId)
        {
            try
            {
                using var db = Connection;
                return await db.QuerySingleOrDefaultAsync<StagingPremium>(
                    "sp_GetStagingPremiumById",
                    new { StagingPremiumId = stagingPremiumId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error fetching premium ID {stagingPremiumId}.", ex);
            }
        }

        public async Task<StagingDependent?> GetStagingDependentByIdAsync(long stagingDependentId)
        {
            try
            {
                using var db = Connection;
                return await db.QuerySingleOrDefaultAsync<StagingDependent>(
                    "sp_GetStagingDependentById",
                    new { StagingDependentId = stagingDependentId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error fetching dependent ID {stagingDependentId}.", ex);
            }
        }

        public async Task<ErrorCounts> GetErrorCountsAsync(int fileLogId)
        {
            using var db = Connection;
            const string sql = @"
SELECT
    SUM(CASE WHEN vr.Severity = 'Error'          THEN 1 ELSE 0 END) AS ErrorCount,
    SUM(CASE WHEN vr.Severity = 'Warning'        THEN 1 ELSE 0 END) AS WarningCount,
    SUM(CASE WHEN vr.Severity = 'Information'    THEN 1 ELSE 0 END) AS InformationCount,
    SUM(CASE WHEN vr.Severity = 'EmployerAction' THEN 1 ELSE 0 END) AS EmployerActionCount,
    COUNT(*) AS TotalErrorsCount
FROM StagingRowErrors sre
JOIN ValidationRules vr ON sre.RuleId = vr.RuleId
WHERE sre.FileLogId = @FileLogId
  AND sre.Status != 'Skipped'";

            return await db.QueryFirstOrDefaultAsync<ErrorCounts>(sql, new { FileLogId = fileLogId })
                   ?? new ErrorCounts();
        }
    }
}