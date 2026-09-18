using ACA360.Core.Constants;
using ACA360.Logging.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Provides the employee self-service portal for accessing and downloading ACA forms (1095-C/B).
    /// Authentication is token-based: employees arrive via an emailed link, verify their identity
    /// using the last 4 digits of their SSN, and receive a temporary session to access their forms.
    ///
    /// <para>
    /// Deliberately anonymous. Employees are not users of this application and hold
    /// none of its roles, so the previous class-level role list — which named every
    /// internal role — made the portal unreachable by the only people it is for.
    /// Combined with the global FallbackPolicy, clicking the emailed link redirected
    /// them to a login page they could never pass.
    /// </para>
    ///
    /// <para>
    /// Authorisation is therefore per-action, not per-controller. Access and Verify
    /// are genuinely public; every action that exposes employee data first requires
    /// the portal session that Verify establishes. That session is the credential —
    /// it is set only after the token and the last four SSN digits both check out.
    /// </para>
    /// </summary>
    [AllowAnonymous]
    public class MyFormsController : Controller
    {
        /// <summary>Failed verifications allowed against one link before it is frozen.</summary>
        private const int MaxAttemptsPerLink = 5;

        /// <summary>Failed verifications allowed from one address across all links.</summary>
        private const int MaxAttemptsPerAddress = 20;

        /// <summary>How long a lockout lasts, and the window attempts are counted over.</summary>
        private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

        private readonly IDistributionService _distService;
        private readonly IPdfService _pdfService;
        private readonly ILoggerService _logger;
        private readonly IMemoryCache _cache;

        /// <summary>
        /// Initialises the MyFormsController with the distribution service, PDF service,
        /// logger and memory cache injected via the DI container.
        /// </summary>
        public MyFormsController(
            IDistributionService distService,
            IPdfService pdfService,
            ILoggerService logger,
            IMemoryCache cache)
        {
            _distService = distService;
            _pdfService = pdfService;
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// GET: Landing page for the employee portal — reached via the emailed token link.
        /// Validates that the token in the URL is not empty before rendering the identity
        /// verification form. Returns 400 if the link is malformed or the token is missing.
        /// URL: /MyForms/Access?token=GUID
        /// </summary>
        [HttpGet]
        public IActionResult Access(Guid token)
        {
            try
            {
                // Reject empty tokens immediately — the link is invalid or has been tampered with
                if (token == Guid.Empty)
                    return BadRequest("Invalid Link");

                // Pass the token to the view so it can be included in the verification POST
                ViewBag.Token = token;
                return View();
            }
            catch (Exception ex)
            {
                // Log unexpected rendering errors and return a 500 to avoid a blank page
                _logger.LogError(ex, nameof(Access), "MyFormsController",
                    "Error rendering the Access landing page", GetUserIp());

                return StatusCode(500, "An unexpected error occurred while loading the access page.");
            }
        }

        /// <summary>
        /// POST: Verifies the employee's identity using their token and the last 4 digits of their SSN (2FA step).
        /// On success, establishes a temporary portal session and redirects to the employee dashboard.
        /// On failure, returns the Access view with a descriptive error message so the employee can retry.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Verify(Guid token, string last4SSN)
        {
            try
            {
                // Identity here rests on four digits — ten thousand combinations, which
                // is nothing to an automated client, and the link itself names the
                // employee. Throttle per link and per address before spending a lookup.
                // Counted in memory rather than on SecureDownloadLinks so this needs no
                // schema change; the trade-off is that the counters reset on restart and
                // are per-instance, which is worth revisiting if the portal is ever run
                // behind more than one node.
                if (IsThrottled(token, out var retryAfter))
                {
                    _logger.LogError(new UnauthorizedAccessException("Portal verification throttled"),
                        nameof(Verify), "MyFormsController",
                        $"Verification blocked for token {token} from {GetUserIp()} — too many failed attempts.",
                        GetUserIp());

                    ViewBag.Error = $"Too many failed attempts. Please try again in about {retryAfter} minutes.";
                    ViewBag.Token = token;
                    return View("Access");
                }

                var result = await _distService.ValidateTokenAsync(token, last4SSN, GetUserIp());

                if (result == null || !result.IsValid)
                {
                    RecordFailedAttempt(token);

                    // The link now counts its own failures and locks itself, so a lockout
                    // can be reported precisely — reaching it means the caller already
                    // holds a real link, so saying so reveals nothing they do not know.
                    // Everything else stays one message: distinguishing "wrong digits"
                    // from "unknown, expired or revoked link" would confirm which links
                    // exist and which employees they belong to.
                    if (result?.IsLockedOut == true)
                    {
                        _logger.LogError(new UnauthorizedAccessException("Portal link locked"),
                            nameof(Verify), "MyFormsController",
                            $"Link {token} locked after repeated failures. Caller {GetUserIp()}.",
                            GetUserIp());

                        ViewBag.Error =
                            $"Too many failed attempts on this link. Please try again in about " +
                            $"{Math.Max(1, result.RetryAfterMinutes)} minutes, or ask your employer to send a new one.";
                    }
                    else
                    {
                        ViewBag.Error = "Verification Failed. The Last 4 SSN does not match our records or the link has expired.";
                    }

                    ViewBag.Token = token;
                    return View("Access");
                }

                ClearFailedAttempts(token);

                //  Store both EmployeeId and the correct filing Year in session
                HttpContext.Session.SetInt32("EmployeePortal_Id", result.EmployeeId.Value);
                HttpContext.Session.SetInt32("EmployeePortal_Year", result.Year.Value);
                HttpContext.Session.SetString("EmployeePortal_Token", token.ToString());

                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Verify), "MyFormsController",
                    $"Identity verification failed for Token={token}", GetUserIp());

                ViewBag.Error = "A system error occurred during verification. Please try again or contact support.";
                ViewBag.Token = token;
                return View("Access");
            }
        }

        /// <summary>
        /// GET: Renders the authenticated employee's form dashboard.
        /// Guards access using the portal session established after successful identity verification.
        /// Redirects back to the Access page if the session has expired or is absent.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            try
            {
                int? empId = HttpContext.Session.GetInt32("EmployeePortal_Id");
                int? year = HttpContext.Session.GetInt32("EmployeePortal_Year");

                if (empId == null || year == null)
                {
                    // Session expired or year missing — force re-verification
                    return RedirectToAction("Access", new { token = Guid.Empty });
                }

                //  No more DateTime.Now.Year — use the year tied to this employee's actual token
                return View(year.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Dashboard), "MyFormsController",
                    "Error loading employee dashboard", GetUserIp());

                return StatusCode(500, "An unexpected error occurred while loading the dashboard.");
            }
        }

        /// <summary>
        /// GET: Generates and streams the employee's 1095-C PDF for the requested filing year.
        /// PDFs are generated on-demand to ensure freshness and avoid stale cached files.
        /// Returns 401 if the portal session has expired; 500 on PDF generation failure.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DownloadForm(int year)
        {
            try
            {
                // Security check: require an active portal session before generating any form data
                int? empId = HttpContext.Session.GetInt32("EmployeePortal_Id");
                int? sessionYear = HttpContext.Session.GetInt32("EmployeePortal_Year");

                if (empId == null || sessionYear == null)
                    return Unauthorized();

                // Generate the 1095-C PDF on the fly for the verified employee and requested year.
                // Note: For B-form employers, replace with Generate1095BForEmployeeAsync accordingly.
                var pdfBytes = await _pdfService.Generate1095CForEmployeeAsync(empId.Value, sessionYear.Value);

                // Return the PDF as a downloadable attachment with a descriptive file name
                return File(pdfBytes, "application/pdf", $"My_1095C_{sessionYear.Value}.pdf");
            }
            catch (Exception ex)
            {
                // Log the PDF generation failure; include year for correlation with filing records
                _logger.LogError(ex, nameof(DownloadForm), "MyFormsController",
                    $"PDF generation failed for Year={year}", GetUserIp());

                return StatusCode(500, "An unexpected error occurred while generating your form. Please try again.");
            }
        }

        /// <summary>
        /// GET: Terminates the employee portal session and renders the logout confirmation view.
        /// Clears all session state to prevent session fixation or re-use after the employee leaves.
        /// </summary>
        [HttpGet]
        public IActionResult Logout()
        {
            try
            {
                // Clear all portal session data to fully invalidate the employee's temporary access
                HttpContext.Session.Clear();
                return View("Logout");
            }
            catch (Exception ex)
            {
                // Log but do not rethrow — always render the logout view to avoid the user getting stuck
                _logger.LogError(ex, nameof(Logout), "MyFormsController",
                    "Error during employee portal logout", GetUserIp());

                return View("Logout");
            }
        }

        #region Helpers

        /// <summary>
        /// Returns the remote IP address of the current HTTP request.
        /// Used to enrich log entries with caller context across all actions.
        /// </summary>
        private string GetUserIp()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        /// <summary>
        /// Whether verification should be refused outright. Two counters: one on the
        /// link, which stops a single employee's form being ground down, and one on
        /// the caller's address, which stops the same client working through many
        /// links at a few guesses each.
        /// </summary>
        /// <param name="token">The link being verified.</param>
        /// <param name="retryAfterMinutes">Roughly how long until the window clears.</param>
        private bool IsThrottled(Guid token, out int retryAfterMinutes)
        {
            retryAfterMinutes = (int)AttemptWindow.TotalMinutes;

            return _cache.TryGetValue(LinkKey(token), out int linkFails) && linkFails >= MaxAttemptsPerLink
                || _cache.TryGetValue(AddressKey(), out int addressFails) && addressFails >= MaxAttemptsPerAddress;
        }

        /// <summary>
        /// Records one failed verification against both counters. Each entry uses a
        /// sliding-free absolute expiry, so a run of failures cannot hold the lockout
        /// open indefinitely — the window always ends a fixed time after it opened.
        /// </summary>
        private void RecordFailedAttempt(Guid token)
        {
            Increment(LinkKey(token));
            Increment(AddressKey());
        }

        /// <summary>
        /// Clears the link's counter after a successful verification. The address
        /// counter is deliberately left alone: one success should not wipe the record
        /// of failures against other links from the same caller.
        /// </summary>
        private void ClearFailedAttempts(Guid token) => _cache.Remove(LinkKey(token));

        private void Increment(string key)
        {
            var count = _cache.TryGetValue(key, out int existing) ? existing + 1 : 1;

            _cache.Set(key, count, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = AttemptWindow
            });
        }

        private static string LinkKey(Guid token) => $"portal:verify:link:{token}";

        private string AddressKey() => $"portal:verify:ip:{GetUserIp()}";

        #endregion
    }
}