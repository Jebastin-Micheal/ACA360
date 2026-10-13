using ACA360.Security.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace ACA360.Security.Encryption
{
    public class AesEncryptionService : IEncryptionService
    {
        private readonly byte[] key;

        public AesEncryptionService(string encryptionKey)
        {
            // Ensure Key is exactly 32 bytes (256 bits)
            using var sha256 = SHA256.Create();
            key = sha256.ComputeHash(Encoding.UTF8.GetBytes(encryptionKey));
        }

        // ACA360.Security/Encryption/AesEncryptionService.cs
        public string Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV(); // GENERATE RANDOM IV
            var iv = aes.IV;

            using var encryptor = aes.CreateEncryptor(aes.Key, iv);
            using var ms = new MemoryStream();
            // Prepend IV to the stream so we can read it back during decryption
            ms.Write(iv, 0, iv.Length);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        public string Decrypt(string encryptedText)
        {
            var fullCipher = Convert.FromBase64String(encryptedText);

            using var aes = Aes.Create();
            aes.Key = key;

            // Extract the IV from the first 16 bytes
            var iv = new byte[16];
            Array.Copy(fullCipher, 0, iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

            // Decrypt the rest
            using var ms = new MemoryStream(fullCipher, 16, fullCipher.Length - 16);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }
    }
}
