using System;

namespace ACA360.Core.Extensions
{
    public static class AvatarExtensions
    {
        public static string GetInitials(this string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "A";
            // improved logic: handles "John Doe" -> "JD"
            var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
                return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpper();

            return name.Substring(0, 1).ToUpper();
        }

        public static string GetAvatarColor(this string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "bg-primary";
            char c = char.ToUpper(name[0]);

            return c switch
            {
                >= 'A' and <= 'D' => "bg-label-primary",   // Purple/Blue
                >= 'E' and <= 'H' => "bg-label-success",   // Green
                >= 'I' and <= 'L' => "bg-label-info",      // Cyan
                >= 'M' and <= 'P' => "bg-label-warning",   // Orange
                >= 'Q' and <= 'T' => "bg-label-danger",    // Red
                >= 'U' and <= 'Z' => "bg-label-secondary", // Grey
                _ => "bg-label-dark"
            };
        }
    }
}