using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IFilingYearService
    {
        Task<List<FilingYearModel>> GetAllFilingYears();        
    }
}
