using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IFaqService
    {
        Task<IEnumerable<FaqModel>> GetActiveFaqsAsync(); // Existing
        Task<IEnumerable<FaqModel>> GetAllFaqsAsync();
        Task<FaqModel> GetFaqByIdAsync(string id);
        Task<bool> CreateFaqAsync(FaqModel faq);
        Task<bool> UpdateFaqAsync(FaqModel faq);
        Task<bool> DeleteFaqAsync(string id);
        // FaqCategoryModel
        Task<IEnumerable<FaqCategoryModel>> GetAllCategoriesAsync();
        Task<FaqCategoryModel> CreateCategoryAsync(string name , string icon);
        Task<FaqCategoryModel> GetCategoryByIdAsync(string id);
        Task<bool> UpdateCategoryAsync(string id, string name, string icon);
        Task<(bool Success, string Message)> DeleteCategoryAsync(string id);
        // Add these to your existing IFaqService interface
        Task<string> GetSystemSettingAsync(string key);
        Task<bool> UpdateSystemSettingAsync(string key, string value);
    }
}
