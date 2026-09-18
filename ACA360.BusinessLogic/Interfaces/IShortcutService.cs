using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACA360.Core.Models;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IShortcutService
    {
        Task<List<UserShortcutItem>> GetUserShortcuts(long userId);
        Task<List<UserShortcutItem>> GetAvailableShortcutMenus(long userId);
        Task<bool> AddUserShortcut(long userId, int menuId);
        Task RemoveUserShortcut(long userId, int menuId);
        Task ReorderUserShortcuts(long userId, List<int> orderedMenuIds);
    }
}
