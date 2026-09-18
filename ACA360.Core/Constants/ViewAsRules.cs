using System;
using System.Collections.Generic;
using ACA360.Core.Models;

namespace ACA360.Core.Constants
{
    /// <summary>
    /// The single place the View As role policy lives. Whitelist, not blacklist:
    /// a role that is not named here gets no impersonation modes, so adding a new
    /// role to the system cannot silently grant it.
    /// </summary>
    public static class ViewAsRules
    {
        private static readonly HashSet<string> MedcomInternal =
            new(StringComparer.OrdinalIgnoreCase)
            {
                UserRoles.SuperAdmin,
                UserRoles.Admin,
                UserRoles.ACADirector,
                UserRoles.DASupervisor,
                UserRoles.AMSupervisor,
                UserRoles.DataAnalyst,
                UserRoles.AccountManager,
                UserRoles.StaffAC,
                UserRoles.StaffAU
            };

        public static bool IsMedcomInternal(string? role) =>
            !string.IsNullOrWhiteSpace(role) && MedcomInternal.Contains(role);

        public static bool CanViewAsBroker(string? role) => IsMedcomInternal(role);

        public static bool CanViewAsEmployer(string? role) =>
            IsMedcomInternal(role) ||
            string.Equals(role, UserRoles.Broker, StringComparison.OrdinalIgnoreCase);

        public static bool CanViewAsAnything(string? role) =>
            CanViewAsBroker(role) || CanViewAsEmployer(role);

        public static bool IsModeAllowed(string? role, ImpersonationMode mode) => mode switch
        {
            ImpersonationMode.Broker => CanViewAsBroker(role),
            ImpersonationMode.Employer => CanViewAsEmployer(role),
            _ => false
        };
    }
}