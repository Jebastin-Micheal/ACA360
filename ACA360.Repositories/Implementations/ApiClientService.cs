using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class ApiClientService : IApiClientService
    {
        private readonly string _connectionString;
        private readonly ILogger<ApiClientService> _logger;

        /// <summary>
        /// Purpose: Initializes the ApiClientService with necessary dependencies.
        /// Input parameters: string connectionString, ILogger logger
        /// Output/return value: None
        /// </summary>
        public ApiClientService(IConfiguration configuration, ILogger<ApiClientService> logger)
        {
            try
            {
                _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing from configuration."); ; 
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logger.LogError(ex, "Error occurred during ApiClientService initialization.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Validates an API client by checking the Client ID and Secret Hash.
        /// Input parameters: string clientId, string clientSecret
        /// Output/return value: Task of ApiClient
        /// </summary>
        public async Task<ApiClient?> ValidateClientAsync(string clientId, string clientSecret)
        {
            try
            {
                using var db = Connection;
                return await db.QueryFirstOrDefaultAsync<ApiClient>(
                    "sp_ValidateApiClient",
                    new { CId = clientId, Secret = clientSecret },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in ValidateClientAsync for ClientId: {ClientId}", clientId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a list of active API clients for a specific employer.
        /// Input parameters: int employerId
        /// Output/return value: Task of IEnumerable of ApiClientViewModel
        /// </summary>
        public async Task<IEnumerable<ApiClientViewModel>> GetClientsForEmployerAsync(int employerId)
        {
            try
            {
                using var db = Connection;
                return await db.QueryAsync<ApiClientViewModel>(
                    "sp_GetClientsForEmployer",
                    new { EmpId = employerId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetClientsForEmployerAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Generates a new API client key, hashes the secret, and saves the client to the database.
        /// Input parameters: int employerId, string name
        /// Output/return value: Task of NewKeyResult
        /// </summary>
        public async Task<NewKeyResult> GenerateNewClientAsync(int employerId, string name)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                // A. Generate Random Secret
                string secret = "sk_live_" + GenerateRandomString(32);

                // B. Hash it for the Database
                string secretHash = ComputeSha256Hash(secret);

                // C. Generate Client ID
                Guid clientId = Guid.NewGuid();

                await db.ExecuteAsync(
                    "sp_GenerateNewApiClient",
                    new
                    {
                        CId = clientId,
                        EmpId = employerId,
                        Hash = secretHash,
                        Name = name
                    },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();

                // D. Return the PLAIN secret (One time only!)
                return new NewKeyResult
                {
                    ClientId = clientId,
                    ClientSecret = secret,
                    Name = name
                };
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in GenerateNewClientAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Revokes an active API client key by setting it to inactive.
        /// Input parameters: Guid clientId, int employerId
        /// Output/return value: Task
        /// </summary>
        public async Task RevokeClientAsync(Guid clientId, int employerId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_RevokeApiClient",
                    new { CId = clientId, EmpId = employerId },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in RevokeClientAsync for ClientId: {ClientId}", clientId);
                throw;
            }
        }

        // --- CRYPTO HELPERS ---

        /// <summary>
        /// Purpose: Generates a cryptographically secure URL-safe random string.
        /// Input parameters: int length
        /// Output/return value: string
        /// </summary>
        private string GenerateRandomString(int length)
        {
            try
            {
                using var rng = RandomNumberGenerator.Create();
                var bytes = new byte[length];
                rng.GetBytes(bytes);
                return Convert.ToBase64String(bytes)
                    .Replace("+", "").Replace("/", "").Replace("=", "")
                    .Substring(0, length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GenerateRandomString.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Computes a SHA-256 hash for a given raw string.
        /// Input parameters: string rawData
        /// Output/return value: string
        /// </summary>
        private string ComputeSha256Hash(string rawData)
        {
            try
            {
                using var sha256 = SHA256.Create();
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in ComputeSha256Hash.");
                throw;
            }
        }
    }
}