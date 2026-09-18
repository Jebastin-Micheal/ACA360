using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACA360.Core.Models.Tracker;

namespace ACA360.Repositories.Interfaces
{
    public interface ISalesRepRepository
    {
        Task<(IEnumerable<SalesRepItem> List, int TotalCount)> GetSalesRepListAsync(string search, string sortCol, string sortOrder, int page, int pageSize);
        Task<SalesRepItem> GetSalesRepByIdAsync(int id);
        Task AddUpdateSalesRepAsync(SalesRepItem salesRep);
        Task DeleteSalesRepAsync(int id);
    }
}