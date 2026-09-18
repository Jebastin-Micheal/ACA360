using System.IdentityModel.Tokens.Jwt;

namespace ACA360.Security.Authentication
{
    public static class JwtHelper
    {
        public static bool IsTokenExpired(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            return jwtToken.ValidTo < DateTime.UtcNow;
        }
    }
}
