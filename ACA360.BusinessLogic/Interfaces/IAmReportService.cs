using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IAmReportService
    {
        Task<List<AccountManagerModel>> GetAccountManagersAsync();
        Task<List<AmReportModel>> GetAmReportAsync(int acctManagerId, int? filingYear);
        Task<List<int>> GetFilingYearsAsync();
    }
}