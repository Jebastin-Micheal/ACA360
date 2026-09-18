// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>
    /// Dapper-based implementation of <see cref="IConsentService"/>.
    /// Mirrors the DistributionService pattern: a connection string in,
    /// a fresh SqlConnection per call, stored procedures for all I/O.
    /// </summary>
    public class ConsentService : IConsentService
    {
        private readonly string _connectionString;
        private readonly ILogger<ConsentService> _logger;

        public ConsentService(string connString, ILogger<ConsentService> logger)
        {
            _connectionString = connString;
            _logger = logger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <inheritdoc />
        public async Task<EmployeeConsentViewModel> GetConsentAsync(int employeeId, int taxYear)
        {
            try
            {
                using var db = Connection;

                // Ongoing consent: carry a prior year's active electronic consent into this year
                // if none exists yet (no-op otherwise).
                await db.ExecuteAsync(
                    "sp_CarryForwardConsent",
                    new { EmployeeId = employeeId, TaxYear = taxYear },
                    commandType: CommandType.StoredProcedure);

                using var multi = await db.QueryMultipleAsync(
                    "sp_GetEmployeeConsent",
                    new { EmployeeId = employeeId, TaxYear = taxYear },
                    commandType: CommandType.StoredProcedure);

                var record = await multi.ReadFirstOrDefaultAsync<EmployeeConsentViewModel>();
                var events = (await multi.ReadAsync<ConsentEvent>()).ToList();

                if (record == null)
                {
                    // No consent captured yet - return a blank model for a first visit.
                    return new EmployeeConsentViewModel
                    {
                        EmployeeId = employeeId,
                        TaxYear = taxYear
                    };
                }

                record.History = events;
                return record;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetConsentAsync for EmployeeId {EmployeeId}, Year {Year}", employeeId, taxYear);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<string?> SaveConsentAsync(EmployeeConsentViewModel model, string ipAddress, string userAgent)
        {
            try
            {
                using var db = Connection;
                var result = await db.QueryFirstOrDefaultAsync(
                    "sp_SaveEmployeeConsent",
                    new
                    {
                        model.EmployeeId,
                        model.TaxYear,
                        model.DeliveryPreference,
                        model.NotificationEmail,
                        ConsentMethod = string.IsNullOrEmpty(model.ConsentMethod) ? "Employee Portal" : model.ConsentMethod,
                        IpAddress = ipAddress,
                        UserAgent = userAgent
                    },
                    commandType: CommandType.StoredProcedure);

                return result?.ConsentId as string;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SaveConsentAsync for EmployeeId {EmployeeId}, Year {Year}", model.EmployeeId, model.TaxYear);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task LogEventAsync(int employeeId, int taxYear, string eventType, string? detail)
        {
            try
            {
                using var db = Connection;
                await db.ExecuteAsync(
                    "sp_LogConsentEvent",
                    new { EmployeeId = employeeId, TaxYear = taxYear, EventType = eventType, Detail = detail },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                // Audit logging must never block the user flow.
                _logger.LogError(ex, "Error in LogEventAsync ({EventType}) for EmployeeId {EmployeeId}", eventType, employeeId);
            }
        }

        /// <inheritdoc />
        public async Task<bool> IsFormAvailableAsync(int employeeId, int taxYear)
        {
            try
            {
                using var db = Connection;
                // A 1095-C is available when the employee is flagged to receive a form for that year.
                var available = await db.ExecuteScalarAsync<int?>(
                    @"SELECT CASE WHEN EXISTS (
                          SELECT 1 FROM dbo.EmployeeCode
                          WHERE employeeId = @EmployeeId AND filingYear = @TaxYear AND getForm = 1
                      ) THEN 1 ELSE 0 END",
                    new { EmployeeId = employeeId, TaxYear = taxYear });
                return available == 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in IsFormAvailableAsync for EmployeeId {EmployeeId}, Year {Year}", employeeId, taxYear);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<List<int>> GetAvailableFormYearsAsync(int employeeId)
        {
            try
            {
                using var db = Connection;
                var years = await db.QueryAsync<int>(
                    @"SELECT DISTINCT filingYear
                        FROM dbo.EmployeeCode
                       WHERE employeeId = @id AND getForm = 1 AND filingYear IS NOT NULL
                       ORDER BY filingYear DESC",
                    new { id = employeeId });
                return years.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetAvailableFormYearsAsync for EmployeeId {EmployeeId}", employeeId);
                return new List<int>();
            }
        }

        /// <inheritdoc />
        public async Task<string> GetFormTypeAsync(int employeeId, int taxYear)
        {
            try
            {
                using var db = Connection;
                // Form type is driven by the employer's transmittal type (1094-B => 1095-B, else 1095-C).
                var ft = await db.ExecuteScalarAsync<string>(
                    @"SELECT er.FormType
                        FROM dbo.Employee e
                        JOIN dbo.Employer er ON er.id = e.EmployerId
                       WHERE e.id = @id",
                    new { id = employeeId });

                return (!string.IsNullOrEmpty(ft) && ft.IndexOf('B') >= 0) ? "1095-B" : "1095-C";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetFormTypeAsync for EmployeeId {EmployeeId}", employeeId);
                return "1095-C";
            }
        }

        /// <inheritdoc />
        public async Task RequestFormNotificationAsync(int employeeId, int taxYear)
        {
            try
            {
                using var db = Connection;
                await db.ExecuteAsync(
                    "sp_RequestFormNotification",
                    new { EmployeeId = employeeId, TaxYear = taxYear },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RequestFormNotificationAsync for EmployeeId {EmployeeId}, Year {Year}", employeeId, taxYear);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<bool> IsNotifyRequestedAsync(int employeeId, int taxYear)
        {
            try
            {
                using var db = Connection;
                var v = await db.ExecuteScalarAsync<int?>(
                    "sp_IsFormNotifyRequested",
                    new { EmployeeId = employeeId, TaxYear = taxYear },
                    commandType: CommandType.StoredProcedure);
                return v == 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in IsNotifyRequestedAsync for EmployeeId {EmployeeId}, Year {Year}", employeeId, taxYear);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task WithdrawAsync(int employeeId, int taxYear)
        {
            try
            {
                using var db = Connection;
                await db.ExecuteAsync(
                    "sp_WithdrawConsent",
                    new { EmployeeId = employeeId, TaxYear = taxYear },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in WithdrawAsync for EmployeeId {EmployeeId}, Year {Year}", employeeId, taxYear);
                throw;
            }
        }
    }
}
