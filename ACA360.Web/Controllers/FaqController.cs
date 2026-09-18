using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Microsoft.AspNetCore.Authorization; // Added missing namespace
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
   // [Authorize(Roles = "Admin, SuperAdmin")]
    public class FaqController : Controller
    {
        private readonly IFaqService _faqService;

        public FaqController(IFaqService faqService)
        {
            _faqService = faqService;
        }

        // GET: Faq
        public async Task<IActionResult> Index()
        {
            ViewBag.FAQ_SupportPhone = await _faqService.GetSystemSettingAsync("FAQ_SupportPhone");
            ViewBag.FAQ_SupportEmail = await _faqService.GetSystemSettingAsync("FAQ_SupportEmail");
            // Pass the Icons to the View via ViewBag
            ViewBag.IconOptions = new List<(string Value, string Text, string CssClass)>
    {
        ("bx-folder", "Folder", "bx-folder"),
        ("bx-credit-card", "Payment / Credit Card", "bx-credit-card"),
        ("bx-shopping-bag", "Delivery / Bag", "bx-shopping-bag"),
        ("bx-revision", "Return / Refresh", "bx-revision"),
        ("bx-box", "Box / Orders", "bx-box"),
        ("bx-cog", "Settings / Services", "bx-cog"),
        ("bx-file", "File / Document", "bx-file"),
        ("bx-shield", "Security / Shield", "bx-shield"),
        ("bxl-instagram", "Instagram", "bxl-instagram"),
        ("bxl-twitch", "Twitch", "bxl-twitch")
    };
            var faqs = await _faqService.GetAllFaqsAsync();
            return View(faqs);
        }
        
     
        // GET: Faq/GetContactSettingsForm
        [HttpGet]
        public async Task<IActionResult> GetContactSettingsForm()
        {
            var model = new ContactSettingsViewModel
            {
                FAQ_SupportPhone = await _faqService.GetSystemSettingAsync("FAQ_SupportPhone"),
                FAQ_SupportEmail = await _faqService.GetSystemSettingAsync("FAQ_SupportEmail")
            };

            return PartialView("_ContactSettingsForm", model);
        }

        // POST: Faq/SaveContactSettings
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveContactSettings(ContactSettingsViewModel model)
        {
            
            try
            {
                await _faqService.UpdateSystemSettingAsync("FAQ_SupportPhone", model.FAQ_SupportPhone ?? "");
                await _faqService.UpdateSystemSettingAsync("FAQ_SupportEmail", model.FAQ_SupportEmail ?? "");

                TempData["SuccessMessage"] = "Contact settings updated successfully!";
                return RedirectToAction("Index");
            }
            catch (Exception)
            {
                TempData["ErrorMessage"] = "An error occurred while saving contact settings.";
                return RedirectToAction("Index");
            }
        }


        // GET: Faq/GetFaqForm (Handles both Create and Edit)
        [HttpGet]
        public async Task<IActionResult> GetFaqForm(string id = null)
        {
            // Load categories for the dropdown
            ViewBag.Categories = await _faqService.GetAllCategoriesAsync();

            if (string.IsNullOrEmpty(id))
            {
                return PartialView("_FaqForm", new FaqModel { IsActive = true, SortOrder = 10 });
            }

            var faq = await _faqService.GetFaqByIdAsync(id);
            if (faq == null) return NotFound();

            return PartialView("_FaqForm", faq);
        }

        // POST: Faq/SaveFaq
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveFaq(FaqModel model)
        {            

            try
            {
                if (string.IsNullOrEmpty(model.Id))
                {
                    await _faqService.CreateFaqAsync(model);
                    TempData["SuccessMessage"] = "FAQ created successfully!";
                }
                else
                {
                    await _faqService.UpdateFaqAsync(model);
                    TempData["SuccessMessage"] = "FAQ updated successfully!";
                }

                return RedirectToAction(nameof(Index)); // Redirects to Index action
            }
            catch (Exception)
            {
                ModelState.AddModelError("", "An error occurred while saving.");
                return PartialView("_FaqForm", model);
            }
        }

        // POST: Faq/DeleteFaq
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFaq(string id)
        {
            try
            {
                await _faqService.DeleteFaqAsync(id);
                return Json(new { success = true, message = "FAQ deleted successfully!" });
            }
            catch (System.Exception)
            {
                return Json(new { success = false, message = "An error occurred while deleting." });
            }
        }

        // New method to handle on-the-fly category creation
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddNewCategory(string categoryName, string categoryIcon)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return Json(new { success = false, message = "Category name is required." });

            try
            {
                var newCat = await _faqService.CreateCategoryAsync(categoryName, categoryIcon);
                return Json(new { success = true, id = newCat.Id, name = newCat.Name });
            }
            catch (System.Exception ex)
            {
                return Json(new { success = false, message = "Failed to add category." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetCategory(string id)
        {
            var category = await _faqService.GetCategoryByIdAsync(id);
            if (category == null) return NotFound();

            return Json(category);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCategory(string categoryId, string categoryName, string categoryIcon)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return Json(new { success = false, message = "Category name is required." });

            bool result = await _faqService.UpdateCategoryAsync(categoryId, categoryName, categoryIcon);
            return Json(new { success = result, message = result ? "Category updated successfully!" : "Failed to update category." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCategory(string id)
        {
            var result = await _faqService.DeleteCategoryAsync(id);
            return Json(new { success = result.Success, message = result.Message });
        }
    }
}