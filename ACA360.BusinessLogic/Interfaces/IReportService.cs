using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
      public interface IReportService
      {
        Task<List<Employer_ReportModel>> GetEmployerReport(string reportType);
        AcaReportViewModel GetAcaData(string taxId, string ssn, string year);
        Task<ExportMailFulfillmentViewModel> GetExportMailFulfillmentData(string employerTaxId, string filingYear, string ssn = null);
      }

}
