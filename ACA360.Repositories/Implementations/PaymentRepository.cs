using ACA360.Repositories.Interfaces;
using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<PaymentRepository> _logger;

        /// <summary>
        /// Purpose: Initializes the PaymentRepository with database connection and logging dependencies.
        /// Input parameters: string connectionString, ILogger logger
        /// Output/return value: None
        /// </summary>
        public PaymentRepository(string connectionString, ILogger<PaymentRepository> logger)
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
                    logger.LogError(ex, "Error occurred during initialization of PaymentRepository.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Creates a new payment record in the database.
        /// Input parameters: Payment payment
        /// Output/return value: Task
        /// </summary>
        public async Task CreatePaymentAsync(Payment payment)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_CreatePayment",
                    payment,
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in CreatePaymentAsync.");
            }
        }

        /// <summary>
        /// Purpose: Retrieves all payments associated with a specific employer EIN.
        /// Input parameters: string employerEIN
        /// Output/return value: Task of IEnumerable of Payment
        /// </summary>
        public async Task<IEnumerable<Payment>> GetPaymentsForEmployerAsync(string employerEIN)
        {
            try
            {
                using var db = Connection;
                return await db.QueryAsync<Payment>(
                    "sp_GetPaymentsForEmployer",
                    new { EmployerEIN = employerEIN },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetPaymentsForEmployerAsync for EmployerEIN: {EmployerEIN}", employerEIN);
                throw;
            }
        }
    }
}