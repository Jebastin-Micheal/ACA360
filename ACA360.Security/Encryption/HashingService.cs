using System.Security.Cryptography;
using System.Text;

namespace ACA360.Security.Encryption
{
    public static class HashingService
    {
        public static string ComputeSHA256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder();
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
