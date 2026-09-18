using System.Security.Claims;

namespace ACA360.Security.Interfaces
{
    public interface ITokenService
    {
        string GenerateToken(string username, IList<string> roles);
        ClaimsPrincipal ValidateToken(string token);
    }
}
