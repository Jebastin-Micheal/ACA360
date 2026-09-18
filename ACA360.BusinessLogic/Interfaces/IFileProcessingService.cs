using ACA360.Core.Models;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IFileProcessingService
    {
        Task ProcessFileAsync(int fileLogId, int templateId);
        Task<AnalysisResultViewModel> AnalyzeImportAsync(int fileLogId);
        Task<ImportValidationSummaryModel> GetImportSummaryAsync(int fileLogId);
    }
}