using ACA360.Core.Models;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;

namespace ACA360.BusinessLogic.Services
{
    public class AcaTrackingService : IAcaTrackingService
    {
        private readonly string _connectionString;
        private readonly ICommunicationRepository _commRepo;
        private readonly ILogger<AcaTrackingService> _logger;

        public AcaTrackingService(string connString, ICommunicationRepository commRepo,
            ILogger<AcaTrackingService> logger)
        {
            _connectionString = connString;
            _commRepo = commRepo;
            _logger = logger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task<AcaTrackingViewModel> GetTrackingSummaryAsync(int employerId, int year)
        {
            try
            {
                using var db = Connection;
                var summary = await db.QueryFirstOrDefaultAsync<AcaTrackingViewModel>(
                    "sp_GetAcaTrackingSummary",
                    new { EmployerId = employerId, TaxYear = year },
                    commandType: CommandType.StoredProcedure);

                return summary ?? new AcaTrackingViewModel();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetTrackingSummaryAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        public async Task<List<ConsentByLocation>> GetConsentByLocationAsync(int employerId, int year)
        {
            try
            {
                using var db = Connection;
                var result = await db.QueryAsync<ConsentByLocation>(
                    "sp_GetConsentByLocation",
                    new { EmployerId = employerId, TaxYear = year },
                    commandType: CommandType.StoredProcedure);

                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetConsentByLocationAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        public async Task<List<ConsentTrendPoint>> GetConsentTrendAsync(int employerId, int months, int year)
        {
            try
            {
                if (months <= 0) months = 6;
                if (year <= 0) year = DateTime.UtcNow.Year;

                using var db = Connection;
                var result = await db.QueryAsync<ConsentTrendPoint>(
                    "sp_GetConsentTrend",
                    new { EmployerId = employerId, Months = months, TaxYear = year },
                    commandType: CommandType.StoredProcedure);

                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetConsentTrendAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        public async Task<List<RecentConsentActivity>> GetRecentActivityAsync(int employerId, int top, int year)
        {
            try
            {
                if (top <= 0) top = 10;
                if (year <= 0) year = DateTime.UtcNow.Year;

                using var db = Connection;
                var result = await db.QueryAsync<RecentConsentActivity>(
                    "sp_GetRecentConsentActivity",
                    new { EmployerId = employerId, Top = top, TaxYear = year },
                    commandType: CommandType.StoredProcedure);

                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetRecentActivityAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        // Quick Action: Send Consent Reminder to employees with NULL (No Preference) consent
        public async Task SendConsentReminderAsync(int employerId, int year)
        {
            try
            {
                using var db = Connection;
                var noPrefEmployees = await db.QueryAsync<(int EmployeeId, string Email, string Name)>(
                    @"SELECT e.id AS EmployeeId, e.email AS Email, e.FirstName + ' ' + e.LastName AS Name
      FROM Employee e
      INNER JOIN EmployeeCode ec
              ON ec.employeeId = e.id
             AND ec.employerId = @EmployerId
             AND ec.filingYear = @TaxYear
             AND ec.isVoid = 0
      LEFT JOIN EmployeeConsent c
             ON c.EmployeeId = e.id
            AND c.TaxYear    = @TaxYear
      WHERE e.IsDeleted = 0
        AND c.Id IS NULL
        AND e.email IS NOT NULL AND e.email <> ''",
                    new { EmployerId = employerId, TaxYear = year });

                foreach (var emp in noPrefEmployees)
                {
                    string subject = $"Action Needed: Set Your {year} ACA Document Delivery Preference";
                    string body = $"Hi {emp.Name}, please log in and choose electronic or paper delivery for your {year} ACA forms.";

                    await _commRepo.LogEmailAsync(emp.EmployeeId, "Employee", "Consent-Reminder", emp.Email, 0, subject, body);
                    // TODO: actual SMTP send here (same pattern as LogEmailSentAsync in DistributionService)
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendConsentReminderAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }
    }
}