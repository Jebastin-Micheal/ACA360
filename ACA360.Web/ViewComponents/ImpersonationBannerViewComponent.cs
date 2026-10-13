using ACA360.BusinessLogic.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace ACA360.Web.ViewComponents
{
    /// <summary>
    /// Renders the "you are viewing as someone else" banner on every page.
    /// It reads the effective context itself rather than taking a model, so it can
    /// sit in _Layout without every page view model having to carry the state.
    /// </summary>
    public class ImpersonationBannerViewComponent : ViewComponent
    {
        private readonly IViewAsService _viewAs;

        public ImpersonationBannerViewComponent(IViewAsService viewAs) => _viewAs = viewAs;

        public Task<IViewComponentResult> InvokeAsync()
        {
            var ctx = _viewAs.Resolve(HttpContext);
            return Task.FromResult<IViewComponentResult>(View(ctx));
        }
    }
}