using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Validators
{
    public static class PasswordPolicyValidator
    {
        public static List<string> Validate(string? password, SecuritySettingsDto policy)
        {
            password ??= string.Empty;
            var errors = new List<string>();

            if (password.Length < policy.MinPasswordLength)
                errors.Add($"Password must be at least {policy.MinPasswordLength} characters long.");

            if (policy.RequireUppercase && !password.Any(char.IsUpper))
                errors.Add("Password must contain at least one uppercase letter.");

            if (policy.RequireDigits && !password.Any(char.IsDigit))
                errors.Add("Password must contain at least one number (0-9).");

            return errors;
        }
    }
}