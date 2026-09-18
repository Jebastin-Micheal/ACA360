// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>
    /// Dapper + BCrypt implementation of the employee portal authentication service.
    /// Mirrors AccountService's temp-password → forced-reset pattern.
    /// </summary>
    public class EmployeePortalAuthService : IEmployeePortalAuthService
    {
        private readonly string _connectionString;
        private readonly ILogger<EmployeePortalAuthService> _logger;

        public EmployeePortalAuthService(string connString, ILogger<EmployeePortalAuthService> logger)
        {
            _connectionString = connString;
            _logger = logger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <inheritdoc />
        public async Task<string> ActivateAsync(int employeeId, int taxYear, string email, string createdBy)
        {
            try
            {
                var tempPassword = GenerateTempPassword();
                using var db = Connection;
                await db.ExecuteAsync(
                    "sp_ActivatePortalUser",
                    new { EmployeeId = employeeId, TaxYear = taxYear, Email = email, TempPassword = tempPassword, CreatedBy = createdBy },
                    commandType: CommandType.StoredProcedure);
                return tempPassword;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating portal user for EmployeeId {EmployeeId}", employeeId);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<EmployeePortalUser?> GetByEmailAsync(string email)
        {
            using var db = Connection;
            return await db.QueryFirstOrDefaultAsync<EmployeePortalUser>(
                "sp_GetPortalUserByEmail",
                new { Email = email },
                commandType: CommandType.StoredProcedure);
        }

        /// <inheritdoc />
        public async Task<PortalLoginResult> ValidateLoginAsync(string email, string password)
        {
            try
            {
                email = (email ?? string.Empty).Trim();
                var user = await GetByEmailAsync(email);

                if (user == null)
                    return new PortalLoginResult { Status = PortalLoginStatus.InvalidCredentials };

                if (!user.IsActive)
                    return new PortalLoginResult { Status = PortalLoginStatus.InactiveAccount };

                // First login: match the one-time temp password → force a reset.
                // Temp passwords are system-generated uppercase, so compare trimmed + case-insensitively.
                if (user.RequirePasswordReset || string.IsNullOrEmpty(user.PasswordHash))
                {
                    var typedTemp = (password ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(user.TempPassword) &&
                        string.Equals(user.TempPassword.Trim(), typedTemp, StringComparison.OrdinalIgnoreCase))
                        return new PortalLoginResult { Status = PortalLoginStatus.RequiresPasswordReset, User = user };

                    await IncrementFailedAsync(email);
                    return new PortalLoginResult { Status = PortalLoginStatus.InvalidCredentials };
                }

                // Normal login: BCrypt verify against the exact password the employee chose.
                if (BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                {
                    await ResetFailedAsync(email);
                    return new PortalLoginResult { Status = PortalLoginStatus.Success, User = user };
                }

                await IncrementFailedAsync(email);
                return new PortalLoginResult { Status = PortalLoginStatus.InvalidCredentials };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating portal login for {Email}", email);
                return new PortalLoginResult { Status = PortalLoginStatus.InvalidCredentials };
            }
        }

        /// <inheritdoc />
        public async Task SetPasswordAsync(string email, string newPassword)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            using var db = Connection;
            await db.ExecuteAsync(
                "sp_SetPortalPassword",
                new { Email = email, PasswordHash = hash },
                commandType: CommandType.StoredProcedure);
        }

        /// <inheritdoc />
        public async Task<bool> VerifyIdentityAsync(string email, string last4SSN, DateTime dateOfBirth)
        {
            try
            {
                using var db = Connection;
                var id = await db.QueryFirstOrDefaultAsync<int?>(
                    "sp_VerifyPortalIdentity",
                    new { Email = (email ?? string.Empty).Trim(), Last4SSN = (last4SSN ?? string.Empty).Trim(), Dob = dateOfBirth.Date },
                    commandType: CommandType.StoredProcedure);
                return id.HasValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in VerifyIdentityAsync for {Email}", email);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<int?> GetLatestFilingYearAsync()
        {
            try
            {
                using var db = Connection;
                return await db.ExecuteScalarAsync<int?>("SELECT MAX(filingYear) FROM dbo.FilingYear");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetLatestFilingYearAsync");
                return null;
            }
        }

        private async Task IncrementFailedAsync(string email)
        {
            using var db = Connection;
            await db.ExecuteAsync("sp_IncrementPortalFailedLogin", new { Email = email }, commandType: CommandType.StoredProcedure);
        }

        private async Task ResetFailedAsync(string email)
        {
            using var db = Connection;
            await db.ExecuteAsync("sp_ResetPortalFailedLogin", new { Email = email }, commandType: CommandType.StoredProcedure);
        }

        private static string GenerateTempPassword()
        {
            // Readable one-time password, e.g. "ACA-7F3K9Q".
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var rng = new Random();
            var buf = new char[6];
            for (int i = 0; i < buf.Length; i++) buf[i] = chars[rng.Next(chars.Length)];
            return "ACA-" + new string(buf);
        }
    }
}
