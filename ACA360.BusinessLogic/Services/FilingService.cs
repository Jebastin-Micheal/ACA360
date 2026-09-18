using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Dapper;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public interface IFilingService
    {
        Task<FilingDashboardViewModel> GetFilingDashboardAsync(string userId, string role, int year, PaginationEntity pagination);
        Task FinalizeFilingAsync(int employerId, int year, string userId);
        Task UnlockFilingAsync(int employerId, int year, string userId); // SuperAdmin only
        Task<List<Monthly1094Count>> Calculate1094CountsAsync(int employerId, int year);
        Task<List<FilingAuditLogDto>> GetFilingTimelineAsync(int employerId, int year);
        Task ToggleArchiveFilingAsync(int employerId, int year, bool isArchive, string userId);
    }

    public class FilingService : IFilingService
    {
        private readonly string _connectionString;
        private readonly IWebHostEnvironment _env;
        private readonly IAzureBlobService _azureBlobService;
        private readonly string _generatedReportsFolder;
        private readonly ILogger<FilingService> _logger;

        /// <summary>
        /// Purpose: Initializes the FilingService with necessary dependencies and configurations.
        /// Input parameters: string connectionString, IWebHostEnvironment env, IConfiguration config, IAzureBlobService azureBlobService, ILogger logger
        /// Output/return value: None
        /// </summary>
        public FilingService(string connectionString, IWebHostEnvironment env, IConfiguration config, IAzureBlobService azureBlobService, ILogger<FilingService> logger)
        {
            try
            {
                _connectionString = connectionString;
                _env = env;
                _azureBlobService = azureBlobService;
                _generatedReportsFolder = config["AzureBlob:GeneratedReports"] ?? "GeneratedReports";
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "Error occurred during initialization of FilingService.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves the filing dashboard status for a given user, role, and tax year, and verifies if report batches exist in blob storage.
        /// Input parameters: string userId, string role, int year
        /// Output/return value: Task of List of EmployerFilingStatusDto
        /// </summary>
        public async Task<FilingDashboardViewModel> GetFilingDashboardAsync(string userId, string role, int year, PaginationEntity pagination)
        {
            try
            {
                using var db = Connection;
                var parameters = new
                {
                    UserId = userId,
                    UserRole = role,
                    Year = year,
                    PageIndex = pagination.PageIndex,
                    PageSize = pagination.PageSize,
                    Search = pagination.Search,
                    SortColumn = pagination.SortColumn,
                    SortOrder = pagination.SortOrder ?? "asc",
                    StatusFilter = pagination.StatusFilter
                };

                using var multi = await db.QueryMultipleAsync("sp_GetFilingCenterDashboard", parameters, commandType: CommandType.StoredProcedure);

                // 1. Read the paginated Employers list
                var employers = (await multi.ReadAsync<EmployerFilingStatusDto>()).AsList();

                // 2. Read Pagination Metadata
                PaginationViewEntity paginationMeta = null;
                if (!multi.IsConsumed)
                {
                    int totalItems = await multi.ReadSingleOrDefaultAsync<int>();
                    int pageSize = pagination.PageSize;
                    int currentPage = pagination.PageIndex;

                    paginationMeta = new PaginationViewEntity
                    {
                        TotalItems = totalItems,
                        CurrentPage = currentPage,
                        PageSize = pageSize,
                        TotalPages = (int)Math.Ceiling((double)totalItems / pageSize),
                        StartPage = (currentPage - 1) * pageSize + 1,
                        EndPage = Math.Min((currentPage - 1) * pageSize + pageSize, totalItems),
                        SortColumn = pagination.SortColumn,
                        SortOrder = pagination.SortOrder,
                        TotalCount = totalItems
                    };
                }

                FilingStatusSummary statusSummary = null;
                if (!multi.IsConsumed)
                {
                    statusSummary = await multi.ReadSingleOrDefaultAsync<FilingStatusSummary>();
                }

                // 3. Check File System for Artifacts (Now extremely fast because it only checks 10 rows!)
                if (employers.Any())
                {
                    var employerIds = employers.Select(e => e.EmployerId).ToList();

                    var batchedEmployerIds = (await db.QueryAsync<int>(
                        @"SELECT DISTINCT e.EmployerId
                  FROM GenerateMultiEmployerBatchHistoryEmployers e
                  WHERE e.EmployerId IN @EmployerIds AND e.TaxYear = @Year",
                        new { EmployerIds = employerIds, Year = year }))
                        .ToHashSet();

                    foreach (var item in employers)
                    {
                        item.HasBatch = batchedEmployerIds.Contains(item.EmployerId);
                    }
                }

                return new FilingDashboardViewModel
                {
                    SelectedYear = year,
                    Employers = employers,
                    Metadata = paginationMeta,
                    Search = pagination.Search,
                    SortColumn = pagination.SortColumn,
                    SortOrder = pagination.SortOrder,
                    StatusSummary = statusSummary ?? new FilingStatusSummary()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetFilingDashboardAsync");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Finalizes the filing process for a specific employer for a given tax year.
        /// Input parameters: int employerId, int year, string userId
        /// Output/return value: Task
        /// </summary>
        public async Task FinalizeFilingAsync(int employerId, int year, string userId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_FinalizeFiling",
                    new { EmployerId = employerId, TaxYear = year, UserId = userId },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in FinalizeFilingAsync for EmployerId: {EmployerId}, Year: {Year}", employerId, year);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Unlocks a previously finalized filing, allowing for further modifications (Restricted to SuperAdmin).
        /// Input parameters: int employerId, int year
        /// Output/return value: Task
        /// </summary>
        public async Task UnlockFilingAsync(int employerId, int year, string userId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_UnlockFiling",
                    new { EmployerId = employerId, TaxYear = year, UserId = userId },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in UnlockFilingAsync for EmployerId: {EmployerId}, Year: {Year}", employerId, year);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Calculates and retrieves the monthly count of 1094 records for a specific employer and year.
        /// Input parameters: int employerId, int year
        /// Output/return value: Task of List of Monthly1094Count
        /// </summary>
        public async Task<List<Monthly1094Count>> Calculate1094CountsAsync(int employerId, int year)
        {
            try
            {
                using var db = Connection;
                return (await db.QueryAsync<Monthly1094Count>(
                    "sp_Calculate1094Counts",
                    new { EmployerId = employerId, Year = year },
                    commandType: CommandType.StoredProcedure)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in Calculate1094CountsAsync for EmployerId: {EmployerId}, Year: {Year}", employerId, year);
                throw;
            }
        }


        public async Task ToggleArchiveFilingAsync(int employerId, int year, bool isArchive, string userId)
        {
            try
            {
                using var db = Connection;

                string sql = @"UPDATE EmployerFilingStatus 
                       SET IsArchived = @IsArchived 
                       WHERE EmployerId = @EmployerId AND TaxYear = @TaxYear";

                await db.ExecuteAsync(sql, new
                {
                    IsArchived = isArchive ? 1 : 0,
                    EmployerId = employerId,
                    TaxYear = year
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in ToggleArchiveFilingAsync for EmployerId: {EmployerId}, Year: {Year}", employerId, year);
                throw;
            }
        }

        public async Task<List<FilingAuditLogDto>> GetFilingTimelineAsync(int employerId, int year)
        {
            try
            {
                using var db = Connection;
                return (await db.QueryAsync<FilingAuditLogDto>(
                    "sp_GetFilingAuditLog",
                    new { EmployerId = employerId, TaxYear = year },
                    commandType: CommandType.StoredProcedure)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetFilingTimelineAsync for EmployerId: {EmployerId}, Year: {Year}", employerId, year);
                throw;
            }
        }
    }
}