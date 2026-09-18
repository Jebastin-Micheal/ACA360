using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACA360.Core.Models;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IDataAnalystReportService
    {
        Task<List<DataAnalystModel>> GetDataAnalystsAsync();
        Task<List<int>> GetFilingYearsAsync();
        Task<List<AmReportModel>> GetDataAnalystReportAsync(int dataAnalystId, int? filingYear);
    }
}