using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Globalization;
using System.Net;

namespace ACA360.Web.Helpers
{
    public static class LocalTimeHtmlHelper
    {
        /// <summary>
        /// Renders a UTC timestamp as a &lt;time&gt; element. local-time.js rewrites it
        /// in the viewer's own time zone; without script it reads as UTC and says so.
        /// Only for values stored in UTC (DateTime.UtcNow / GETUTCDATE()).
        /// </summary>
        /// <param name="format">short: "Sep 22, 20:05" · long: "Sep 22, 2026 · 20:05" · seconds: "Sep 22 · 20:05:43" · time: "20:05:43"</param>
        public static IHtmlContent LocalTime(this IHtmlHelper html, DateTime utc, string format = "short")
        {
            var fallback = format switch
            {
                "long" => utc.ToString("MMM dd, yyyy · HH:mm", CultureInfo.InvariantCulture),
                "seconds" => utc.ToString("MMM dd · HH:mm:ss", CultureInfo.InvariantCulture),
                "time" => utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                _ => utc.ToString("MMM dd, HH:mm", CultureInfo.InvariantCulture)
            };
            var iso = utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "Z";

            return new HtmlString(
                $"<time class=\"js-local-time\" datetime=\"{iso}\" data-format=\"{WebUtility.HtmlEncode(format)}\">{WebUtility.HtmlEncode(fallback)} UTC</time>");
        }
    }
}
