using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using ACA360.Data.Helpers;
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
namespace ACA360.BusinessLogic.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly ILogger<SettingsService> _logger;
        private readonly string _connectionString;

        /// <summary>
        /// Purpose: Initializes the SettingsService with required dependencies.
        /// Input parameters: DbHelper dbHelper, ILogger logger, IConfiguration config
        /// Output/return value: None
        /// </summary>
        public SettingsService(ILogger<SettingsService> logger, IConfiguration config)
        {
            try
            {

                _logger = logger;
                _connectionString = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(
                        "ConnectionStrings:DefaultConnection is missing from configuration."); ;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during initialization of SettingsService.");
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves the user profile header and detailed account information.
        /// Input parameters: string userId
        /// Output/return value: Task of UserProfileViewModel
        /// </summary>
        public async Task<UserProfileViewModel> GetUserProfileAsync(string userId)
        {
            try
            {
                using var db = Connection;

                var user = await db.QueryFirstOrDefaultAsync<dynamic>(
                    "sp_GetUserProfileHeader",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure);

                if (user == null) return null;

                var model = new UserProfileViewModel
                {
                    UserId = user.User_ID == null ? 0.ToString() : user.User_ID.ToString(),
                    UserName = user.User_Name,
                    RoleId = user.Role_ID == null ? 0 : Convert.ToInt32(user.Role_ID),
                    RoleName = user.RoleName,
                    RefId = user.Ref_ID == null ? 0 : Convert.ToInt32(user.Ref_ID),
                    ProfilePicture = user.Profile_Picture,
                    IsMFAEnabled = (user.isMFA != null && Convert.ToInt32(user.isMFA) == 1)
                };

                if (model.RefId > 0)
                {
                    var details = await db.QueryFirstOrDefaultAsync<dynamic>(
                        "sp_GetAccountDetails",
                        new { RoleId = model.RoleId, RefId = model.RefId },
                        commandType: CommandType.StoredProcedure);

                    if (details != null)
                    {
                        MapDetails(model, details);
                    }
                }

                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetUserProfileAsync for UserId: {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Maps dynamic dictionary data to a strongly-typed UserProfileViewModel.
        /// Input parameters: UserProfileViewModel model, dynamic data
        /// Output/return value: void
        /// </summary>
        private void MapDetails(UserProfileViewModel model, dynamic data)
        {
            try
            {
                var d = (IDictionary<string, object>)data;
                model.FirstName = d.ContainsKey("FName") ? d["FName"]?.ToString() : "";
                model.LastName = d.ContainsKey("LName") ? d["LName"]?.ToString() : "";
                model.Email = d.ContainsKey("Email") ? d["Email"]?.ToString() : "";
                model.PhoneNumber = d.ContainsKey("Phone") ? d["Phone"]?.ToString() : "";
                model.Address1 = d.ContainsKey("Addr1") ? d["Addr1"]?.ToString() : "";
                model.Address2 = d.ContainsKey("Addr2") ? d["Addr2"]?.ToString() : "";
                model.City = d.ContainsKey("City") ? d["City"]?.ToString() : "";
                model.State = d.ContainsKey("State") ? d["State"]?.ToString() : "";
                model.ZipCode = d.ContainsKey("Zip") ? d["Zip"]?.ToString() : "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in MapDetails.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Deactivates a specific user in the system.
        /// Input parameters: string userId, int roleId, int refId
        /// Output/return value: Task
        /// </summary>
        public async Task DeactivateUserAsync(string userId, int roleId, int refId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_DeactivateUser",
                    new { UserId = userId, RoleId = roleId, RefId = refId },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in DeactivateUserAsync for UserId: {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Updates user profile details and profile picture.
        /// Input parameters: UserProfileViewModel model
        /// Output/return value: Task
        /// </summary>
        public async Task UpdateUserProfileAsync(UserProfileViewModel model)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync("sp_UpdateAccountDetails", new
                {
                    RoleId = model.RoleId,
                    RefId = model.RefId,
                    Fn = model.FirstName,
                    Ln = model.LastName,
                    Em = model.Email,
                    Ph = model.PhoneNumber,
                    A1 = model.Address1,
                    A2 = model.Address2,
                    Ci = model.City,
                    St = model.State,
                    Zi = model.ZipCode
                },
                transaction: tx,
                commandType: CommandType.StoredProcedure);

                // Update Profile Picture
                if (!string.IsNullOrEmpty(model.ProfilePicture))
                {
                    await db.ExecuteAsync(
                        "sp_UpdateProfilePicture",
                        new { UserId = model.UserId, ProfilePicturePath = model.ProfilePicture },
                        transaction: tx,
                        commandType: CommandType.StoredProcedure);
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in UpdateUserProfileAsync for UserId: {UserId}", model?.UserId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Authenticates current password and updates to a new hashed password.
        /// Input parameters: string userId, string oldPassword, string newPassword
        /// Output/return value: Task of bool (true if successful, false if old password invalid)
        /// </summary>
        public async Task<bool> ChangePasswordAsync(string userId, string oldPassword, string newPassword)
        {
            try
            {
                using var db = Connection;
                var userRow = await db.QueryFirstOrDefaultAsync<dynamic>(
                    "sp_GetUserPasswordHash",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure);

                if (userRow == null)
                {
                    _logger.LogInformation("No user row found for UserId: {UserId}", userId);
                    return false;
                }

                string storedHash = userRow.User_Password?.ToString() ?? "";
                string storedTempPassword = userRow.Temp_Password?.ToString() ?? "";

                _logger.LogInformation("User_Password present: {A}, Temp_Password present: {B}",
                    !string.IsNullOrEmpty(storedHash), !string.IsNullOrEmpty(storedTempPassword));

                bool isOldPasswordValid = false;

                _logger.LogWarning("DEBUG - Attempt at {Time} for UserId {UserId} | Incoming password length: {Len} | Stored hash: {Hash}",
    DateTime.Now.ToString("HH:mm:ss.fff"), userId, oldPassword?.Length ?? 0, storedHash);


                if (!string.IsNullOrEmpty(storedHash) && storedHash.StartsWith("$2"))
                {
                    isOldPasswordValid = BCrypt.Net.BCrypt.Verify(oldPassword, storedHash);
                    _logger.LogWarning("DEBUG - BCrypt.Verify result: {Result}", isOldPasswordValid);
                }
                else if (!string.IsNullOrEmpty(storedTempPassword))
                {
                    isOldPasswordValid = (storedTempPassword == oldPassword);
                }

                if (!isOldPasswordValid)
                {
                    _logger.LogInformation("Old password verification FAILED for UserId: {UserId}", userId);
                    return false;
                }

                string securityStamp = Guid.NewGuid().ToString();
                string bcryptHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                string passwordHash = ComputePasswordHash(newPassword, userId, securityStamp);

                db.Open();
                using var tx = db.BeginTransaction();
                try
                {
                    int rows = await db.ExecuteAsync(
                        "sp_UpdateUserPassword",
                        new
                        {
                            UserId = userId,
                            Hash = bcryptHash,
                            PHash = passwordHash,
                            SStamp = securityStamp
                        },
                        transaction: tx,
                        commandType: CommandType.StoredProcedure);

                    _logger.LogInformation("Rows affected: {Rows}", rows);
                    tx.Commit();
                    return rows > 0;
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    _logger.LogError(ex, "Database error occurred executing password update transaction for UserId: {UserId}", userId);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in ChangePasswordAsync for UserId: {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Enables or disables Multi-Factor Authentication (MFA) for a user.
        /// Input parameters: string userId, bool enable
        /// Output/return value: Task
        /// </summary>
        public async Task UpdateMFAAsync(string userId, bool enable)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_UpdateUserMFA",
                    new { Status = enable ? 1 : 0, UserId = userId },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in UpdateMFAAsync for UserId: {UserId}", userId);
                throw;
            }
        }

        //Helper Method
        private string ComputePasswordHash(string password, string userId, string securityStamp)
        {
            // HMAC-SHA256: password + userId + securityStamp combine panni hash pannudhу
            string raw = $"{password}:{userId}:{securityStamp}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(securityStamp));
            byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToBase64String(hashBytes);
        }
    }
}