using ACA360.BusinessLogic;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.ViewComponents
{
    public class NotificationsViewComponent : ViewComponent
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        public NotificationsViewComponent(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var notifications = await GetUserNotificationsAsync();
            return View(notifications);
        }
        // Simulated async data retrieval (replace with database/service logic)
        private Task<List<string>> GetUserNotificationsAsync()
        {
            // Example: Fetch based on user
            var userId = HttpContext.User.Identity.Name ?? "Guest";
            var notifications = new List<string>
        {
            $"Welcome {userId}!",
            "You have 3 new messages.",
            "Your profile was updated successfully."
        };

            return Task.FromResult(notifications);
        }
    }
}
