using System.Security.Claims;
using ACA360.Core.Constants;

namespace ACA360.Web.Helpers
{
    public static class PiiAccess
    {
        public static bool CanViewFullPii(this ClaimsPrincipal user)
        {
            return user.IsInRole(UserRoles.SuperAdmin)
                || user.IsInRole(UserRoles.Admin)
                || user.IsInRole(UserRoles.ACADirector)
                || user.IsInRole(UserRoles.DASupervisor)
                || user.IsInRole(UserRoles.AMSupervisor)
                || user.IsInRole(UserRoles.DataAnalyst)
                || user.IsInRole(UserRoles.StaffAC)
                || user.IsInRole(UserRoles.StaffAU)
                || user.IsInRole(UserRoles.AccountManager);
        }
    }
}
