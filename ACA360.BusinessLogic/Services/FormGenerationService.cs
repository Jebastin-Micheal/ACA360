using ACA360.Core.Models;
using Dapper;
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
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class FormGenerationService : IFormGenerationService
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly IWebHostEnvironment _environment;
        private readonly IAzureBlobService _azureBlobService;
        private readonly string _generatedReportsFolder;
        private readonly ILogger<FormGenerationService> _logger;

        /// <summary>
        /// Purpose: Initializes the FormGenerationService with configuration, environment, blob storage, and logging dependencies.
        /// Input parameters: IConfiguration configuration, IWebHostEnvironment environment, string connectionString, IAzureBlobService azureBlobService, ILogger logger
        /// Output/return value: None
        /// </summary>
        public FormGenerationService(IConfiguration configuration, IWebHostEnvironment environment, string connectionString, IAzureBlobService azureBlobService, ILogger<FormGenerationService> logger)
        {
            try
            {
                _connectionString = connectionString;
                _environment = environment;
                _configuration = configuration;
                _azureBlobService = azureBlobService;
                _generatedReportsFolder = configuration["AzureBlob:GeneratedReports"] ?? "GeneratedReports";
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "Error occurred during initialization of FormGenerationService.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves the generation dashboard data for employers, including the latest file generation dates from Azure Blob Storage.
        /// Input parameters: string userId, string userRole, int year, string filterStatus, string sortColumn, string sortOrder, string searchTerm
        /// Output/return value: Task of List of EmployerGenerationStatusDto
        /// </summary>
        public async Task<List<EmployerGenerationStatusDto>> GetGenerationDashboardAsync(
            string userId, string userRole, int year,
            string filterStatus, string sortColumn, string sortOrder, string searchTerm = "")
        {
            try
            {
                using var db = Connection;

                // 1. Call the Stored Procedure
                var employers = (await db.QueryAsync<EmployerGenerationStatusDto>(
                    "sp_GetGenerationDashboard",
                    new
                    {
                        UserId = userId,
                        UserRole = userRole,
                        Year = year,
                        FilterStatus = filterStatus,
                        SortColumn = sortColumn,
                        SortOrder = sortOrder?.ToUpper(),
                        SearchTerm = searchTerm
                    },
                    commandType: CommandType.StoredProcedure)).ToList();

                var fileDates = await _azureBlobService.GetLatestFileDatesAsync(_generatedReportsFolder, year);

                // Map the latest dates back to the employers list
                foreach (var emp in employers)
                {
                    if (fileDates.ContainsKey(emp.EmployerId))
                    {
                        emp.LastGeneratedDate = fileDates[emp.EmployerId];
                    }
                }

                return employers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetGenerationDashboardAsync for UserId: {UserId}, Year: {Year}", userId, year);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a list of employees associated with a specific employer for form preview purposes.
        /// Input parameters: int employerId
        /// Output/return value: Task of IEnumerable of dynamic
        /// </summary>
        public async Task<IEnumerable<dynamic>> GetEmployeesForPreviewAsync(int employerId)
        {
            try
            {
                using var db = Connection;

                return await db.QueryAsync(
                    "sp_GetEmployeesForPreview",
                    new { EmployerId = employerId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetEmployeesForPreviewAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        public async Task<Dictionary<int, (string City, string Zip)>> GetEmployeeCityAndZipAsync(List<int> employeeIds)
        {
            
            if (employeeIds == null || !employeeIds.Any())
                return new Dictionary<int, (string, string)>();

            using var db = Connection;

            
            string sql = "SELECT id, ISNULL(city, '') AS city, ISNULL(zip, '') AS zip FROM Employee WHERE id IN @Ids";

            var result = await db.QueryAsync(sql, new { Ids = employeeIds });

            
            return result.ToDictionary(
                row => (int)row.id,
                row => ((string)row.city, (string)row.zip)
            );
        }
    }
}