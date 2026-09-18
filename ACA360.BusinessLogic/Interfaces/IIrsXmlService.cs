using ACA360.Core.Models;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IIrsXmlService
    {
        // 1. Core Action: Generates both XML files (Manifest + Data) for a batch
        Task<IrsSubmissionLog> GenerateSubmissionFilesAsync(int employerId, int year, string userId);

        // 2. Helper: Validates XML against IRS XSDs (Future Step)
        // Task<bool> ValidateXmlAsync(string filePath);
        // Add these methods
        Task<List<IrsDashboardItemDto>> GetIrsDashboardAsync(int year);
        Task<IrsSubmissionLog> GetSubmissionByIdAsync(int submissionId);
        Task UpdateStatusAsync(int submissionId, int statusId, string message);
        Task<bool> ProcessAckFileAsync(int submissionId, IFormFile file);
        Task<bool> ProcessErrorFileAsync(int submissionId, IFormFile file);
        Task<List<IrsSubmissionErrorDto>> GetSubmissionErrorsAsync(int submissionId);
        Task<IrsSubmissionLog> GenerateCorrectionFilesAsync(int originalSubmissionId, string userId);
        // Add this method
        Task<IEnumerable<IrsSubmissionLogDto>> GetSubmissionsForAmAsync(string amUserId, int year);
    }
}