using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IRuleService
    {
        Task<(List<ValidationRule> Rules, PaginationViewEntity PageInfo)> GetRuleList(PaginationEntity paginationEntity);
        Task<ValidationRule> GetRuleByIdAsync(int id);
        Task AddRuleAsync(ValidationRule rule);
        Task UpdateRuleAsync(ValidationRule rule);
        Task DeleteRuleAsync(int id);
    }
}
