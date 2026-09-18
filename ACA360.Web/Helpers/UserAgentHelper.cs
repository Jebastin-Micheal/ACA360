// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using System.Text.RegularExpressions;

namespace ACA360.Web.Helpers
{
    /// <summary>
    /// Turns a raw User-Agent header into a friendly "Browser version / OS" label
    /// (e.g. "Chrome 125.0.0.0 / Windows 11") for the consent audit record.
    /// </summary>
    public static class UserAgentHelper
    {
        public static string Friendly(string? ua)
        {
            if (string.IsNullOrWhiteSpace(ua)) return "Unknown";

            string browser = "Unknown browser";
            Match m;

            // Browser — order matters (Edge/Opera also contain "Chrome").
            if ((m = Regex.Match(ua, @"Edg(?:A|iOS)?/([\d.]+)")).Success) browser = "Edge " + m.Groups[1].Value;
            else if ((m = Regex.Match(ua, @"OPR/([\d.]+)")).Success) browser = "Opera " + m.Groups[1].Value;
            else if ((m = Regex.Match(ua, @"Chrome/([\d.]+)")).Success) browser = "Chrome " + m.Groups[1].Value;
            else if ((m = Regex.Match(ua, @"Firefox/([\d.]+)")).Success) browser = "Firefox " + m.Groups[1].Value;
            else if ((m = Regex.Match(ua, @"Version/([\d.]+).*Safari")).Success) browser = "Safari " + m.Groups[1].Value;

            // Operating system.
            string os = "Unknown OS";
            if (Regex.IsMatch(ua, @"Windows NT 10")) os = "Windows 10/11";
            else if (Regex.IsMatch(ua, @"Windows NT 6\.3")) os = "Windows 8.1";
            else if (Regex.IsMatch(ua, @"Windows NT 6\.1")) os = "Windows 7";
            else if (ua.Contains("Windows")) os = "Windows";
            else if ((m = Regex.Match(ua, @"Android ([\d.]+)")).Success) os = "Android " + m.Groups[1].Value;
            else if ((m = Regex.Match(ua, @"(?:iPhone|CPU) OS ([\d_]+)")).Success) os = "iOS " + m.Groups[1].Value.Replace('_', '.');
            else if ((m = Regex.Match(ua, @"Mac OS X ([\d_]+)")).Success) os = "macOS " + m.Groups[1].Value.Replace('_', '.');
            else if (ua.Contains("Mac OS X")) os = "macOS";
            else if (ua.Contains("Linux")) os = "Linux";

            return $"{browser} / {os}";
        }
    }
}
