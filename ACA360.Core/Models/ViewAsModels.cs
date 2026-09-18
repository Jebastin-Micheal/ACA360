using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public enum ImpersonationMode
    {
        None = 0,
        Broker = 1,
        Employer = 2
    }

    /// <summary>One selectable target in the View As picker.</summary>
    public class ViewAsTarget
    {
        public int Id { get; set; }
        public string? DisplayName { get; set; }
        public string? Detail { get; set; }   // e.g. "12 employers" or an EIN
    }

    /// <summary>
    /// Who the request is acting as, resolved server-side on every request.
    /// The actor fields always describe the real signed-in person; they are never
    /// replaced by the impersonated target.
    /// </summary>
    public class EffectiveContext
    {
        public int ActorUserId { get; set; }
        public string ActorRole { get; set; } = "";
        public string ActorName { get; set; } = "";

        public ImpersonationMode Mode { get; set; } = ImpersonationMode.None;
        public int TargetId { get; set; }
        public string TargetName { get; set; } = "";

        public bool IsImpersonating => Mode != ImpersonationMode.None;

        /// <summary>Impersonated sessions are strictly read-only.</summary>
        public bool IsReadOnly => IsImpersonating;

        /// <summary>
        /// The role the UI and data scoping should behave as. Equals the actor's
        /// own role when not impersonating.
        /// </summary>
        public string EffectiveRole => Mode switch
        {
            ImpersonationMode.Broker => "Broker",
            ImpersonationMode.Employer => "Employer",
            _ => ActorRole
        };

        /// <summary>
        /// The user id entitlement lookups run against. Meaningful for Broker mode
        /// (the broker's own id) and for no impersonation (the actor). In Employer
        /// mode there is no user to scope by — use TargetId via
        /// IViewAsService.GetPermittedEmployersAsync instead.
        /// </summary>
        public int ScopeUserId => Mode == ImpersonationMode.Broker ? TargetId : ActorUserId;

        public string BannerText => Mode switch
        {
            ImpersonationMode.Broker => $"Viewing as Broker — {TargetName}",
            ImpersonationMode.Employer => $"Viewing as Employer — {TargetName}",
            _ => ""
        };
    }

    public class ViewAsOptions
    {
        public bool CanViewAsBroker { get; set; }
        public bool CanViewAsEmployer { get; set; }
        public bool AnyAvailable => CanViewAsBroker || CanViewAsEmployer;
        public List<ViewAsTarget> Brokers { get; set; } = new();
        public List<ViewAsTarget> Employers { get; set; } = new();
    }
}