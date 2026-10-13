using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IUISettingsService
    {
        Task<UISettingsModel?> GetSettings();
        Task SaveSettings(UISettingsModel settings, string user);

    }
}
