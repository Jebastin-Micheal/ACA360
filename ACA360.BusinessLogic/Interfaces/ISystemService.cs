using ACA360.Core.Models;
using System.IO;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface ISystemService
    {
        Task<object> RunHealthCheckAsync();
        Task<(bool success, string message)> TestSmtpConnectionAsync();
        Task<Stream> ExportAuditLogsToCsvAsync();
        Task<(bool success, string message)> ValidateIrsFormatAsync();
    }
}
