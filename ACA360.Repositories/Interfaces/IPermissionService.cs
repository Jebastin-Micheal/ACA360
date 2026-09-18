using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IPermissionService
    {
        Task<RolePermissionViewModels> GetPermissionsByRoleAsync(int roleId);
        Task UpdatePermissionsAsync(int roleId, List<PermissionViewModel> permissions, string modifiedBy);
        //Task<List<Menu>> GetMenuWithActionsAsync();
        //Task<bool> HasPermissionAsync(string username, string controller, string action);

    } 
}
