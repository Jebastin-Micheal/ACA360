using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using ACA360.Web.Models; // Assume you put the model in your Models folder

namespace ACA360.Web.ViewComponents
{
    public class BreadcrumbViewComponent : ViewComponent
    {
        // Update Invoke to accept the title as a string
        public IViewComponentResult Invoke(string pageTitle, List<BreadcrumbItem> items)
        {
            var model = new BreadcrumbViewModel
            {
                PageTitle = pageTitle,
                Items = items
            };
            return View(model);
        }
    }
}