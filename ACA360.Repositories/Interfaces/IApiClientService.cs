using System.Threading.Tasks;
using ACA360.Core.Models; // Ensure you have a basic ApiClient model or use dynamic

namespace ACA360.Repositories.Interfaces
{
    public interface IApiClientService
    {
        Task<ApiClient?> ValidateClientAsync(string clientId, string clientSecret);
        Task<IEnumerable<ApiClientViewModel>> GetClientsForEmployerAsync(int employerId);
        Task<NewKeyResult> GenerateNewClientAsync(int employerId, string name);
        Task RevokeClientAsync(Guid clientId, int employerId);
    }
}