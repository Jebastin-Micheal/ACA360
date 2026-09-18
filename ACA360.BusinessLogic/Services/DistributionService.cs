using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
namespace ACA360.BusinessLogic.Services
{
    public class DistributionService : IDistributionService
    {
        private readonly string _connectionString;
        private readonly ICommunicationRepository _commRepo;
        private readonly ISystemSettingsService _settings;
        private readonly IMailDeliveryService _mail;
        private readonly ILogger<DistributionService> _logger;

        /// <summary>
        /// Purpose: Initializes the DistributionService with necessary dependencies.
        /// Input parameters: string connString, ICommunicationRepository commRepo, ISystemSettingsService settings, ILogger logger
        /// Output/return value: None
        /// </summary>
        public DistributionService(string connString, ICommunicationRepository commRepo, ISystemSettingsService settings, IMailDeliveryService mail, ILogger<DistributionService> logger)
        {
            try
            {
                _connectionString = connString;
                _commRepo = commRepo;
                _settings = settings;
                _mail = mail;
                _logger = logger;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during initialization of DistributionService.");
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves statistics regarding employee document distribution (electronic vs paper).
        /// Input parameters: int employerId, int year
        /// Output/return value: Task of DistributionDashboardViewModel
        /// </summary>
        /// <summary>
        /// Purpose: Retrieves statistics regarding employee document distribution (electronic vs paper).
        /// Input parameters: int employerId, int year
        /// Output/return value: Task of DistributionDashboardViewModel
        /// </summary>
        public async Task<DistributionDashboardViewModel> GetDistributionStatsAsync(int employerId, int year, PaginationEntity pagination, string statusFilter = "all")
        {
            try
            {
                using var db = Connection;
                var parameters = new
                {
                    EmployerId = employerId,
                    TaxYear = year,
                    PageIndex = pagination.PageIndex,
                    PageSize = pagination.PageSize,
                    Search = pagination.Search,
                    SortColumn = pagination.SortColumn,
                    SortOrder = pagination.SortOrder ?? "asc",
                    StatusFilter = statusFilter
                };

                // Execute multiple queries
                using var multi = await db.QueryMultipleAsync("sp_GetDistributionStats", parameters, commandType: CommandType.StoredProcedure);

                // 1. Read Stats
                var stats = await multi.ReadSingleAsync<DistributionDashboardViewModel>();

                // 2. Read Employees
                var employees = (await multi.ReadAsync<EmployeeDistributionStatus>()).ToList();

                // 3. Read Pagination Metadata
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

                // Combine into final model
                stats.Employees = employees;
                stats.Metadata = paginationMeta;

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetDistributionStatsAsync");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Generates a secure, unique link token for an employee to access their documents.
        /// Input parameters: int employeeId, int year
        /// Output/return value: Task of string
        /// </summary>
        public async Task<string> GenerateSecureLinkAsync(int employeeId, int year)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var token = Guid.NewGuid();

                await db.ExecuteAsync(
                    "sp_GenerateSecureLink",
                    new { LinkId = token, EmployeeId = employeeId, TaxYear = year, Token = token.ToString() },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
                return token.ToString();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in GenerateSecureLinkAsync for EmployeeId: {EmployeeId}", employeeId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Validates a secure token against the provided Last 4 SSN and marks it as used if valid.
        /// Input parameters: Guid linkId, string last4SSN
        /// Output/return value: Task of int? (returns EmployeeId if valid, null otherwise)
        /// </summary>
        public async Task<SecureTokenValidationResult> ValidateTokenAsync(
            Guid linkId, string last4SSN, string? ipAddress = null)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var result = await db.QueryFirstOrDefaultAsync<SecureTokenValidationResult>(
                    "sp_ValidateSecureToken",
                    new { LinkId = linkId, Last4SSN = last4SSN, IpAddress = ipAddress },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                // The procedure records the failure count and any lockout, so the commit
                // has to happen on the failure path too — rolling back would discard the
                // very counter that limits guessing.
                tx.Commit();

                // Status 1 (invalid) is the safe default: a procedure that returned
                // nothing must not read as success.
                return result ?? new SecureTokenValidationResult
                {
                    EmployeeId = null, Year = null, Status = 1
                };
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in ValidateTokenAsync for LinkId: {LinkId}", linkId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Logs a generic email communication event.
        /// Input parameters: int entityId, string entityType, string type, string sentTo, int sentBy, string subject, string body
        /// Output/return value: Task
        /// </summary>
        public async Task LogEmailAsync(int entityId, string entityType, string type, string sentTo, int sentBy, string subject, string body)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_LogCommunication",
                    new
                    {
                        EntityId = entityId,
                        EntityType = entityType,
                        Type = type,
                        SentTo = sentTo,
                        SentBy = sentBy,
                        Subject = subject,
                        Body = body
                    },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in LogEmailAsync for EntityId: {EntityId}", entityId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Fetches employee email and logs that a secure document link was sent.
        /// Input parameters: int employeeId, int year, string secureLink
        /// Output/return value: Task
        /// </summary>
        /// <summary>
        /// Composes the employee's 1095-C notification, SENDS it, then records what
        /// actually happened.
        ///
        /// This method previously composed the message and wrote a log row without ever
        /// sending anything — the send was a commented-out line in the controller — so
        /// the Distribution Center reported deliveries that never left the building
        /// (F-37). The log row now carries the real outcome in CommunicationLog.Status.
        ///
        /// It does not throw when delivery fails. A batch of several hundred must not
        /// stop because one employee has a bad address, and the caller reads the result.
        /// </summary>
        public async Task<MailDeliveryResult> SendEmployeeFormEmailAsync(
            int employeeId, int year, string secureLink)
        {
            string email = "Unknown";
            string subject = $"Action Required: Your {year} Form 1095-C is Ready";
            MailDeliveryResult result;

            try
            {
                using var db = Connection;

                email = await db.ExecuteScalarAsync<string>(
                    "sp_GetEmployeeEmail",
                    new { EmployeeId = employeeId },
                    commandType: CommandType.StoredProcedure) ?? string.Empty;

                var (html, text) = BuildFormNotification(year, secureLink);

                result = await _mail.SendAsync(
                    toEmail: email,
                    toName: null,
                    subject: subject,
                    htmlBody: html,
                    plainTextBody: text);

                await _commRepo.LogEmailAsync(
                    employeeId, "Employee", "1095-Email",
                    string.IsNullOrWhiteSpace(email) ? "Unknown" : email,
                    0, subject, text, result.Status);

                if (!result.Success)
                {
                    _logger.LogWarning(
                        "1095-C notification not delivered for EmployeeId {EmployeeId}: {Status} - {Detail}",
                        employeeId, result.Status, result.Detail);
                }

                return result;
            }
            catch (Exception ex)
            {
                // Reaching here means the lookup or the logging failed, not the send.
                _logger.LogError(ex, "Error occurred in SendEmployeeFormEmailAsync for EmployeeId: {EmployeeId}", employeeId);

                result = MailDeliveryResult.Failed($"Internal error: {ex.Message}");

                try
                {
                    await _commRepo.LogEmailAsync(
                        employeeId, "Employee", "1095-Email",
                        string.IsNullOrWhiteSpace(email) ? "Unknown" : email,
                        0, subject, result.Detail ?? "", result.Status);
                }
                catch { /* the original fault is what matters; do not mask it */ }

                return result;
            }
        }

        /// <summary>
        /// Kept for callers that only want the audit row. Delegates to the sending path
        /// so the two cannot drift apart and quietly stop agreeing about what was sent.
        /// </summary>
        public async Task LogEmailSentAsync(int employeeId, int year, string secureLink)
            => await SendEmployeeFormEmailAsync(employeeId, year, secureLink);

        /// <summary>
        /// Builds the HTML and plain-text bodies. Both are supplied: an HTML-only message
        /// scores worse with spam filters, and this one carries a link the employee has to
        /// trust enough to click.
        /// </summary>
        private static (string Html, string Text) BuildFormNotification(int year, string secureLink)
        {
            var safeLink = System.Net.WebUtility.HtmlEncode(secureLink);

            var html =
$@"<!doctype html><html><body style=""font-family:Segoe UI,Arial,sans-serif;font-size:15px;color:#222;line-height:1.5"">
  <p>Your Form 1095-C for {year} is ready.</p>
  <p>This form reports the health coverage your employer offered you. Keep it with your tax records.</p>
  <p style=""margin:24px 0"">
    <a href=""{safeLink}"" style=""background:#1B5B78;color:#fff;padding:11px 20px;border-radius:4px;text-decoration:none"">
      View my 1095-C
    </a>
  </p>
  <p>You will be asked for the last four digits of your Social Security Number to confirm it is you.</p>
  <p style=""color:#666;font-size:13px"">If the button does not work, copy this address into your browser:<br>{safeLink}</p>
  <p style=""color:#666;font-size:13px"">This link expires in 90 days. If it stops working, ask your employer to send a new one.</p>
</body></html>";

            var text =
$@"Your Form 1095-C for {year} is ready.

This form reports the health coverage your employer offered you. Keep it with your tax records.

View your form:
{secureLink}

You will be asked for the last four digits of your Social Security Number to confirm it is you.

This link expires in 90 days. If it stops working, ask your employer to send a new one.";

            return (html, text);
        }

        /// <summary>
        /// Purpose: Processes a batch of employees, generating links and logging the emails sent.
        /// Input parameters: int employerId, int year, List of int employeeIds
        /// Output/return value: Task
        /// </summary>
        public async Task ProcessBatchEmailDistributionAsync(int employerId, int year, List<int> employeeIds)
        {
            int sent = 0, failed = 0;

            if (!_mail.IsConfigured)
            {
                // Say so once, loudly, rather than logging several hundred identical
                // "Disabled" rows and leaving someone to work out why nothing arrived.
                _logger.LogError(
                    "Batch distribution for EmployerId {EmployerId}, year {Year}: no mail provider is configured, " +
                    "so none of the {Count} messages can be delivered. Set SendGrid:ApiKey and SendGrid:FromEmail.",
                    employerId, year, employeeIds?.Count ?? 0);
            }

            foreach (var empId in employeeIds)
            {
                try
                {
                    string token = await GenerateSecureLinkAsync(empId, year);

                    string baseUrl = await _settings.GetSettingValueAsync("AppBaseUrl") ?? "https://localhost:7224";
                    string link = $"{baseUrl}/MyForms/Access?token={token}";

                    // Sends and records the real outcome. Returns rather than throws on a
                    // delivery failure, so one bad address cannot end the run.
                    var result = await SendEmployeeFormEmailAsync(empId, year, link);

                    if (result.Success) sent++; else failed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Error occurred in ProcessBatchEmailDistributionAsync for EmployeeId: {EmployeeId}", empId);
                    // Continue loop even if one fails
                }
            }

            // Hangfire shows the job as Succeeded whatever happened inside, so the only
            // record of how the run actually went is this line and the per-employee rows
            // in CommunicationLog.
            _logger.LogInformation(
                "Batch distribution finished for EmployerId {EmployerId}, year {Year}: {Sent} sent, {Failed} failed of {Total}.",
                employerId, year, sent, failed, employeeIds?.Count ?? 0);
        }
    }
}