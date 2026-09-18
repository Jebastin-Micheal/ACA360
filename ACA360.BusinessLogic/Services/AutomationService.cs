using ACA360.Core.Models;
using Dapper;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class AutomationService : IAutomationService
    {
        private readonly string _connection;
        private readonly IBackgroundJobClient _backgroundJobs;
        private readonly IRecurringJobManager _recurringJobs;
        private readonly ILogger<AutomationService> _logger;

        /// <summary>
        /// Purpose: Initializes the AutomationService with configuration, Hangfire clients, and logger.
        /// Input parameters: IConfiguration config, IBackgroundJobClient backgroundJobs, IRecurringJobManager recurringJobs, ILogger logger
        /// Output/return value: None
        /// </summary>
        public AutomationService(IConfiguration config,
            IBackgroundJobClient backgroundJobs,
            IRecurringJobManager recurringJobs,
            ILogger<AutomationService> logger)
        {
            try
            {
                _connection = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing from configuration."); ;
                _backgroundJobs = backgroundJobs;
                _recurringJobs = recurringJobs;
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "Error occurred during initialization of AutomationService.");
                }
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a list of all automation jobs from the database.
        /// Input parameters: None
        /// Output/return value: Task of IEnumerable of AutomationJobDto
        /// </summary>
        public async Task<IEnumerable<AutomationJobDto>> GetJobsAsync()
        {
            try
            {
                using var conn = new SqlConnection(_connection);
                return await conn.QueryAsync<AutomationJobDto>(
                    "usp_GetAutomationJobs",
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetJobsAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Updates an automation job's database record and synchronizes its schedule with Hangfire.
        /// Input parameters: string jobKey, string cron, bool enabled
        /// Output/return value: Task
        /// </summary>
        public async Task UpdateJobAsync(string jobKey, string cron, bool enabled)
        {
            using var conn = new SqlConnection(_connection);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync(
                    "usp_UpdateAutomationJob",
                    new { JobKey = jobKey, Cron = cron, Enabled = enabled },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure
                );

                if (enabled)
                    _recurringJobs.AddOrUpdate(jobKey, () => Console.WriteLine($"Running {jobKey}"), cron);
                else
                    _recurringJobs.RemoveIfExists(jobKey);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in UpdateJobAsync for JobKey: {JobKey}", jobKey);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Manually enqueues an automation job to run immediately via Hangfire.
        /// Input parameters: string jobKey
        /// Output/return value: Task
        /// </summary>
        public Task RunJobNowAsync(string jobKey)
        {
            try
            {
                _backgroundJobs.Enqueue(() => Console.WriteLine($"Manual run triggered: {jobKey}"));
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in RunJobNowAsync for JobKey: {JobKey}", jobKey);
                throw;
            }
        }
    }
}