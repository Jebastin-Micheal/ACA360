using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.BusinessLogic.Validators;
using ACA360.Core.Constants;
using ACA360.Core.Extensions;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Handles all account-related operations including authentication,
    /// user profile management, MFA flow, and session/preference management.
    /// </summary>
    public class AccountController : BaseController
    {
        private readonly IAccountService _accountService;
        private readonly ILoggerService _logger;
        private readonly IFilingYearService _filingyearservice;
        private readonly IEmployerService _employerService;
        private readonly IUserTrackingService _trackingService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly UserManager<UserModel> _userManager;
        private readonly SignInManager<UserModel> _signInManager;
        private readonly ISystemSettingsService _systemSettingsService;

        /// <summary>
        /// Initializes the AccountController with all required service dependencies.
        /// ILogger is provided via constructor injection for structured error logging.
        /// </summary>
        public AccountController(
            IAccountService accountService,
            ILoggerService logger,
            IFilingYearService filingYearService,
            IEmployerService employerService,
            IUserTrackingService trackingService,
            IWebHostEnvironment webHostEnvironment,
            UserManager<UserModel> userManager,
            SignInManager<UserModel> signInManager, ISystemSettingsService systemSettingsService)

        {
            _accountService = accountService;
            _logger = logger;
            _filingyearservice = filingYearService;
            _employerService = employerService;
            _webHostEnvironment = webHostEnvironment;
            _trackingService = trackingService;
            _userManager = userManager;
            _signInManager = signInManager;
            _systemSettingsService = systemSettingsService;
        }

        #region Authentication (Login/Logout)

        /// <summary>
        /// GET: Renders the Login page.
        /// Redirects authenticated users directly to the Dashboard to prevent redundant login.
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login()
        {
            try
            {
                // FIX (Issue 1): Added null-conditional to Identity to safely check authentication status
                if (User?.Identity?.IsAuthenticated == true)
                {
                    return RedirectToAction("Index", "Dashboard");
                }

                if (TempData["SuccessMessage"] != null)
                    ViewData["SuccessMessage"] = TempData["SuccessMessage"];

                return View(new LoginViewModel());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Login), "AccountController", "Error rendering Login page", GetUserIp());
                return StatusCode(500, "An unexpected error occurred while loading the login page.");
            }
        }

        /// <summary>
        /// POST: Processes the login form submission.
        /// Validates credentials, handles Remember Me cookie, routes MFA users to the MFA step,
        /// builds claims identity, sets up session state, and resolves employer/filing year context.
        /// </summary>
        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);



                // 1. Find the user
                var userAccount = await _accountService.LoginAsync(model.Username, model.Password);
                //var userAccount = await _userManager.FindByNameAsync(model.Username);

                if (userAccount?.IsLockedOut == true)
                {
                    ViewData["ValidateMessage"] = "Account locked. Please try again after 15 minutes.";
                    return View(model);
                }

                if (userAccount == null)
                {
                    ViewData["ValidateMessage"] = "Invalid login attempt.";
                    return View(model);
                }



                if (userAccount.RequirePasswordReset)
                {
                    HttpContext.Session?.SetString("PendingPasswordResetUser", userAccount.User_Name ?? "");
                    return RedirectToAction("SetNewPassword", "Account");
                }



                // F-01: this used to redirect to Account/MFA, an action that does not
                // exist and never has. No second-factor provider is implemented
                // anywhere in the solution — there is no authenticator-key storage,
                // and outbound email does not work either (see F-37), so an emailed
                // code was never an option. The redirect produced a 404 after a
                // SUCCESSFUL password check, with no sign-in cookie issued, which
                // left the account unable to reach Settings to turn the flag back off.
                //
                // Because the flag has never gated anything, honouring it cannot
                // "preserve" a protection that was never in force — it only locks
                // people out. So sign-in continues, and the attempt is recorded so
                // the gap is visible rather than silent.
                //
                // When a real second factor is built, this block is where the
                // challenge belongs. The toggles that set the flag now refuse to
                // enable it, so this should stop being reachable once the existing
                // rows are cleared.
                if (userAccount.IsMFA == 1)
                {
                    _logger.LogError(
                        new NotImplementedException("MFA flag set but no second-factor provider exists"),
                        nameof(Login), "AccountController",
                        $"User '{userAccount.User_Name}' has isMFA=1. Signing in without a second factor " +
                        "because no MFA provider is implemented. Clear the flag or implement the challenge.",
                        GetUserIp());
                }
                var claims = new List<Claim>
{
new Claim(ClaimTypes.Name, userAccount.User_Name ?? "Unknown"),
new Claim("Profile_Name", userAccount.Profile_Name ?? "Unknown"),
new Claim(ClaimTypes.NameIdentifier, userAccount.Ref_ID?.ToString() ?? string.Empty),
new Claim(ClaimTypes.Role, userAccount.Role_Name ?? "User"),
new Claim("UserId", userAccount.User_ID?.ToString() ?? string.Empty),
            //new Claim("OriginalUserId", userAccount.User_ID?.ToString() ?? string.Empty),
            new Claim("ProfilePicture", userAccount.Profile_Picture ?? "")
};
                // Check for Impersonation context from Session or existing claims (if re-logging)
                var originalUserId = User?.FindFirst("OriginalUserId")?.Value;
                if (!string.IsNullOrEmpty(originalUserId))
                {
                    claims.Add(new Claim("OriginalUserId", originalUserId));
                }
                var Profile_Name = User?.FindFirst("Profile_Name")?.Value;
                if (!string.IsNullOrEmpty(Profile_Name))
                {
                    claims.Add(new Claim("Profile_Name", Profile_Name));
                }


                if (userAccount.Permissions != null)
                {
                    foreach (var permission in userAccount.Permissions)
                    {
                        if (!string.IsNullOrWhiteSpace(permission?.Controller) &&
                        !string.IsNullOrWhiteSpace(permission?.Action))
                        {
                            claims.Add(new Claim("Permission", $"{permission.Controller}:{permission.Action}"));
                        }
                    }
                }



                await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = model.RememberMe ? DateTime.UtcNow.AddDays(30) : null
                });



                var filingYears = await _filingyearservice.GetAllFilingYears();
                HttpContext.Session?.SetObject("FilingYear", filingYears);

                if (userAccount.Role_Name == UserRoles.DataAnalyst)
                {
                    int defaultYear = (int)(filingYears?.Any() == true
                        ? filingYears.Max(y => y.FilingYear)
                        : DateTime.Now.Year);
                    SetCurrentFilingYear(defaultYear);

                    return RedirectToAction("Index", "Dashboard");
                }
                //// Super Admins manage site settings/roles, not employer data, so they skip the
                //// employer/filing-year context entirely.
                //if (userAccount.Role_Name == UserRoles.SuperAdmin)
                //{
                //    return RedirectToAction("Index", "Dashboard");
                //}
               
                //if (userAccount.Role_Name == UserRoles.SuperAdmin)
                //{
                //    int defaultYear = (int)(filingYears?.Any() == true
                //        ? filingYears.Max(y => y.FilingYear)
                //        : DateTime.Now.Year);
                //    SetCurrentFilingYear(defaultYear);

                //    return RedirectToAction("Index", "SelectEmployer");
                //}
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
                var agent = Request.Headers["User-Agent"].ToString() ?? "Unknown";
                var sessionId = HttpContext.Session?.Id ?? "No-Session";



                // int logId = await _trackingService.LogLoginAsync(userAccount.User_ID?.ToString() ?? string.Empty, ip, agent, sessionId, originalUserId);
                // HttpContext.Session?.SetInt32("UserLogId", logId);



                var prefs = await _trackingService.GetUserPreferenceAsync(userAccount.User_ID?.ToString() ?? string.Empty);



                string userIdStr = userAccount.User_ID?.ToString() ?? string.Empty;
                var assignedEmployers = await _employerService.GetEmployersForUserAsync(userIdStr, userAccount.Role_Name ?? "");



                // FIX (Issue 7): Used .HasValue and Value to safely handle nullable value types
                //if (prefs != null && prefs.LastSelectedEmployerId.HasValue)
                //{
                //    var employer = await _employerService.GetEmployerByIdAsync(prefs.LastSelectedEmployerId.Value.ToString());
                //    if (employer != null)
                //    {
                //        SetCurrentEmployerId(employer.Id?.ToString()??string.Empty);
                //        SetCurrentEmployerName(employer.Name ?? "");



                //        // (Legacy fix integration)
                //        SetCurrentFilingYear(prefs.LastSelectedFilingYear.ToString() ?? DateTime.Now.Year.ToString());





                //        return RedirectToAction("Index", "Dashboard");
                //    }
                //}



                if (prefs != null && prefs.LastSelectedEmployerId.HasValue)
                {
                    string lastSelectedId = prefs.LastSelectedEmployerId.Value.ToString();



                    // SECURITY CHECK: Do they still have access to this employer?
                    bool stillHasAccess = assignedEmployers?.Any(e => e.Id == lastSelectedId) == true;



                    if (stillHasAccess)
                    {
                        var employer = await _employerService.GetEmployerByIdAsync(lastSelectedId);
                        if (employer != null)
                        {
                            SetCurrentEmployerId(employer.Id?.ToString() ?? string.Empty);
                            SetCurrentEmployerName(employer.Name ?? "");
                            SetCurrentFilingYear(prefs.LastSelectedFilingYear.ToString() ?? DateTime.Now.Year.ToString());



                            return RedirectToAction("Index", "Dashboard");
                        }
                    }
                }





                if (assignedEmployers?.Any() == true && assignedEmployers.Count() == 1)
                {
                    var emp = assignedEmployers.First();
                    SetCurrentEmployerId(emp.Id ?? "0");
                    SetCurrentEmployerName(emp.Name ?? "");
                    int selectedYear = (int)(filingYears?.Any() == true
                    ? filingYears.Max(y => y.FilingYear)
                    : DateTime.Now.Year);
                    SetCurrentFilingYear(selectedYear);
                    // Employer logins can belong to a Primary/Affiliate family. Cache the family
                    // list and whether this login is the Primary so the navbar can render either
                    // a switcher (Primary) or a disabled affiliate label (Affiliate).
                    if (userAccount.Role_Name == UserRoles.Employer && !string.IsNullOrEmpty(emp.Id))
                    {
                        var family = await _employerService.GetEmployerFamilyAsync(emp.Id);
                        HttpContext.Session?.SetObject("EmployerFamily", family);
                        var selfInFamily = family.FirstOrDefault(f => f.Id == emp.Id);
                        bool isPrimary = selfInFamily == null || selfInFamily.CompanyId == null || selfInFamily.CompanyId == 0;
                        HttpContext.Session?.SetString("IsPrimaryEmployer", isPrimary.ToString());
                    }
                    return RedirectToAction("Index", "Dashboard");
                }




                return RedirectToAction("Index", "SelectEmployer");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Login), "AccountController", "Login failed unexpectedly", GetUserIp());
                ViewData["ValidateMessage"] = "An unexpected error occurred. Please try again.";
                return View(model);
            }
        }

        /// <summary>
        /// GET/POST: Logs the user out by clearing the auth cookie, session, and all response cookies.
        /// Always redirects to the Login page regardless of outcome to ensure clean state.
        /// </summary>
        public async Task<IActionResult> Logout()
        {
            try
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                HttpContext.Session?.Clear();

                if (Request?.Cookies != null)
                {
                    foreach (var cookie in Request.Cookies.Keys)
                    {
                        Response.Cookies.Delete(cookie);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Logout), "AccountController", "Logout encountered an error", GetUserIp());
            }

            return RedirectToAction("Login");
        }

        /// <summary>
        /// GET: Renders the Access Denied view when a user attempts to reach an unauthorized resource.
        /// </summary>
        [AllowAnonymous]
        public IActionResult AccessDenied() => View();

        #endregion

        #region Profile

        /// <summary>
        /// GET: Loads the authenticated user's profile page.
        /// Accepts an optional tab parameter to pre-select a specific profile section (default: "account").
        /// Redirects to Login if the profile cannot be resolved, protecting against orphaned sessions.
        /// </summary>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> MyProfile(string tab = "account")
        {
            try
            {
                string userId = GetCurrentUserId();
                var userProfile = await _accountService.GetUserProfileAsync(userId);

                if (userProfile == null)
                    return RedirectToAction("Login");

                userProfile.ActiveTab = string.IsNullOrEmpty(tab) ? "account" : tab;
                userProfile.PasswordModel = new ChangePasswordViewModel();

                return View(userProfile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(MyProfile), "AccountController", "Error loading user profile", GetUserIp());
                return RedirectToAction("Error", "Home");
            }
        }

        #endregion

        #region Session Helpers

        /// <summary>
        /// POST: Stores an arbitrary key-value pair in the server-side session.
        /// Used by client-side JavaScript to persist UI state (e.g. selected employer, filing year).
        /// Returns 400 if the key is missing; 500 on unexpected failure.
        /// </summary>

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetSessionData([FromBody] SessionDataDto data)
        {
            try
            {
                if (string.IsNullOrEmpty(data?.Key))
                    return BadRequest("Key is required.");

                string normalKey = data.Key.Trim();
                string normalValue = data.Value?.Trim() ?? "";

                // Allow-list. Employer scope is deliberately excluded: it is set only by
                // SelectEmployerController.SelectThis, which verifies the caller's assignment.
                if (!normalKey.Equals("SelectedFilingYear", StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Unsupported session key.");

                string yearToSave = int.TryParse(normalValue, out int parsedYear)
                    ? parsedYear.ToString()
                    : DateTime.Now.Year.ToString();

                HttpContext.Session?.SetString("SelectedFilingYear", yearToSave);

                // BUG FIX: this previously wrote "SelectedEmployerID" (capital D) while
                // BaseController reads "SelectedEmployerId". The clear-on-year-change never
                // fired, so switching filing year left the previous employer selected.
                HttpContext.Session?.SetString("SelectedEmployerId", "");
                HttpContext.Session?.SetString("SelectedEmployerName", "");

                return Ok(new { Status = "Success", Saved = data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SetSessionData), "AccountController", "Failed to set session data", GetUserIp());
                return StatusCode(500, "Failed to update session.");
            }
        }

        /// <summary>
        /// GET: Retrieves the currently selected filing year from the session.
        /// Falls back to the current calendar year if the session value is absent.
        /// </summary>
        [Authorize]
        [HttpGet("Account/GetSessionValue")]
        public IActionResult GetSessionValue(string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return BadRequest("Key is required.");

                string normalKey = key.Trim();

                // Centralized safety check for Filing Year lookups
                if (normalKey.Equals("SelectedFilingYear", StringComparison.OrdinalIgnoreCase))
                {
                    // 1. Try reading as a string (Matches BaseController logic)
                    var stringVal = HttpContext?.Session.GetString("SelectedFilingYear");
                    if (int.TryParse(stringVal, out int parsedStringYear))
                    {
                        return Ok(parsedStringYear.ToString());
                    }

                    // 2. Try reading as an Int32 fallback (Matches BaseController logic)
                    var intVal = HttpContext?.Session.GetInt32("SelectedFilingYear");
                    if (intVal.HasValue)
                    {
                        return Ok(intVal.Value.ToString());
                    }

                    // 3. Ultimate Fallback: Return current calendar year if nothing is found
                    return Ok(DateTime.UtcNow.Year.ToString());
                }

                // Standard Fallback: Read as a normal string for all other layout/UI context keys
                var sessionValue = HttpContext?.Session.GetString(key);
                return Ok(sessionValue ?? "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetSessionValue), "AccountController", "Failed to retrieve session value", GetUserIp());
                return StatusCode(500, "Error retrieving session value.");
            }
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Extracts the current authenticated user's integer ID from their claims.
        /// Returns "0" as a safe default if the claim is absent or unparseable.
        /// </summary>
        public new string GetCurrentUserId()
        {
            var claim = User?.FindFirst("UserId");

            if (claim != null && int.TryParse(claim.Value, out int id))
            {
                return id.ToString();
            }

            return "0";
        }


        /// <summary>
        /// Returns the remote IP address of the current HTTP request.
        /// Used throughout the controller to enrich log entries with caller context.
        /// </summary>
        private new string GetUserIp()
        {
            return HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> SetNewPassword()
        {
            var pendingUser = HttpContext.Session?.GetString("PendingPasswordResetUser");
            if (string.IsNullOrEmpty(pendingUser))
                return RedirectToAction("Login");

            ViewBag.PasswordPolicy = await _systemSettingsService.GetSecuritySettingsAsync();
            return View(new SetNewPasswordViewModel { Username = pendingUser });
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetNewPassword(SetNewPasswordViewModel model)
        {
            try
            {
                var pendingUser = HttpContext.Session?.GetString("PendingPasswordResetUser");
                if (string.IsNullOrEmpty(pendingUser))
                    return RedirectToAction("Login");

                // ★ fetch policy first, so it's available for both success/failure re-render
                var policy = await _systemSettingsService.GetSecuritySettingsAsync();
                ViewBag.PasswordPolicy = policy;

                if (!ModelState.IsValid)
                    return View(model);

                if (model.NewPassword != model.ConfirmPassword)
                {
                    ModelState.AddModelError("", "Passwords do not match.");
                    return View(model);
                }

                var password = model.NewPassword ?? string.Empty;

                var errors = PasswordPolicyValidator.Validate(password, policy);
                if (errors.Any())
                {
                    foreach (var e in errors)
                        ModelState.AddModelError("", e);
                    return View(model);
                }
                // ------------------------------------------------------------

                bool success = await _accountService.SetNewPasswordAsync(pendingUser, model.NewPassword);
                if (!success)
                {
                    ViewData["ValidateMessage"] = "Unable to update password. Please try again.";
                    ModelState.AddModelError("", "Unable to update password. Please try again.");
                    return View(model);
                }

                HttpContext.Session?.Remove("PendingPasswordResetUser");
                TempData["SuccessMessage"] = "Password updated successfully. Please login with your new password.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SetNewPassword), "AccountController", "Error setting new password", GetUserIp());
                ViewData["ValidateMessage"] = "An unexpected error occurred. Please try again.";
                return View(model);
            }
        }

        #endregion
    }
}