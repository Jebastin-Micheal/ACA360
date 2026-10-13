using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Extensions;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Filters
{
    /// <summary>
    /// Puts the working context (filing-year list, selected plan year, selected employer)
    /// back into session when a signed-in user arrives without it.
    ///
    /// The session lives in server memory, but the sign-in lives in tbl_AuthSessions
    /// (script 023). So an app restart, recycle or deploy leaves users signed in with an
    /// empty session: no employer, no year and an empty year picker, until they picked
    /// again. This does what login does, from the preferences saved at each selection
    /// (tbl_User preferences via IUserTrackingService), and re-checks that the user may
    /// still see that employer.
    ///
    /// Runs once per session (a marker key), so a user who deliberately has no employer
    /// selected is not re-filled on every request.
    /// </summary>
    public sealed class SessionContextRestoreFilter : IAsyncActionFilter
    {
        private const string DoneKey = "ContextRestored";

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;
            var session = http.Features.Get<ISessionFeature>()?.Session;

            if (session != null
                && http.User?.Identity?.IsAuthenticated == true
                && session.GetString(DoneKey) == null
                && int.TryParse(http.User.FindFirst("UserId")?.Value, out var userId) && userId > 0)
            {
                session.SetString(DoneKey, "1");
                try
                {
                    await RestoreAsync(http, session, userId);
                }
                catch (Exception ex)
                {
                    // Never block a page over this; the user can still pick by hand.
                    http.RequestServices.GetService<ILogger<SessionContextRestoreFilter>>()
                        ?.LogError(ex, "Restoring the session context failed for user {UserId}.", userId);
                }
            }

            await next();
        }

        private static async Task RestoreAsync(HttpContext http, ISession session, int userId)
        {
            var services = http.RequestServices;

            var years = session.GetObject<List<FilingYearModel>>("FilingYear");
            if (years == null || years.Count == 0)
            {
                years = await services.GetRequiredService<IFilingYearService>().GetAllFilingYears() ?? new();
                session.SetObject("FilingYear", years);
            }

            var prefs = await services.GetRequiredService<IUserTrackingService>()
                                      .GetUserPreferenceAsync(userId.ToString());

            var year = session.GetString("SelectedFilingYear");
            if (string.IsNullOrEmpty(year))
            {
                int latest = years.Where(y => y.FilingYear.HasValue).Select(y => y.FilingYear!.Value)
                                  .DefaultIfEmpty(DateTime.Now.Year).Max();
                year = (prefs?.LastSelectedFilingYear is int saved && saved > 0 ? saved : latest).ToString();
                session.SetString("SelectedFilingYear", year);
            }

            var employerId = session.GetString("SelectedEmployerId");
            if (!string.IsNullOrEmpty(employerId) && employerId != "0")
                return;

            // Permission first: the same check the employer picker and every page use.
            var viewAs = services.GetRequiredService<IViewAsService>();
            var permitted = await viewAs.GetPermittedEmployersAsync(viewAs.Resolve(http)) ?? new List<Employer>();
            var inYear = permitted.Where(e => string.IsNullOrEmpty(e.FilingYear) || e.FilingYear == year).ToList();

            Employer? pick = null;
            if (prefs?.LastSelectedEmployerId is int last)
            {
                // The saved row may be from another year; use the same employer (EIN) in this one.
                var saved = permitted.FirstOrDefault(e => e.Id == last.ToString());
                if (saved != null)
                    pick = inYear.FirstOrDefault(e => e.Id == saved.Id)
                           ?? inYear.FirstOrDefault(e => !string.IsNullOrEmpty(saved.TaxId) && e.TaxId == saved.TaxId);
            }
            if (pick == null && inYear.Count == 1)
                pick = inYear[0];

            if (pick == null || string.IsNullOrEmpty(pick.Id))
                return;

            session.SetString("SelectedEmployerId", pick.Id);
            session.SetString("SelectedEmployerName", pick.Name ?? "");

            // Employer logins can belong to a Primary/Affiliate family; login caches it for
            // the navbar switcher, so restore that too.
            if (http.User.IsInRole(UserRoles.Employer))
            {
                var family = await services.GetRequiredService<IEmployerService>().GetEmployerFamilyAsync(pick.Id);
                session.SetObject("EmployerFamily", family);
                var self = family.FirstOrDefault(f => f.Id == pick.Id);
                bool isPrimary = self == null || self.CompanyId == null || self.CompanyId == 0;
                session.SetString("IsPrimaryEmployer", isPrimary.ToString());
            }
        }
    }
}
