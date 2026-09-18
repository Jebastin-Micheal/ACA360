using ACA360.Web.Extensions;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Mvc.Razor;

namespace ACA360.Web.Infrastructure
{
    public abstract class ACA360RazorPage<TModel> : RazorPage<TModel>
    {
        public bool CanViewSSNorEIN => User.CanViewFullPii();
        //public bool CanEditEmployer => User.CanEditEmployer();
        //public bool IsInternalUser => User.IsInternalTeam();
    }
}