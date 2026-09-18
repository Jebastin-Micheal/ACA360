using ACA360.Core.Models;
using ACA360.Data.Helpers;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class RuleRepository : IRuleService
    {
        private readonly string _connectionString;
        private readonly ILogger<RuleRepository> _logger;

        /// <summary>
        /// Purpose: Initializes the RuleRepository with database connection and logging dependencies.
        /// Input parameters: string connectionString, ILogger logger
        /// Output/return value: None
        /// </summary>
        public RuleRepository(string connectionString, ILogger<RuleRepository> logger)
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
                    logger.LogError(ex, "Error initializing RuleRepository.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves a paginated list of validation rules with associated metadata.
        /// Input parameters: PaginationEntity paginationEntity
        /// Output/return value: Task of (List of ValidationRule, PaginationViewEntity)
        /// </summary>
        public async Task<(List<ValidationRule> Rules, PaginationViewEntity PageInfo)> GetRuleList(PaginationEntity paginationEntity)
        {
            try
            {
                using var db = Connection;

                var parameters = new
                {
                    paginationEntity.PageIndex,
                    paginationEntity.PageSize,
                    Search = paginationEntity.Search ?? (object)DBNull.Value,
                    paginationEntity.SortColumn,
                    SortOrder = paginationEntity.SortOrder ?? "asc"
                };

                using var multi = await db.QueryMultipleAsync(
                    "sp_GetAllValidationRules_List",
                    parameters,
                    commandType: CommandType.StoredProcedure);

                // Read the first result set (rules)
                var ruleList = (await multi.ReadAsync<ValidationRule>()).ToList();

                // Read the second result set (total count)
                int totalItems = await multi.ReadSingleAsync<int>();

                // Calculate pagination metadata
                int pageSize = paginationEntity.PageSize;
                int currentPage = paginationEntity.PageIndex;

                var paginationMeta = new PaginationViewEntity
                {
                    TotalItems = totalItems,
                    CurrentPage = currentPage,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalItems / pageSize),
                    StartPage = (currentPage - 1) * pageSize + 1,
                    EndPage = Math.Min((currentPage - 1) * pageSize + pageSize, totalItems),
                    SortColumn = paginationEntity.SortColumn,
                    SortOrder = paginationEntity.SortOrder,
                    RecordCount = totalItems,
                    PageNumber = currentPage,
                    TotalCount = totalItems
                };

                return (ruleList, paginationMeta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetRuleList.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a specific validation rule by its ID.
        /// Input parameters: int id
        /// Output/return value: Task of ValidationRule
        /// </summary>
        public async Task<ValidationRule> GetRuleByIdAsync(int id)
        {
            try
            {
                using var db = Connection;
                return await db.QuerySingleOrDefaultAsync<ValidationRule>(
                    "sp_GetValidationRuleById",
                    new { RuleId = id },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetRuleByIdAsync for RuleId: {RuleId}", id);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Adds a new validation rule to the database.
        /// Input parameters: ValidationRule rule
        /// Output/return value: Task
        /// </summary>
        public async Task AddRuleAsync(ValidationRule rule)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_CreateValidationRule",
                    rule,
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in AddRuleAsync for Rule: {RuleName}", rule?.RuleName);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Updates an existing validation rule in the database.
        /// Input parameters: ValidationRule rule
        /// Output/return value: Task
        /// </summary>
        public async Task UpdateRuleAsync(ValidationRule rule)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_UpdateValidationRule",
                    rule,
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in UpdateRuleAsync for RuleId: {RuleId}", rule?.RuleId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Deletes a specific validation rule from the database.
        /// Input parameters: int id
        /// Output/return value: Task
        /// </summary>
        public async Task DeleteRuleAsync(int id)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_DeleteValidationRule",
                    new { RuleId = id },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in DeleteRuleAsync for RuleId: {RuleId}", id);
                throw;
            }
        }
    }
}