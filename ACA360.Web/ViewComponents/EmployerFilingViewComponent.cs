using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.ViewComponents
{
    public class EmployerFilingViewComponent : ViewComponent
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        public EmployerFilingViewComponent(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var ipAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();
            return View();
        }
    }
}
