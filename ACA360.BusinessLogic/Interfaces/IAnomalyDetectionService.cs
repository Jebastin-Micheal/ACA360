using ACA360.Core.Models;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IAnomalyDetectionService
    {
        Task<DataHealthScorecard> AnalyzeEmployerDataAsync(int employerId, int year);
    }
}