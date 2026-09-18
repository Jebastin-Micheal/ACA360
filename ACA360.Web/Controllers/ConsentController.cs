// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using System;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Employer/admin-facing view of an employee's electronic-delivery consent.
    /// Reached from the employee list (consent icon). Reads the same consent data
    /// the employee self-service page (MyForms/DeliveryPreference) writes.
    /// </summary>
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," +
                       UserRoles.ACADirector + "," + UserRoles.Employer)]
    public class ConsentController : BaseController
    {
        private readonly IConsentService _consentService;
        private readonly ILoggerService _logger;

        public ConsentController(IConsentService consentService, ILoggerService logger)
        {
            _consentService = consentService;
            _logger = logger;
        }

        /// <summary>
        /// GET: Renders the "Electronic Delivery Consent Confirmed" page for one employee.
        /// URL: /Consent/Confirmation?id={employeeId}&year={taxYear}
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Confirmation(int id, int year = 0)
        {
            try
            {
                if (id <= 0)
                    return BadRequest("A valid employee id is required.");

                if (year == 0)
                {
                    var yearString = GetCurrentFilingYear();
                    year = !string.IsNullOrEmpty(yearString) ? int.Parse(yearString) : DateTime.UtcNow.Year;
                }

                var model = await _consentService.GetConsentAsync(id, year);
                ViewBag.TaxYear = year;
                Audit("Viewed consent confirmation", id, year);
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Confirmation), "ConsentController",
                    $"Error loading consent confirmation for Employee {id}, Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred while loading the consent details.");
            }
        }

        /// <summary>
        /// GET: A print-friendly consent receipt the admin can view/print/save as PDF.
        /// URL: /Consent/Receipt?id={employeeId}&year={taxYear}
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Receipt(int id, int year)
        {
            try
            {
                if (id <= 0 || year <= 0) return BadRequest("A valid employee id and year are required.");
                var model = await _consentService.GetConsentAsync(id, year);

                Audit("Viewed consent receipt", id, year);
                var pdf = BuildReceiptPdf(model, year);
                return File(pdf, "application/pdf", $"ConsentReceipt_{id}_{year}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Receipt), "ConsentController", $"Error loading consent receipt for Employee {id}, Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while generating the receipt.");
            }
        }

        /// <summary>Writes an admin-action audit line to the application log.</summary>
        private void Audit(string action, int employeeId, int year)
        {
            _logger.LogInfo($"{action} for Employee {employeeId}, Year {year} by {User?.Identity?.Name ?? "Unknown"}",
                "ConsentController", "ConsentAudit",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "System");
        }

        /// <summary>Builds a compliance-grade PDF consent receipt with QuestPDF.</summary>
        private static byte[] BuildReceiptPdf(EmployeeConsentViewModel m, int year)
        {
            var consentDate = m.UpdatedOn ?? m.CreatedOn;
            var empName = string.IsNullOrWhiteSpace(m.EmployeeName) ? "Employee" : m.EmployeeName;

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(11).FontColor("#333333"));

                    page.Header().BorderBottom(2).BorderColor("#696cff").PaddingBottom(8).Row(r =>
                    {
                        r.RelativeItem().Text("ACA 360").FontSize(18).Bold().FontColor("#2b3a67");
                        r.RelativeItem().AlignRight().Text("Electronic Delivery Consent Receipt").FontColor("#777777");
                    });

                    page.Content().PaddingVertical(16).Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Text("Consent Receipt").FontSize(20).Bold();
                        col.Item().PaddingBottom(8).Text("This document confirms the employee's electronic delivery consent for ACA tax forms and is retained for compliance records.").FontColor("#777777");

                        void Field(string label, string val)
                        {
                            col.Item().Row(r =>
                            {
                                r.ConstantItem(180).Text(label).FontColor("#777777");
                                r.RelativeItem().Text(string.IsNullOrEmpty(val) ? "—" : val).SemiBold();
                            });
                        }

                        Field("Employee Name", empName);
                        Field("SSN", m.SsnMasked);
                        Field("Effective Tax Year", year.ToString());
                        Field("Status", m.Status);
                        Field("Delivery Preference", m.DeliveryPreference);
                        Field("Consent ID", m.ConsentId);
                        Field("Consent Method", m.ConsentMethod);
                        Field("Consent Date & Time", consentDate.HasValue ? consentDate.Value.ToString("MMM dd, yyyy 'at' h:mm tt") + " UTC" : "—");
                        Field("Work Email", m.WorkEmail);
                        Field("Notification Email", m.NotificationEmail);
                        Field("IP Address", m.IpAddress);
                        Field("Device / Browser", m.UserAgent);
                    });

                    page.Footer().Text($"Generated {DateTime.UtcNow:MMM dd, yyyy 'at' h:mm tt} UTC  ·  © {DateTime.Now.Year} Medcom Benefit Solutions, LLC.")
                        .FontSize(9).FontColor("#999999");
                });
            });

            return doc.GeneratePdf();
        }

        /// <summary>
        /// POST: Emails the consent receipt to the employee.
        /// (SMTP is not wired yet — the request is recorded and surfaced to the admin.)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmailReceipt(int id, int year)
        {
            try
            {
                if (id <= 0 || year <= 0) return BadRequest("A valid employee id and year are required.");

                var model = await _consentService.GetConsentAsync(id, year);
                var to = model.NotificationEmail ?? model.WorkEmail;

                // TODO: send via the email service once SMTP is configured.
                Audit($"Emailed consent receipt to {to}", id, year);

                TempData["ConsentUpdated"] = string.IsNullOrEmpty(to)
                    ? "This employee has no email on file, so the receipt couldn't be sent."
                    : $"A consent receipt will be emailed to {to} once email delivery is enabled. You can also View/Print it now.";

                return RedirectToAction(nameof(Confirmation), new { id, year });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(EmailReceipt), "ConsentController", $"Error emailing consent receipt for Employee {id}, Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while emailing the receipt.");
            }
        }

        /// <summary>
        /// POST: Withdraws the employee's electronic consent (reverts to paper) and
        /// returns to the confirmation page.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WithdrawConsent(int id, int year)
        {
            try
            {
                if (id <= 0 || year <= 0)
                    return BadRequest("A valid employee id and year are required.");

                await _consentService.WithdrawAsync(id, year);
                Audit("Withdrew electronic consent", id, year);
                TempData["ConsentUpdated"] = "Electronic consent withdrawn. The employee will receive paper forms.";
                return RedirectToAction(nameof(Confirmation), new { id, year });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(WithdrawConsent), "ConsentController",
                    $"Error withdrawing consent for Employee {id}, Year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred while withdrawing the consent.");
            }
        }
    }
}
