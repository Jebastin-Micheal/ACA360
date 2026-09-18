using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.ViewComponents
{
    public class QuickLinksViewComponent : ViewComponent
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        public QuickLinksViewComponent(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var QuickLinks = await GetUserQuickLinksAsync();
            return View(QuickLinks);
        }
        // Simulated async data retrieval (replace with database/service logic)
        private Task<List<string>> GetUserQuickLinksAsync()
        {
            // Example: Fetch based on user
            var userId = HttpContext.User.Identity.Name ?? "Guest";
            var QuickLinks = new List<string>
        {
            $"Welcome {userId}!",
            "You have 3 new messages.",
            "Your profile was updated successfully."
        };

            return Task.FromResult(QuickLinks);
        }
    }
}
