using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Repositories.Interfaces
{
        public interface IMenuService
        {
        Task<(List<Menu> Menus, PaginationViewEntity PageInfo)> GetMenuList(PaginationEntity paginationEntity);
        Task<MenuFormDataDto> GetMenuFormData(int? id);
        Task AddOrUpdateMenu(Menu menu, int roleId, List<int> actionIds);
        Task SoftDeleteMenu(int id);
        Task<List<Menu>> GetMenuByUserId(string userId);
        Task<List<SelectListItem>> GetAll_Menu_Action();
        Task<List<ParentMenuDto>> GetAll_Parent_Menu();
    }
}


