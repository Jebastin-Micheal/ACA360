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
    public class CommunicationRepository : ICommunicationRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<CommunicationRepository> _logger;
        //private readonly IConfiguration _configuration;
        /// <summary>
        /// Purpose: Initializes the CommunicationRepository with the database connection and logger.
        /// Input parameters: string connectionString, ILogger logger
        /// Output/return value: None
        /// </summary>
        public CommunicationRepository(IConfiguration configuration, ILogger<CommunicationRepository> logger)
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
                    logger.LogError(ex, "Error occurred during initialization of CommunicationRepository.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Creates a new communication log entry in the database.
        /// Input parameters: CommunicationLog log
        /// Output/return value: Task
        /// </summary>
        public async Task CreateLogAsync(CommunicationLog log)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_CreateCommunicationLog",
                    log,
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in CreateLogAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves all communication logs associated with a specific employer EIN.
        /// Input parameters: string employerEIN
        /// Output/return value: Task of IEnumerable of CommunicationLog
        /// </summary>
        public async Task<IEnumerable<CommunicationLog>> GetLogsForEmployerAsync(string employerEIN)
        {
            try
            {
                using var db = Connection;
                return await db.QueryAsync<CommunicationLog>(
                    "sp_GetCommunicationLogsForEmployer",
                    new { EmployerEIN = employerEIN },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetLogsForEmployerAsync for EmployerEIN: {EmployerEIN}", employerEIN);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Logs a specific email communication event into the database.
        /// Input parameters: int entityId, string entityType, string type, string sentTo, int sentBy, string subject, string body
        /// Output/return value: Task
        /// </summary>
        public async Task LogEmailAsync(int entityId, string entityType, string type, string sentTo, int sentBy, string subject, string body, string? status = null)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_LogEmail",
                    new
                    {
                        EntityId = entityId,
                        EntityType = entityType,
                        Type = type,
                        SentTo = sentTo,
                        SentBy = sentBy,
                        Subject = subject,
                        Body = body,
                        Status = status
                    },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in LogEmailAsync for EntityId: {EntityId}, SentTo: {SentTo}", entityId, sentTo);
                throw;
            }
        }
    }
}