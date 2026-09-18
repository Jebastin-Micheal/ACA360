using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;

namespace ACA360.BusinessLogic.Services
{
    public class ViewAsService : IViewAsService
    {
        private const string KeyMode = "ViewAs.Mode";
        private const string KeyTargetId = "ViewAs.TargetId";
        private const string KeyTargetName = "ViewAs.TargetName";
        private const string KeyPrevEmployerId = "ViewAs.PrevSelectedEmployerId";
        private const string KeyPrevEmployerName = "ViewAs.PrevSelectedEmployerName";

        private readonly string _connectionString;
        private readonly IEmployerService _employerService;
        private readonly IDataAuditlogService _audit;
        private readonly ILogger<ViewAsService> _logger;

        public ViewAsService(
            string connectionString,
            IEmployerService employerService,
            IDataAuditlogService audit,
            ILogger<ViewAsService> logger)
        {
            _connectionString = connectionString;
            _employerService = employerService;
            _audit = audit;
            _logger = logger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        // ------------------------------------------------------------------ resolve

        public EffectiveContext Resolve(HttpContext http)
        {
            var ctx = new EffectiveContext();
            var user = http?.User;
            if (user?.Identity == null || !user.Identity.IsAuthenticated) return ctx;

            int.TryParse(user.FindFirst("UserId")?.Value, out int actorId);
            ctx.ActorUserId = actorId;
            ctx.ActorRole = user.FindFirst(ClaimTypes.Role)?.Value ?? "";
            ctx.ActorName = user.Identity.Name ?? "";

            var session = http.Session;
            if (session == null) return ctx;

            var rawMode = session.GetInt32(KeyMode);
            if (rawMode == null || rawMode == 0) return ctx;

            var mode = (ImpersonationMode)rawMode.Value;

            // Belt and braces: even though BeginAsync validated, re-check the policy
            // on every request. A role change or a session restored from a different
            // deployment must not keep an impersonation alive.
            if (!ViewAsRules.IsModeAllowed(ctx.ActorRole, mode))
            {
                ClearSession(session);
                return ctx;
            }

            ctx.Mode = mode;
            ctx.TargetId = session.GetInt32(KeyTargetId) ?? 0;
            ctx.TargetName = session.GetString(KeyTargetName) ?? "";

            if (ctx.TargetId <= 0)
            {
                ClearSession(session);
                return new EffectiveContext
                {
                    ActorUserId = ctx.ActorUserId,
                    ActorRole = ctx.ActorRole,
                    ActorName = ctx.ActorName
                };
            }

            return ctx;
        }

        // ------------------------------------------------------------------ options

        public async Task<ViewAsOptions> GetOptionsAsync(int actorUserId, string actorRole)
        {
            var opts = new ViewAsOptions
            {
                CanViewAsBroker = ViewAsRules.CanViewAsBroker(actorRole),
                CanViewAsEmployer = ViewAsRules.CanViewAsEmployer(actorRole)
            };

            if (!opts.AnyAvailable) return opts;   // Employer role stops here

            if (opts.CanViewAsBroker)
            {
                using var db = Connection;
                var rows = await db.QueryAsync<(int UserId, string DisplayName, int EmployerCount)>(
                    "sp_GetViewAsBrokerTargets",
                    new { ActorUserId = actorUserId },
                    commandType: CommandType.StoredProcedure);

                opts.Brokers = rows.Select(r => new ViewAsTarget
                {
                    Id = r.UserId,
                    DisplayName = r.DisplayName,
                    Detail = r.EmployerCount == 1 ? "1 employer" : $"{r.EmployerCount} employers"
                }).ToList();
            }

            if (opts.CanViewAsEmployer)
            {
                // Reuse the existing entitlement oracle rather than writing a second
                // one: it already returns all employers for SuperAdmin/Admin/Director
                // and only assigned employers for everyone else, brokers included.
                var employers = await _employerService.GetEmployersForUserAsync(
                    actorUserId.ToString(), actorRole);

                // Employer.Id is a string on the model. Parse it here so the target
                // list is typed, and drop anything unparseable rather than letting a
                // zero id through — a 0 would never match on the way back in, but it
                // would sit in the picker looking selectable.
                opts.Employers = (employers ?? Enumerable.Empty<Employer>())
                    .Select(e => new
                    {
                        Id = int.TryParse(e.Id, out var parsed) ? parsed : 0,
                        e.Name,
                        e.TaxId
                    })
                    .Where(x => x.Id > 0)
                    .Select(x => new ViewAsTarget
                    {
                        Id = x.Id,
                        DisplayName = x.Name,
                        Detail = x.TaxId
                    })
                    .OrderBy(t => t.DisplayName)
                    .ToList();
            }

            return opts;
        }
        public async Task<List<Employer>> GetPermittedEmployersAsync(EffectiveContext ctx)
        {
            if (ctx == null || ctx.ActorUserId <= 0) return new List<Employer>();

            // Viewing as an employer: the permitted set is exactly that one employer.
            // Do NOT route this through GetEmployersForUserAsync — with role "Employer"
            // it resolves an employer by USER id, and the actor is staff, so it returns
            // nothing and every page 403s.
            if (ctx.Mode == ImpersonationMode.Employer)
            {
                var one = await _employerService.GetEmployerByIdAsync(ctx.TargetId.ToString());
                return one != null ? new List<Employer> { one } : new List<Employer>();
            }

            // Viewing as a broker: scope to the broker's own user id and role, so the
            // list is exactly their book.
            // Not impersonating: the actor's own id and role.
            var employers = await _employerService.GetEmployersForUserAsync(
                ctx.ScopeUserId.ToString(), ctx.EffectiveRole);

            return (employers ?? Enumerable.Empty<Employer>()).ToList();
        }
        // -------------------------------------------------------------------- begin

        public async Task<bool> BeginAsync(HttpContext http, ImpersonationMode mode, int targetId)
        {
            var ctx = Resolve(http);
            if (ctx.ActorUserId <= 0) return false;

            // No nesting. Exit the current impersonation first.
            if (ctx.IsImpersonating) return false;

            if (!ViewAsRules.IsModeAllowed(ctx.ActorRole, mode)) return false;
            if (targetId <= 0) return false;

            // Re-derive the permitted set and confirm membership. The id arriving
            // from the client is only ever a lookup key, never an authorisation.
            var opts = await GetOptionsAsync(ctx.ActorUserId, ctx.ActorRole);
            var target = mode == ImpersonationMode.Broker
                ? opts.Brokers.FirstOrDefault(t => t.Id == targetId)
                : opts.Employers.FirstOrDefault(t => t.Id == targetId);

            if (target == null)
            {
                _logger.LogWarning(
                    "View As denied. Actor {ActorId} ({Role}) requested {Mode} target {TargetId}, which is not in their permitted set.",
                    ctx.ActorUserId, ctx.ActorRole, mode, targetId);
                return false;
            }

            http.Session.SetInt32(KeyMode, (int)mode);
            http.Session.SetInt32(KeyTargetId, targetId);
            http.Session.SetString(KeyTargetName, target.DisplayName ?? "");

            // Remember whatever was selected before we overwrite it, so EndAsync
            // can restore it when this impersonation ends.
            http.Session.SetString(KeyPrevEmployerId, http.Session.GetString("SelectedEmployerId") ?? "");
            http.Session.SetString(KeyPrevEmployerName, http.Session.GetString("SelectedEmployerName") ?? "");

            // Selecting an employer while impersonating must not inherit the actor's
            // previously selected employer.
            http.Session.SetString("SelectedEmployerId",
                mode == ImpersonationMode.Employer ? targetId.ToString() : "");
            http.Session.SetString("SelectedEmployerName",
                mode == ImpersonationMode.Employer ? (target.DisplayName ?? "") : "");

            await AuditAsync(http, ctx, "IMPERSONATION_BEGIN",
                $"{mode}:{targetId} ({target.DisplayName})");

            return true;
        }

        // ---------------------------------------------------------------------- end

        public async Task EndAsync(HttpContext http)
        {
            var ctx = Resolve(http);
            if (ctx.IsImpersonating)
            {
                await AuditAsync(http, ctx, "IMPERSONATION_END",
                    $"{ctx.Mode}:{ctx.TargetId} ({ctx.TargetName})");
            }

            ClearSession(http.Session);
            var prevId = http.Session.GetString(KeyPrevEmployerId) ?? "";
            var prevName = http.Session.GetString(KeyPrevEmployerName) ?? "";
            http.Session.SetString("SelectedEmployerId", prevId);
            http.Session.SetString("SelectedEmployerName", prevName);
            http.Session.Remove(KeyPrevEmployerId);
            http.Session.Remove(KeyPrevEmployerName);
        }

        // ------------------------------------------------------------------ helpers

        private static void ClearSession(ISession session)
        {
            session.Remove(KeyMode);
            session.Remove(KeyTargetId);
            session.Remove(KeyTargetName);
        }

        private async Task AuditAsync(HttpContext http, EffectiveContext ctx, string action, string detail)
        {
            try
            {
                await _audit.LogChangeAsync(new DataAuditLog
                {
                    Id = Guid.NewGuid(),
                    Table_Name = "ViewAs",
                    Record_Id = ctx.ActorUserId.ToString(),
                    Action_Type = action,
                    Old_Values = null,
                    New_Values = detail,
                    User_Id = ctx.ActorUserId.ToString(),
                    User_Name = ctx.ActorName,
                    User_Role = ctx.ActorRole,
                    IP_Address = http.Connection?.RemoteIpAddress?.ToString(),
                    Created_at = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                // Never let an audit failure block the response, but do not let it
                // pass unnoticed either.
                _logger.LogError(ex, "Failed to audit {Action} for actor {ActorId}.", action, ctx.ActorUserId);
            }
        }
    }
}