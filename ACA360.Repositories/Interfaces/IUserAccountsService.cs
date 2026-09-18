using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Repositories.Interfaces
{
    public interface IUserAccountsService
    {
        Task<(List<UserAccounts> UserAccountss, PaginationViewEntity PageInfo, List<RoleSummary> RoleStats)> GetUserAccountsList(PaginationEntity paginationEntity, int roleId, int statusId);

        Task<UserAccounts> GetUserAccountsByIdAsync(long id, int? roleId = null); // Add roleId parameter
        Task AddUserAccountsAsync(UserAccounts UserAccounts, string profilePictureFileName);
        Task UpdateUserAccountsAsync(UserAccounts UserAccounts, string profilePictureFileName);
        Task DeleteUserAccountsAsync(long id,int roleId);
        Task<List<SelectListItem>> GetAll_Roles();
        Task<List<UserSelectionModel>> GetUsersInRoleAsync(string roleName);
        Task<IEnumerable<StateModel>> GetAllStatesAsync();
    }
}


