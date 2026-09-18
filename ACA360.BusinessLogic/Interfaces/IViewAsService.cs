using System.Threading.Tasks;
using ACA360.Core.Models;
using Microsoft.AspNetCore.Http;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IViewAsService
    {
        /// <summary>Resolves the effective context from the auth cookie plus session.</summary>
        EffectiveContext Resolve(HttpContext http);

        /// <summary>Targets this actor is permitted to select. Never derived from the client.</summary>
        Task<ViewAsOptions> GetOptionsAsync(int actorUserId, string actorRole);

        /// <summary>Re-checks entitlement, then writes the impersonation into session and audits it.</summary>
        Task<bool> BeginAsync(HttpContext http, ImpersonationMode mode, int targetId);

        /// <summary>Clears the impersonation and audits the exit.</summary>
        Task EndAsync(HttpContext http);
        /// <summary>
        /// The employers this request may open, accounting for impersonation.
        /// The one place that answers this question, so the three callers cannot drift.
        /// </summary>
        Task<List<Employer>> GetPermittedEmployersAsync(EffectiveContext ctx);
    }
}