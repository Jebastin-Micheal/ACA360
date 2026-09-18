using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using ACA360.Data.Helpers;
using ACA360.Logging.Interfaces;
using ACA360.Web.Models;
using BCrypt.Net;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;


namespace ACA360.BusinessLogic.Services
{
    public class AccountService : IAccountService
    {
        private readonly ILoggerService _logger;
        private readonly string _connectionString;

        /// <summary>
        /// Purpose: Initializes the AccountService with required dependencies.
        /// Input parameters: DbHelper dbHelper, ILoggerService logger, IConfiguration config
        /// Output/return value: None
        /// </summary>
        public AccountService(string connectionString, ILoggerService logger)
        {
            try
            {
                _connectionString = connectionString;
                _logger = logger;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "AccountService.Constructor", "Error", "Initialization failed", "System");
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Authenticates a user with BCrypt verification and auto-migrates legacy passwords.
        /// Input parameters: string username, string password
        /// Output/return value: Task of UserModel (or null if authentication fails)
        /// </summary>
        public async Task<UserModel?> LoginAsync(string username, string password)
        {
            UserModel? user = null;
            string storedPasswordHash = string.Empty;
            string storedTempPassword = string.Empty;

            try
            {
                using var db = Connection;

                // Execute the stored procedure and grab multiple result sets
                using var multi = await db.QueryMultipleAsync(
                    "sp_Login_access",
                    new { uname = username },
                    commandType: CommandType.StoredProcedure);

                // A. Read User Info (using dynamic to handle varying column names/types)
                var userRecord = await multi.ReadFirstOrDefaultAsync<dynamic>();

                if (userRecord != null)
                {
                    storedPasswordHash = userRecord.User_Password?.ToString() ?? "";
                    storedTempPassword = userRecord.Temp_Password?.ToString() ?? "";

                    // FIX — DateTimeOffset safe handling
                    DateTimeOffset? lockoutEnd = null;
                    try
                    {
                        var lockoutRaw = userRecord.LockoutEnd;
                        if (lockoutRaw != null)
                        {
                            // SQL DateTimeOffset → C# DateTimeOffset
                            if (lockoutRaw is DateTimeOffset dto)
                                lockoutEnd = dto;
                            else
                                lockoutEnd = new DateTimeOffset(Convert.ToDateTime((object)lockoutRaw),
                                    TimeSpan.Zero);
                        }
                    }
                    catch { lockoutEnd = null; }

                    if (lockoutEnd.HasValue && lockoutEnd.Value > DateTimeOffset.UtcNow)
                    {
                        return new UserModel { IsLockedOut = true };
                    }


                    user = new UserModel
                    {
                        // Dapper safely maps SQL NULL to C# null, so we can use standard null-conditional logic
                        User_ID = userRecord.User_ID?.ToString() ?? "0",
                        User_Name = userRecord.User_Name,
                        Role_ID = userRecord.Role_ID != null ? Convert.ToInt32(userRecord.Role_ID) : 0,
                        Flag = userRecord.Flag != null ? Convert.ToInt32(userRecord.Flag) : 0,
                        Ref_ID = userRecord.Ref_ID != null ? Convert.ToInt64(userRecord.Ref_ID) : 0,
                        Profile_Picture = userRecord.Profile_Picture,
                        IsMFA = userRecord.isMFA != null ? Convert.ToInt32(userRecord.isMFA) : (int?)null,
                        User_LandingPage = userRecord.User_LandingPage != null ? Convert.ToInt32(userRecord.User_LandingPage) : (int?)null,
                        CreatedOn = userRecord.CreatedOn != null ? Convert.ToDateTime(userRecord.CreatedOn) : (DateTime?)null,
                        Role_Name = userRecord.Role_Name,
                        Permissions = new List<PermissionModel>(),
                        PasswordHash = userRecord.PasswordHash,
                        Temp_Password = userRecord.Temp_Password,
                        Profile_Name = userRecord.Profile_Name
                    };
                }

                // B. Read Permissions
                if (user != null && !multi.IsConsumed)
                {
                    // Dapper automatically maps columns to the PermissionModel properties!
                    var permissions = await multi.ReadAsync<PermissionModel>();
                    user.Permissions = permissions.ToList();
                }

                // C. Verify Password
                if (user != null)
                {
                    bool isValid = false;

                    if (!string.IsNullOrEmpty(storedPasswordHash) && storedPasswordHash.StartsWith("$2"))
                    {
                        // Layer 1: BCrypt verify
                        bool bcryptValid = BCrypt.Net.BCrypt.Verify(password, storedPasswordHash);

                        if (bcryptValid)
                        {
                            // Layer 2: PasswordHash verify (if exists)
                            string? storedPHash = userRecord.PasswordHash?.ToString();
                            string? storedSStamp = userRecord.SecurityStamp?.ToString();
                            string? userId = userRecord.User_ID?.ToString();

                            if (!string.IsNullOrEmpty(storedPHash) &&
                                !string.IsNullOrEmpty(storedSStamp) &&
                                !string.IsNullOrEmpty(userId))
                            {
                                // Recompute and compare
                                string computedPHash = ComputePasswordHash(password, userId, storedSStamp);
                                isValid = computedPHash == storedPHash;
                            }
                            else
                            {
                                // PasswordHash not set yet — BCrypt alone enough
                                // (old users who haven't reset password yet)
                                isValid = true;
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(storedTempPassword) && storedTempPassword == password)
                    {
                        isValid = true;
                        user.RequirePasswordReset = true;
                    }

                    if (isValid)
                    {
                        await db.ExecuteAsync("sp_ResetFailedLogin",
                            new { Username = username },
                            commandType: CommandType.StoredProcedure);
                        return user;
                    }
                    else
                    {
                        await db.ExecuteAsync("sp_IncrementFailedLogin",
                            new { Username = username },
                            commandType: CommandType.StoredProcedure);
                        return null;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AccountService.LoginAsync", "Error", $"Login attempt for: {username}", "System");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Sets a new permanent password for a user (first-time login / temp password flow).
        /// Hashes the new password using BCrypt, saves to User_Password, clears Temp_Password.
        /// Input parameters: string username, string newPassword
        /// Output/return value: Task of bool (true = success, false = user not found)
        /// </summary>
        public async Task<bool> SetNewPasswordAsync(string username, string newPassword)
        {
            try
            {
                // Step 1: Get UserId first
                using var db = Connection;
                db.Open();

                var userId = await db.QueryFirstOrDefaultAsync<string>(
                    "SELECT User_ID FROM tbl_User WHERE User_Name = @Username",
                    new { Username = username });

                if (string.IsNullOrEmpty(userId)) return false;

                // Step 2: Generate all 4 values
                string securityStamp = Guid.NewGuid().ToString();
                string bcryptHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                string passwordHash = ComputePasswordHash(newPassword, userId, securityStamp);

                using var tx = db.BeginTransaction();
                try
                {
                    int rows = await db.ExecuteAsync(
                        "sp_SetNewPassword",
                        new
                        {
                            Username = username,
                            Hash = bcryptHash,               // User_Password
                            PHash = passwordHash,            // PasswordHash
                            SStamp = securityStamp           // SecurityStamp
                        },
                        transaction: tx,
                        commandType: CommandType.StoredProcedure);

                    tx.Commit();
                    return rows > 0;
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    _logger.LogError(ex, "AccountService.SetNewPasswordAsync",
                        "Error", "Password set failed", "System");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AccountService.SetNewPasswordAsync",
                    "Error", $"SetNewPassword failed for: {username}", "System");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves the full profile model for a user including role-specific details.
        /// Input parameters: string userId
        /// Output/return value: Task of UserProfileViewModel
        /// </summary>
        public async Task<UserProfileViewModel?> GetUserProfileAsync(string userId)
        {
            try
            {
                using var db = Connection;

                var user = await db.QueryFirstOrDefaultAsync<dynamic>(
                    "sp_GetUserProfileBase",
                    new { Id = userId },
                    commandType: CommandType.StoredProcedure);

                if (user == null) return null;

                var model = new UserProfileViewModel
                {
                    UserId = user.User_ID == null ? 0 : user.User_ID.ToString(),
                    UserName = user.User_Name,
                    RoleName = user.Role_Name,
                    RoleId = user.Role_ID == null ? 0 : Convert.ToInt32(user.Role_ID),
                    RefId = user.Ref_ID == null ? 0 : Convert.ToInt32(user.Ref_ID),
                    ProfilePicture = user.Profile_Picture,
                    IsMFAEnabled = (user.isMFA != null && Convert.ToInt32(user.isMFA) == 1)
                };

                // Step B: Get Details based on Role
                string storedProcName = "";
                string prefix = "";

                if (model.RefId > 0)
                {
                    
                    if (model.RoleId == 13)
                    {
                        storedProcName = "sp_GetEmployerAccountDetails";
                        prefix = "Employer_";
                    }
                    else if (model.RoleId == 10)
                    {
                        storedProcName = "sp_GetBrokerAccountDetails";
                        prefix = "Broker_";
                    }
                    else 
                    {
                        storedProcName = "sp_GetStaffAccountDetails";
                        prefix = "Staff_";
                    }
                    if (!string.IsNullOrEmpty(storedProcName))
                    {
                        var details = await db.QueryFirstOrDefaultAsync<dynamic>(
                            storedProcName,
                            new { RefId = model.RefId },
                            commandType: CommandType.StoredProcedure);

                        if (details != null)
                        {
                            MapDetails(model, details, prefix);
                        }
                    }
                }
                model.ProfileHealthPercentage = CalculateProfileHealth(model);
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AccountService.GetUserProfileAsync", "Error", $"Profile fetch failed for ID: {userId}", "System");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Maps dynamic dictionary data to a strongly-typed UserProfileViewModel.
        /// Input parameters: UserProfileViewModel model, dynamic data, string prefix
        /// Output/return value: void
        /// </summary>
        private void MapDetails(UserProfileViewModel model, dynamic data, string prefix)
        {
            try
            {
                var d = (IDictionary<string, object>)data;
                model.FirstName = d.ContainsKey(prefix + "FName") ? d[prefix + "FName"]?.ToString() : "";
                model.LastName = d.ContainsKey(prefix + "LName") ? d[prefix + "LName"]?.ToString() : "";
                model.Email = d.ContainsKey(prefix + "Email") ? d[prefix + "Email"]?.ToString() : "";
                model.PhoneNumber = d.ContainsKey(prefix + "Phone") ? d[prefix + "Phone"]?.ToString() : "";
                model.Address1 = d.ContainsKey(prefix + "Addr1") ? d[prefix + "Addr1"]?.ToString() : "";
                model.Address2 = d.ContainsKey(prefix + "Addr2") ? d[prefix + "Addr2"]?.ToString() : "";
                model.City = d.ContainsKey(prefix + "City") ? d[prefix + "City"]?.ToString() : "";
                model.State = d.ContainsKey(prefix + "State") ? d[prefix + "State"]?.ToString() : "";
                model.ZipCode = d.ContainsKey(prefix + "Zip") ? d[prefix + "Zip"]?.ToString() : "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AccountService.MapDetails", "Error", "Mapping profile details failed", "System");
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

        private int CalculateProfileHealth(UserProfileViewModel model)
        {
            int totalEssentialFields = 8;
            int filledFields = 0;

            if (!string.IsNullOrWhiteSpace(model.FirstName)) filledFields++;
            if (!string.IsNullOrWhiteSpace(model.LastName)) filledFields++;
            if (!string.IsNullOrWhiteSpace(model.Email)) filledFields++;
            if (!string.IsNullOrWhiteSpace(model.PhoneNumber)) filledFields++;
            if (!string.IsNullOrWhiteSpace(model.Address1)) filledFields++;
            if (!string.IsNullOrWhiteSpace(model.City)) filledFields++;
            if (!string.IsNullOrWhiteSpace(model.State)) filledFields++;
            if (!string.IsNullOrWhiteSpace(model.ZipCode)) filledFields++;

            // Note: Address2 is intentionally omitted as it is typically optional.

            // Prevent divide-by-zero just in case, though totalEssentialFields is hardcoded
            if (totalEssentialFields == 0) return 0;

            return (int)Math.Round((double)filledFields / totalEssentialFields * 100);
        }

    }
}