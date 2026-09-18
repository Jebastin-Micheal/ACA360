using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IExportService
    {
        byte[] BuildCsv(ExportRequest request);

        byte[] BuildExcel(ExportRequest request);

        byte[] BuildPdf(ExportRequest request);

        string BuildPrintHtml(ExportRequest request);
    }
}