using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IUserRepository
    {
        Task<(List<UserModel>, PaginationViewEntity)> GetUserListAsync(PaginationEntity pagination);
        Task<List<UserSelectionModel>> GetUsersInRoleAsync(string roleName);
    }
}
