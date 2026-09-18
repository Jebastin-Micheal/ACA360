using ACA360.Core.Models;
using Dapper;
using Microsoft.AspNetCore.Identity;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace ACA360.Security.Stores
{
    public class DapperUserStore : 
        IUserStore<UserModel>, 
        IUserPasswordStore<UserModel>, 
        IUserSecurityStampStore<UserModel>, 
        IUserLockoutStore<UserModel>
    {
        private readonly IDbConnection _connection;

        public DapperUserStore(IDbConnection connection)
        {
            _connection = connection;
        }

        public async Task<IdentityResult> CreateAsync(UserModel user, CancellationToken cancellationToken)
        {
            var sql = @"
                INSERT INTO dbo.tbl_User (User_Name, User_Password, Role_ID, Flag, Ref_ID, Profile_Picture, isMFA, User_LandingPage, CreatedOn, PasswordHash, SecurityStamp, AccessFailedCount, LockoutEnd, LockoutEnabled)
                VALUES (@User_Name, @User_Password, @RoleID, @Flag, @RefID, @Profile_Picture, @IsMFA, @User_LandingPage, @CreatedOn, @PasswordHash, @SecurityStamp, @AccessFailedCount, @LockoutEnd, @LockoutEnabled);
                SELECT CAST(SCOPE_IDENTITY() as bigint);";

            user.CreatedOn = DateTime.UtcNow;
            var id = await _connection.QuerySingleOrDefaultAsync<long>(sql, user);
            user.User_ID = id.ToString();
            return IdentityResult.Success;
        }

        public async Task<IdentityResult> DeleteAsync(UserModel user, CancellationToken cancellationToken)
        {
            var sql = "DELETE FROM dbo.tbl_User WHERE User_ID = @Id";
            await _connection.ExecuteAsync(sql, new { Id = int.Parse(user.User_ID) });
            return IdentityResult.Success;
        }

        public void Dispose()
        {
        }

        public async Task<UserModel> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            if (!int.TryParse(userId, out int id)) return null;
            var sql = "SELECT * FROM dbo.tbl_User WHERE User_ID = @Id";
            return await _connection.QuerySingleOrDefaultAsync<UserModel>(sql, new { Id = id });
        }

        public async Task<UserModel> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            var sql = "SELECT * FROM dbo.tbl_User WHERE UPPER(User_Name) = @Name";
            return await _connection.QuerySingleOrDefaultAsync<UserModel>(sql, new { Name = normalizedUserName });
        }

        public Task<string> GetNormalizedUserNameAsync(UserModel user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.User_Name?.ToUpper());
        }

        public Task<string> GetUserIdAsync(UserModel user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.User_ID?.ToString());
        }

        public Task<string> GetUserNameAsync(UserModel user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.User_Name);
        }

        public Task SetNormalizedUserNameAsync(UserModel user, string normalizedName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SetUserNameAsync(UserModel user, string userName, CancellationToken cancellationToken)
        {
            user.User_Name = userName;
            return Task.CompletedTask;
        }

        public async Task<IdentityResult> UpdateAsync(UserModel user, CancellationToken cancellationToken)
        {
            var sql = @"
                UPDATE dbo.tbl_User SET
                    User_Name = @User_Name,
                    PasswordHash = @PasswordHash,
                    SecurityStamp = @SecurityStamp,
                    AccessFailedCount = @AccessFailedCount,
                    LockoutEnd = @LockoutEnd,
                    LockoutEnabled = @LockoutEnabled,
                    User_Password = @User_Password
                WHERE User_ID = @Id";
            await _connection.ExecuteAsync(sql, new 
            { 
                user.User_Name, 
                user.PasswordHash,
                user.SecurityStamp,
                user.AccessFailedCount,
                user.LockoutEnd,
                user.LockoutEnabled,
                user.User_Password,
                Id = int.Parse(user.User_ID) 
            });
            return IdentityResult.Success;
        }

        // IUserPasswordStore
        public Task<string> GetPasswordHashAsync(UserModel user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.PasswordHash);
        }

        public Task<bool> HasPasswordAsync(UserModel user, CancellationToken cancellationToken)
        {
            return Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash) || !string.IsNullOrEmpty(user.User_Password));
        }

        public Task SetPasswordHashAsync(UserModel user, string passwordHash, CancellationToken cancellationToken)
        {
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        // IUserSecurityStampStore
        public Task<string> GetSecurityStampAsync(UserModel user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.SecurityStamp);
        }

        public Task SetSecurityStampAsync(UserModel user, string stamp, CancellationToken cancellationToken)
        {
            user.SecurityStamp = stamp;
            return Task.CompletedTask;
        }

        // IUserLockoutStore
        public Task<int> GetAccessFailedCountAsync(UserModel user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.AccessFailedCount);
        }

        public Task<bool> GetLockoutEnabledAsync(UserModel user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.LockoutEnabled);
        }

        public Task<DateTimeOffset?> GetLockoutEndDateAsync(UserModel user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.LockoutEnd);
        }

        public Task<int> IncrementAccessFailedCountAsync(UserModel user, CancellationToken cancellationToken)
        {
            user.AccessFailedCount++;
            return Task.FromResult(user.AccessFailedCount);
        }

        public Task ResetAccessFailedCountAsync(UserModel user, CancellationToken cancellationToken)
        {
            user.AccessFailedCount = 0;
            return Task.CompletedTask;
        }

        public Task SetLockoutEnabledAsync(UserModel user, bool enabled, CancellationToken cancellationToken)
        {
            user.LockoutEnabled = enabled;
            return Task.CompletedTask;
        }

        public Task SetLockoutEndDateAsync(UserModel user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            user.LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }
    }
}
