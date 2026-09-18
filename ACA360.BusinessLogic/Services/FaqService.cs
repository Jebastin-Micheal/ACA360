using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class FaqService : IFaqService
    {
        private readonly string _connectionString;

        public FaqService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<IEnumerable<FaqModel>> GetActiveFaqsAsync()
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                return await db.QueryAsync<FaqModel>("sp_GetActiveFaqs", commandType: CommandType.StoredProcedure);
            }
        }

        public async Task<IEnumerable<FaqModel>> GetAllFaqsAsync()
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                return await db.QueryAsync<FaqModel>("sp_GetAllFaqs", commandType: CommandType.StoredProcedure);
            }
        }

        public async Task<FaqModel> GetFaqByIdAsync(string id)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                return await db.QueryFirstOrDefaultAsync<FaqModel>(
                    "sp_GetFaqById",
                    new { Id = id },
                    commandType: CommandType.StoredProcedure);
            }
        }

        public async Task<bool> CreateFaqAsync(FaqModel faq)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                faq.Id = Guid.NewGuid().ToString();

                // Pass only the exact parameters the stored procedure expects
                var parameters = new
                {
                    Id = faq.Id,
                    CategoryId = faq.CategoryId,
                    Question = faq.Question,
                    Answer = faq.Answer,
                    SortOrder = faq.SortOrder,
                    IsActive = faq.IsActive,
                    CreatedDate = faq.CreatedDate // Ensure this is populated! (e.g., DateTime.UtcNow)
                };

                int rows = await db.ExecuteAsync(
                    "sp_CreateFaq",
                    parameters,
                    commandType: CommandType.StoredProcedure);

                return rows > 0;
            }
        }

        public async Task<bool> UpdateFaqAsync(FaqModel faq)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                int rows = await db.ExecuteAsync(
                    "sp_UpdateFaq",
                    new { faq.Id, faq.CategoryId, faq.Question, faq.Answer, faq.SortOrder, faq.IsActive },
                    commandType: CommandType.StoredProcedure);

                return rows > 0;
            }
        }

        public async Task<bool> DeleteFaqAsync(string id)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                int rows = await db.ExecuteAsync(
                    "sp_DeleteFaq",
                    new { Id = id },
                    commandType: CommandType.StoredProcedure);

                return rows > 0;
            }
        }

        public async Task<string> GetSystemSettingAsync(string key)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                return await db.QueryFirstOrDefaultAsync<string>(
                    "sp_GetSystemSetting",
                    new { Key = key },
                    commandType: CommandType.StoredProcedure);
            }
        }

        public async Task<bool> UpdateSystemSettingAsync(string key, string value)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                int rows = await db.ExecuteAsync(
                    "sp_UpsertSystemSetting",
                    new { Key = key, Value = value },
                    commandType: CommandType.StoredProcedure);

                return rows > 0;
            }
        }

        public async Task<FaqCategoryModel> CreateCategoryAsync(string name, string icon)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                var category = new FaqCategoryModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Icon = string.IsNullOrWhiteSpace(icon) ? "bx-folder" : icon
                };

                await db.ExecuteAsync(
                    "sp_CreateCategory",
                    new { category.Id, category.Name, category.Icon },
                    commandType: CommandType.StoredProcedure);

                return category;
            }
        }

        public async Task<IEnumerable<FaqCategoryModel>> GetAllCategoriesAsync()
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                return await db.QueryAsync<FaqCategoryModel>("sp_GetAllCategories", commandType: CommandType.StoredProcedure);
            }
        }

        public async Task<FaqCategoryModel> GetCategoryByIdAsync(string id)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                return await db.QueryFirstOrDefaultAsync<FaqCategoryModel>(
                    "sp_GetCategoryById",
                    new { Id = id },
                    commandType: CommandType.StoredProcedure);
            }
        }

        public async Task<bool> UpdateCategoryAsync(string id, string name, string icon)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                int rows = await db.ExecuteAsync(
                    "sp_UpdateCategory",
                    new { Id = id, Name = name, Icon = icon },
                    commandType: CommandType.StoredProcedure);

                return rows > 0;
            }
        }

        // Just for reference - this stays exactly as you have it now:
        public async Task<(bool Success, string Message)> DeleteCategoryAsync(string id)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                var result = await db.QueryFirstOrDefaultAsync<(bool Success, string Message)>(
                    "sp_DeleteCategory",
                    new { Id = id },
                    commandType: CommandType.StoredProcedure);

                return result != default ? result : (false, "An unexpected database error occurred.");
            }
        }
    }
}