using System;
using System.Text.RegularExpressions;

namespace ACA360.Web.Helpers
{
    public static class DataMasker
    {
        /// <summary>
        /// Masks a string while keeping a specified number of characters visible at the end or start.
        /// </summary>
        /// <param name="input">The raw string to mask.</param>
        /// <param name="visibleChars">Number of characters to leave visible.</param>
        /// <param name="maskChar">The character to use for masking (default '*').</param>
        /// <param name="fromStart">If true, keeps characters at START visible. If false (default), keeps characters at END visible.</param>
        /// <returns>The masked string.</returns>
        public static string MaskGeneric(string input, int visibleChars = 4, char maskChar = '*', bool fromStart = false)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Clean input (optional: remove hyphens if you want to mask raw numbers)
            // input = input.Replace("-", "").Replace(" ", ""); 

            if (input.Length <= visibleChars) return new string(maskChar, input.Length);

            int maskLength = input.Length - visibleChars;

            if (fromStart)
            {
                // Show Start: "1234********"
                return input.Substring(0, visibleChars) + new string(maskChar, maskLength);
            }
            else
            {
                // Show End: "********5678" (Standard for SSN/Credit Cards)
                return new string(maskChar, maskLength) + input.Substring(maskLength);
            }
        }

        /// <summary>
        /// specialized masker for US SSN (XXX-XX-1234)
        /// Maintains the hyphens for readability but hides the first 5 digits.
        /// </summary>
        public static string MaskSSN(string ssn)
        {
            if (string.IsNullOrEmpty(ssn)) return string.Empty;

            // Normalize: Remove non-digits to handle "123456789" or "123-45-6789"
            var cleanSSN = Regex.Replace(ssn, "[^0-9]", "");

            if (cleanSSN.Length != 9) return "Invalid SSN"; // Or return MaskGeneric(ssn)

            // Format: ***-**-1234
            return $"***-**-{cleanSSN.Substring(5, 4)}";
        }

        /// <summary>
        /// Specialized masker for Email (j*****@domain.com)
        /// </summary>
        public static string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@")) return email;

            var parts = email.Split('@');
            var localPart = parts[0];
            var domain = parts[1];

            if (localPart.Length <= 2)
            {
                return $"{localPart[0]}****@{domain}";
            }

            // Show first 2 chars, mask the rest
            return $"{localPart.Substring(0, 2)}{new string('*', localPart.Length - 2)}@{domain}";
        }

        /// <summary>
        /// Specialized masker for EIN (XX-XXX1234)
        /// Usually format is XX-xxxxxxx. We often show last 4.
        /// </summary>
        public static string MaskEIN(string ein)
        {
            if (string.IsNullOrEmpty(ein)) return string.Empty;

            // Normalize
            var cleanEin = Regex.Replace(ein, "[^0-9]", "");

            if (cleanEin.Length != 9) return MaskGeneric(ein);

            // Output: **-***1234
            return $"**-***{cleanEin.Substring(5, 4)}";
        }
        public static bool IsMaskedPlaceholder(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return Regex.IsMatch(value, @"^\*\*-\*\*\*\d{4}$")   // MaskEIN format
                || Regex.IsMatch(value, @"^\*\*\*-\*\*-\d{4}$"); // MaskSSN format
        }

    }
}