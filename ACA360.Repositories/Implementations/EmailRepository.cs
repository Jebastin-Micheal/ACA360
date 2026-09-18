using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class EmailRepository : IEmailRepository
    {
        private readonly string _connectionString;

        public EmailRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }



        public async Task<IEnumerable<EmailItem>> GetEmailsAsync(bool isDeleted, string userId)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            return await db.QueryAsync<EmailItem>("sp_GetEmails", new { IsDeleted = isDeleted, UserId = userId }, commandType: CommandType.StoredProcedure);
        }

        public async Task CreateEmailAsync(EmailItem email)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            var parameters = new DynamicParameters();
            parameters.Add("@Id", email.Id);
            parameters.Add("@UserId", email.UserId);
            parameters.Add("@SenderName", email.SenderName);
            parameters.Add("@SenderEmail", email.SenderEmail);
            parameters.Add("@Subject", email.Subject);
            parameters.Add("@Body", email.Body);
            parameters.Add("@LabelId", email.LabelId);
            parameters.Add("@ReceivedDate", email.ReceivedDate);

            await db.ExecuteAsync("sp_InsertEmail", parameters, commandType: CommandType.StoredProcedure);
        }

        public async Task MoveToTrashAsync(string id, bool isDeleted, string userId)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            await db.ExecuteAsync("UPDATE Inbox SET IsDeleted = @IsDeleted WHERE Id = @Id AND UserId = @UserId",
                new { Id = id, IsDeleted = isDeleted, UserId = userId });
        }

        public async Task ToggleStarAsync(string id, bool isStarred, string userId)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            await db.ExecuteAsync("UPDATE Inbox SET IsStarred = @IsStarred WHERE Id = @Id AND UserId = @UserId",
                new { Id = id, IsStarred = isStarred, UserId = userId });
        }

        public async Task ToggleReadAsync(string id, bool isRead, string userId)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            await db.ExecuteAsync("UPDATE Inbox SET IsRead = @IsRead WHERE Id = @Id AND UserId = @UserId",
                new { Id = id, IsRead = isRead, UserId = userId });
        }

        public async Task AssignLabelAsync(string id, string labelId, string userId)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            object dbLabelId = string.IsNullOrEmpty(labelId) ? DBNull.Value : labelId;
            await db.ExecuteAsync("UPDATE Inbox SET LabelId = @LabelId WHERE Id = @Id AND UserId = @UserId",
                new { Id = id, LabelId = dbLabelId, UserId = userId });
        }

        public async Task<IEnumerable<Label>> GetLabelsAsync(string userId)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            return await db.QueryAsync<Label>("SELECT * FROM Inbox_Label WHERE UserId = @UserId", new { UserId = userId });
        }

        public async Task CreateLabelAsync(Label label)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            await db.ExecuteAsync("sp_InsertLabel",
                new { Id = label.Id, UserId = label.UserId, Name = label.Name, ColorClass = label.ColorClass },
                commandType: CommandType.StoredProcedure);
        }

        public async Task UpdateLabelAsync(Label label)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            await db.ExecuteAsync("sp_UpdateLabel",
                new { Id = label.Id, UserId = label.UserId, Name = label.Name, ColorClass = label.ColorClass },
                commandType: CommandType.StoredProcedure);
        }

        public async Task DeleteLabelAsync(string id, string userId)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            await db.ExecuteAsync("sp_DeleteLabel", new { Id = id, UserId = userId }, commandType: CommandType.StoredProcedure);
        }




    }
}
