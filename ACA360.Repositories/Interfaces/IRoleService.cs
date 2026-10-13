using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Repositories.Interfaces
{
    public interface IRoleService
    {
        Task<(List<Role> Roles, PaginationViewEntity PageInfo)> GetRoleList(PaginationEntity paginationEntity);
        Task<RoleFormDataDto> GetRoleFormData(int? id);
        Task AddOrUpdateRole(Role Role);
        Task DeleteRole(int id);        

    }
}


