using System.Threading;
using System.Text.Json;
using System.Linq;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class FlagRepository : IFlagRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<FlagRepository> _logger;

        /// <summary>
        /// Purpose: Initializes the FlagRepository with configuration and logging.
        /// Input parameters: IConfiguration configuration, ILogger logger
        /// Output/return value: None
        /// </summary>
        public FlagRepository(IConfiguration configuration, ILogger<FlagRepository> logger)
        {
            try
            {
                _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing from configuration."); ;
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "Error occurred during FlagRepository initialization.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves all flag definitions from the database.
        /// Input parameters: None
        /// Output/return value: Task of IEnumerable of FlagDefinition
        /// </summary>
        public async Task<IEnumerable<FlagDefinition>> GetAllFlagsAsync()
        {
            try
            {
                using var db = Connection;
                return await db.QueryAsync<FlagDefinition>(
                    "sp_GetAllFlags",
                    commandType: CommandType.StoredProcedure
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetAllFlagsAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a specific flag definition by its integer ID.
        /// Input parameters: int flagId
        /// Output/return value: Task of FlagDefinition
        /// </summary>
        public async Task<FlagDefinition> GetFlagByIdAsync(int flagId)
        {
            try
            {
                using var db = Connection;
                var parameters = new DynamicParameters();
                parameters.Add("@FlagId", flagId);

                return await db.QueryFirstOrDefaultAsync<FlagDefinition>(
                    "sp_GetFlagById",
                    parameters,
                    commandType: CommandType.StoredProcedure
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetFlagByIdAsync for FlagId: {FlagId}", flagId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a specific flag definition by its string code.
        /// Input parameters: string flagCode
        /// Output/return value: Task of FlagDefinition
        /// </summary>
        public async Task<FlagDefinition> GetFlagByCodeAsync(string flagCode)
        {
            try
            {
                using var db = Connection;
                var parameters = new DynamicParameters();
                parameters.Add("@FlagCode", flagCode);

                return await db.QueryFirstOrDefaultAsync<FlagDefinition>(
                    "sp_GetFlagByCode",
                    parameters,
                    commandType: CommandType.StoredProcedure
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetFlagByCodeAsync for FlagCode: {FlagCode}", flagCode);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Saves (Inserts or Updates) a flag definition into the database.
        /// Input parameters: FlagDefinition flag
        /// Output/return value: Task of int (Returns the saved FlagId)
        /// </summary>
        public async Task<int> SaveFlagAsync(FlagDefinition flag)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var parameters = new DynamicParameters();

                // Input parameters
                parameters.Add("@FlagId", flag.FlagId);
                parameters.Add("@FlagCode", flag.FlagCode);
                parameters.Add("@FlagName", flag.FlagName);
                parameters.Add("@Description", flag.Description);
                parameters.Add("@Category", flag.Category);
                parameters.Add("@Severity", flag.Severity);
                parameters.Add("@LogicType", flag.LogicType);
                parameters.Add("@TargetTable", flag.TargetTable);
                parameters.Add("@TargetColumn", flag.TargetColumn);
                parameters.Add("@Operator", flag.Operator);
                parameters.Add("@ComparisonValue", flag.ComparisonValue);
                parameters.Add("@Connector", flag.Connector);
                parameters.Add("@SecondaryColumn", flag.SecondaryColumn);
                parameters.Add("@SecondaryOperator", flag.SecondaryOperator);
                parameters.Add("@SecondaryValue", flag.SecondaryValue);
                parameters.Add("@CustomSqlPredicate", flag.CustomSqlPredicate);
                parameters.Add("@RemediationSuggestion", flag.RemediationSuggestion);
                parameters.Add("@IsActive", flag.IsActive);
                parameters.Add("@IsSystemFlag", flag.IsSystemFlag);
                parameters.Add("@CreatedBy", flag.CreatedBy);
                // TODO: [FLAG-FIX DB] Fix-button routing (sp_SaveFlag must accept these params)
                parameters.Add("@TargetTab", flag.TargetTab);
                parameters.Add("@TargetField", flag.TargetField);

                // Output parameter
                parameters.Add("@NewFlagId", dbType: DbType.Int32, direction: ParameterDirection.Output);

                await db.ExecuteAsync(
                    "sp_SaveFlag",
                    parameters,
                    transaction: tx,
                    commandType: CommandType.StoredProcedure
                );

                tx.Commit();

                return parameters.Get<int>("@NewFlagId") > 0 ? parameters.Get<int>("@NewFlagId") : flag.FlagId;
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in SaveFlagAsync for FlagCode: {FlagCode}", flag?.FlagCode);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Deletes a specific flag definition from the database.
        /// Input parameters: int flagId
        /// Output/return value: Task
        /// </summary>
        public async Task DeleteFlagAsync(int flagId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var parameters = new DynamicParameters();
                parameters.Add("@FlagId", flagId);

                await db.ExecuteAsync(
                    "sp_DeleteFlag",
                    parameters,
                    transaction: tx,
                    commandType: CommandType.StoredProcedure
                );

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in DeleteFlagAsync for FlagId: {FlagId}", flagId);
                throw;
            }
        }

        /// </summary>
        public async Task<(IEnumerable<FlagDefinition> List, int TotalCount)> GetFlagListAsync(
            string search, string sortCol, string sortOrder, int page, int pageSize)
        {
            try
            {
                using var db = Connection;
                var parameters = new DynamicParameters();
                parameters.Add("@SearchText", search);
                parameters.Add("@SortColumn", sortCol);
                parameters.Add("@SortOrder", sortOrder);
                parameters.Add("@PageNumber", page);
                parameters.Add("@PageSize", pageSize);

                // The SP returns FlagDefinition columns plus an extra TotalCount column.
                // Query as dynamic rows so Dapper doesn't fail trying to bind TotalCount
                // onto FlagDefinition (which has no such property), then map manually.
                var rows = (await db.QueryAsync(
                    "sp_GetFlagList",
                    parameters,
                    commandType: CommandType.StoredProcedure
                )).AsList();

                int totalCount = rows.Count > 0 ? (int)rows[0].TotalCount : 0;

                var list = new List<FlagDefinition>();
                foreach (var row in rows)
                {
                    list.Add(new FlagDefinition
                    {
                        FlagId = row.FlagId,
                        FlagCode = row.FlagCode,
                        FlagName = row.FlagName,
                        Description = row.Description,
                        Category = row.Category,
                        Severity = row.Severity,
                        LogicType = row.LogicType,
                        TargetTable = row.TargetTable,
                        TargetColumn = row.TargetColumn,
                        Operator = row.Operator,
                        ComparisonValue = row.ComparisonValue,
                        Connector = row.Connector,
                        SecondaryColumn = row.SecondaryColumn,
                        SecondaryOperator = row.SecondaryOperator,
                        SecondaryValue = row.SecondaryValue,
                        CustomSqlPredicate = row.CustomSqlPredicate,
                        RemediationSuggestion = row.RemediationSuggestion,
                        IsActive = row.IsActive,
                        IsSystemFlag = row.IsSystemFlag,
                        CreatedDate = row.CreatedDate,
                        ModifiedDate = row.ModifiedDate,
                        CreatedBy = row.CreatedBy
                    });
                }

                return (list, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetFlagListAsync. Search: '{Search}', Page: {Page}.", search, page);
                throw;
            }
        }

        public async Task<FlaggedGridDataResult> GetFlaggedEmployeeGridAsync(
    string? employerIds, int filingYear, int skip, int take,
    string? searchValue, string? flagFilter, string? severityFilter,
    bool includeFlagDefs = true, string? editedEmployeeCodeIds = null,
    string? editedEmployeeIds = null, CancellationToken cancellationToken = default,
    string tab = "employee")
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            using var db = Connection;
            var p = new DynamicParameters();
            p.Add("@EmployerIDs", employerIds, DbType.AnsiString, size: -1);
            p.Add("@FilingYear", filingYear, DbType.Int32);
            p.Add("@Skip", skip, DbType.Int32);
            p.Add("@Take", take, DbType.Int32);
            p.Add("@SearchValue", searchValue, DbType.String, size: 100);
            p.Add("@FilterFlag", flagFilter, DbType.String, size: 128);
            p.Add("@FilterSeverity", severityFilter, DbType.String, size: 32);
            p.Add("@IncludeFlagDefs", includeFlagDefs, DbType.Boolean);
            p.Add("@EditedEmployeeCodeIDs", editedEmployeeCodeIds, DbType.AnsiString, size: -1);
            p.Add("@EditedEmployeeIDs", editedEmployeeIds, DbType.AnsiString, size: -1);
            p.Add("@Tab", tab, DbType.String, size: 16);
            try
            {
                using var multi = await db.QueryMultipleAsync(new CommandDefinition(
                    "dbo.sp_GetFlaggedEmployees_GridAndFlags", p,
                    commandType: CommandType.StoredProcedure, commandTimeout: 100,
                    cancellationToken: cancellationToken));
                var employees = (await multi.ReadAsync<FlaggedEmployeeGrid>()).ToList();
                var defs = (await multi.ReadAsync<FlagGridDefinition>()).ToList();
                var counts = await multi.ReadSingleAsync<FlaggedGridCounts>();
                return new FlaggedGridDataResult
                {
                    Employees = employees,
                    FlagDefs = defs,
                    TotalRecords = counts.TotalRecords,
                    FilteredRecords = counts.FilteredRecords
                };
            }
            finally
            {
                // Includes all three result sets and connection-open time. Never log search/SSN/payload.
                _logger.LogInformation("Flag grid SQL/read took {ElapsedMs} ms; year {Year}, skip {Skip}, take {Take}, tab {Tab}, definitions {Definitions}.",
                    timer.ElapsedMilliseconds, filingYear, skip, take, tab, includeFlagDefs);
            }
        }

        public async Task<FlaggedGridSaveResult> SaveFlaggedGridAsync(
            IReadOnlyCollection<FlaggedEmployeeEdit> edits, int filingYear,
            CancellationToken cancellationToken = default)
        {
            var payload = edits.Select(edit => new {
                edit.EmployeeId,
                edit.EmployeeCodeId,
                edit.HireSpanId,
                edit.EnrollmentId,
                edit.StatusId,
                edit.PayrollId,
                edit.CoveredIndividualId,
                edit.Changes
            }).ToList();
            // Explicit casing is local to SQL's case-sensitive OPENJSON contract.
            var options = new JsonSerializerOptions { PropertyNamingPolicy = null, DictionaryKeyPolicy = null };
            using var db = new SqlConnection(_connectionString);
            await db.OpenAsync(cancellationToken);
            var p = new DynamicParameters();
            p.Add("@FilingYear", filingYear, DbType.Int32);
            p.Add("@EditsJson", JsonSerializer.Serialize(payload, options), DbType.String, size: -1);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var multi = await db.QueryMultipleAsync(new CommandDefinition(
                    "dbo.sp_SaveFlaggedEmployees_Grid", p, commandType: CommandType.StoredProcedure,
                    commandTimeout: 120, cancellationToken: cancellationToken));
                var result = await multi.ReadSingleAsync<FlaggedGridSaveResult>();
                result.AuditChanges = (await multi.ReadAsync<FlaggedGridAuditChange>()).ToList();
                return result;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Flag grid database save failed for year {Year}.", filingYear);
                throw;
            }
            finally
            {
                _logger.LogInformation("Flag grid SAVE SQL/read took {ElapsedMs} ms for {EditCount} edit groups.",
                    timer.ElapsedMilliseconds, edits.Count);
            }
        }

        // Called independently after the first page is displayed. No child JSON is expanded.
        public async Task<List<FlagGridDefinition>> GetFlaggedGridFlagCountsAsync(
            string? employerIds, int filingYear, CancellationToken cancellationToken = default)
        {
            using var db = Connection;
            var p = new DynamicParameters();
            p.Add("@EmployerIDs", employerIds, DbType.AnsiString, size: -1);
            p.Add("@FilingYear", filingYear, DbType.Int32);
            p.Add("@CountsOnly", true, DbType.Boolean);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                return (await db.QueryAsync<FlagGridDefinition>(new CommandDefinition(
                    "dbo.sp_GetFlaggedEmployees_GridAndFlags_demo", p,
                    commandType: CommandType.StoredProcedure, commandTimeout: 60,
                    cancellationToken: cancellationToken))).ToList();
            }
            finally
            {
                _logger.LogInformation("Flag grid count SQL/read took {ElapsedMs} ms; year {Year}.",
                    timer.ElapsedMilliseconds, filingYear);
            }
        }


    }
}
