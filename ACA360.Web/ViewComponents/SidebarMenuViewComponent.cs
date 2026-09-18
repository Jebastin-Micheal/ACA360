using ACA360.BusinessLogic;
using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
namespace ACA360.Web.ViewComponents
{
    public class SidebarMenuViewComponent : ViewComponent
    {
        private readonly IMenuService _menuRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SidebarMenuViewComponent(IMenuService menuRepository, IHttpContextAccessor httpContextAccessor)
        {
            _menuRepository = menuRepository;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IViewComponentResult> InvokeAsync(string viewName = "Default")
        {
            var user = _httpContextAccessor.HttpContext?.User;

            var userId = (user as ClaimsPrincipal)?.FindFirst("UserId")?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                var menuItems = await _menuRepository.GetMenuByUserId(userId);
                return View(viewName, menuItems);
            }

            return Content(""); // Return empty if user is not logged in
        }

    }
}
