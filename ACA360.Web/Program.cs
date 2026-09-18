using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Parsing;
using ACA360.BusinessLogic.Services;
using ACA360.Core.Models;
using ACA360.Data.Helpers;
using ACA360.Logging.Interfaces;
using ACA360.Logging.Services;
using ACA360.Repositories.Implementations;
using ACA360.Repositories.Interfaces;
using ACA360.Security.Authentication;
using ACA360.Security.Authorization;
using ACA360.Security.Encryption;
using ACA360.Security.Interfaces;
using ACA360.Security.Models;
using ACA360.Web.Builders;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OfficeOpenXml;
using System.Data;
using System.Text;
var builder = WebApplication.CreateBuilder(args);

// ======================================================
// 1. CONFIG
// ======================================================

var config = builder.Configuration;

var connectionString = config.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string missing");

var jwtSettings = config.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT settings missing");

var encryptionKey = config["Encryption:Key"]
    ?? throw new InvalidOperationException("Encryption key missing");

builder.Services.Configure<JwtSettings>(config.GetSection("JwtSettings"));

// ======================================================
// 2. CORE
// ======================================================

//builder.Services.AddControllersWithViews();
builder.Services.AddControllersWithViews(options =>
{
    // Blocks every non-GET while a View As session is active.
    options.Filters.Add<ACA360.Web.Filters.ImpersonationReadOnlyFilter>();
});
builder.Services.AddHttpContextAccessor();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddMemoryCache();

// ======================================================
// 3. DATABASE
// ======================================================

// Keep both (for gradual migration)
builder.Services.AddScoped<IDbConnection>(sp =>
    new SqlConnection(connectionString));

builder.Services.AddScoped<DbHelper>();

// ======================================================
// 4. SECURITY
// ======================================================

builder.Services.AddScoped<IEncryptionService>(sp =>
    new AesEncryptionService(encryptionKey));

builder.Services.AddScoped<ITokenService, TokenService>();

builder.Services.AddIdentityCore<UserModel>(options =>
{
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 3;
})
// Add this line to specify the role type
.AddRoles<Role>()
.AddSignInManager()
.AddUserStore<ACA360.Security.Stores.DapperUserStore>()
.AddRoleStore<ACA360.Security.Stores.DapperRoleStore>()
.AddDefaultTokenProviders();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "ACA360Auth"; // Match old code
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30); // Old code was 30 mins
    options.SlidingExpiration = true; // Match old code
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminPolicy", policy =>
        policy.Requirements.Add(new CustomRequirement("Role", "Admin")));

    // Deny by default. Any endpoint without its own [Authorize] now requires an
    // authenticated user; genuinely public actions opt out with [AllowAnonymous].
    // Closes FlagsController, EmailController, TemplatesController,
    // MailFulfillmentReportController and eFileReportController, which carry no
    // authorization attribute of their own.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddSingleton<IAuthorizationHandler, CustomRequirementHandler>();
// ======================================================
// 5. INFRA
// ======================================================

builder.Services.AddScoped<ILoggerService, LoggerService>();
builder.Services.AddScoped<ICacheService, MemoryCacheService>();

// 🔥 FIX: HealthCheckService needs string
builder.Services.AddScoped<HealthCheckService>(sp =>
    new HealthCheckService(connectionString));

// ======================================================
// 6. REPOSITORIES (FACTORY FIX)
// ======================================================

builder.Services.AddScoped<ICommunicationRepository, CommunicationRepository>();

builder.Services.AddScoped<IDataProcessingRepository>(sp =>
    new DataProcessingRepository(connectionString, sp.GetRequiredService<ILogger<DataProcessingRepository>>()));
builder.Services.AddScoped<IAnomalyDetectionService>(sp => new AnomalyDetectionService(
        connectionString,
        sp.GetRequiredService<ILogger<AnomalyDetectionService>>() // Add the logger here
    ));
builder.Services.AddScoped<IDataTriageRepository>(sp =>
    new DataTriageRepository(connectionString));
builder.Services.AddScoped<IViewAsService>(sp =>
    new ViewAsService(
        connectionString,
        sp.GetRequiredService<IEmployerService>(),
        sp.GetRequiredService<IDataAuditlogService>(),
        sp.GetRequiredService<ILogger<ViewAsService>>()));
builder.Services.AddScoped<IFileUploadLogService>(sp =>
    new FileUploadLogRepository(connectionString));
builder.Services.AddScoped<IFileParsingService, FileParsingService>();
// Warns at upload when the file's periods do not cover the chosen filing year.
builder.Services.AddScoped<IPlanYearScanService, PlanYearScanService>();
builder.Services.AddScoped<IAssignmentService>(sp =>
    new AssignmentRepository(connectionString));
builder.Services.AddScoped<IFormGenerationService>(sp =>
new FormGenerationService(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<IWebHostEnvironment>(),
    connectionString,
    sp.GetRequiredService<IAzureBlobService>(),
     sp.GetRequiredService<ILogger<FormGenerationService>>()
));
builder.Services.AddTransient<IDownloadService>(sp =>
new DownloadService(
    sp.GetRequiredService<IConfiguration>(),
    connectionString,
    sp.GetRequiredService<IWebHostEnvironment>(),
    sp.GetRequiredService<IAzureBlobService>(),
    sp.GetRequiredService<ILogger<DownloadService>>()
));
builder.Services.AddScoped<IEmployerAssignmentService>(sp =>
    new EmployerAssignmentService(connectionString));
builder.Services.AddScoped<IPdfMergeService, PdfMergeService>();
builder.Services.AddScoped<IPaymentRepository>(sp =>
    new PaymentRepository(connectionString, sp.GetRequiredService<ILogger<PaymentRepository>>()));

builder.Services.AddScoped<IRuleService>(sp =>
    new RuleRepository(connectionString, sp.GetRequiredService<ILogger<RuleRepository>>()));
builder.Services.AddScoped<IUserRepository>(sp =>
    new UserRepository(connectionString));

builder.Services.AddScoped<IUserAccountsService, UserAccountsRepository>();

builder.Services.AddScoped<IUserTrackingService>(sp =>
    new UserTrackingService(connectionString));
// Increase multipart body limit to 125 MB (120 MB file + headers)
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 125_000_000;
});

builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 125_000_000;
});

// QuestPDF License
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Professional;
ExcelPackage.LicenseContext = LicenseContext.Commercial;
// --> FIX: PdfService Registration
builder.Services.AddTransient<IPdfService>(sp =>
  new PdfService(
        connectionString,
        sp.GetRequiredService<ISmartPdfService>(),
        sp.GetRequiredService<IACALogicService>(),
        sp.GetRequiredService<IEmployerService>(),
        sp.GetRequiredService<INotificationService>(),
        sp.GetRequiredService<IWebHostEnvironment>(),
        sp.GetRequiredService<IPdfMergeService>(),
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<IAzureBlobService>()

  ));
builder.Services.AddScoped<IFlagRepository, FlagRepository>();
builder.Services.AddScoped<IPartnerRepository, PartnerRepository>();
builder.Services.AddScoped<IProcessRepository, ProcessRepository>();
builder.Services.AddScoped<IServiceRepository, ServiceRepository>();
builder.Services.AddScoped<ITrackerEmployerRepository, TrackerEmployerRepository>();
builder.Services.AddScoped<ISalesRepRepository, SalesRepRepository>();

// ======================================================
// 7. BUSINESS SERVICES
// ======================================================
builder.Services.AddScoped<IPlanService>(sp =>
    new PlanService(connectionString, sp.GetRequiredService<ILoggerService>(), sp.GetRequiredService<IEncryptionService>(), sp.GetRequiredService<IDataAuditlogService>()));
builder.Services.AddScoped<IAccountService>(sp =>
    new AccountService(connectionString, sp.GetRequiredService<ILoggerService>()));
builder.Services.AddScoped<IEmployeeService>(sp =>
    new EmployeeService(connectionString, sp.GetRequiredService<ILoggerService>(), sp.GetRequiredService<IDataAuditlogService>(), sp.GetRequiredService<IFlagRepository>(), sp.GetRequiredService<IACALogicService>()));
builder.Services.AddScoped<IACALogicService>(sp =>
    new ACALogicService(
        connectionString,
        sp.GetRequiredService<ILogger<ACALogicService>>() // Add the logger here
    ));
// F-37: outbound mail. Nothing in the solution sent email before this — the only
// SmtpClient was the "test connection" button on System Settings, and the
// distribution path logged deliveries it never performed.
//
// AddHttpClient gives the handler pooling and lifetime management; a long-lived
// HttpClient held in a singleton would not pick up DNS changes.
builder.Services.AddHttpClient<IMailDeliveryService, SendGridMailDeliveryService>(client =>
{
    // SendGrid's own guidance. Long enough for a slow response, short enough that a
    // batch job does not stall on one unreachable call.
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddTransient<IDistributionService>(provider =>
    new DistributionService(
        connectionString,
        provider.GetRequiredService<ICommunicationRepository>(),
        provider.GetRequiredService<ISystemSettingsService>(),
        provider.GetRequiredService<IMailDeliveryService>(),
        provider.GetRequiredService<ILogger<DistributionService>>()
    ));
builder.Services.AddScoped<IEmployerService>(provider =>
    new EmployerService(connectionString, provider.GetRequiredService<ILoggerService>(),provider.GetRequiredService<IDataAuditlogService>()));
builder.Services.AddScoped<IApiClientService, ApiClientService>();

builder.Services.AddScoped<INotesService>(sp =>
    new NotesService(connectionString, sp.GetRequiredService<ILogger<NotesService>>()));

builder.Services.AddScoped<INotificationService>(sp =>
    new NotificationService(connectionString, sp.GetRequiredService<ILogger<NotificationService>>()));
builder.Services.AddTransient<IDashboardService>(provider =>
    new DashboardService(
        connectionString,
        provider.GetRequiredService<ILoggerService>(), provider.GetRequiredService<IEmployerService>()
    ));
builder.Services.AddScoped<IFilingYearService>(sp =>
    new FilingYearService(connectionString, sp.GetRequiredService<ILoggerService>()));
builder.Services.AddTransient<IFilingService>(sp =>
    new FilingService(
        connectionString,
        sp.GetRequiredService<IWebHostEnvironment>(),
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<IAzureBlobService>(),
        sp.GetRequiredService<ILogger<FilingService>>()
    ));
builder.Services.AddTransient<IIrsXmlService>(sp =>
    new IrsXmlService(
        connectionString,
        sp.GetRequiredService<IWebHostEnvironment>(),
        sp.GetRequiredService<IEmployerService>(),
        sp.GetRequiredService<IACALogicService>()
    ));
builder.Services.AddScoped<IUISettingsService>(sp => new UISettingsService(connectionString, sp.GetRequiredService<ILoggerService>()));
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IMenuService>(sp =>
    new MenuService(connectionString, sp.GetRequiredService<ILoggerService>()));
builder.Services.AddScoped<IReportService>(sp =>
    new ReportService(connectionString, sp.GetRequiredService<ILoggerService>()));
builder.Services.AddScoped<IPermissionService>(sp =>
    new PermissionService(connectionString, sp.GetRequiredService<ILoggerService>()));
builder.Services.AddScoped<IRoleService>(sp =>
    new RoleService(connectionString, sp.GetRequiredService<ILoggerService>(),sp.GetRequiredService<IWebHostEnvironment>()));
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();
builder.Services.AddScoped<IErrorTriageService, ErrorTriageService>();
builder.Services.AddTransient<FileDateAnalyzer>();
builder.Services.AddScoped<IFileUploadWorkflowService, FileUploadWorkflowService>();
builder.Services.AddTransient<ISmartPdfService, SmartPdfService>();
builder.Services.AddScoped<IExcelStructureValidatorService, ExcelStructureValidatorService>();
builder.Services.AddScoped<IFileProcessingService, FileProcessingService>();
builder.Services.AddScoped<IDataProcessingService, DataProcessingService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();

builder.Services.AddScoped<IAutomationService, AutomationService>();

builder.Services.AddScoped<IAzureBlobService, AzureBlobService>();

builder.Services.AddScoped<ISystemService, SystemService>();

builder.Services.AddScoped<IExportService, ExportService>();

//Shortcutservices..
builder.Services.AddScoped<IShortcutService, ShortcutService>();

//Calendarservices..
builder.Services.AddScoped<ICalendarService, CalendarService>();

//AMReport
builder.Services.AddScoped<IAmReportService, AmReportService>();

//DAReport
builder.Services.AddScoped<IDataAnalystReportService, DataAnalystReportService>();
builder.Services.AddScoped<IEmailRepository, EmailRepository>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IUserPreferenceService, UserPreferenceService>();

//  Consent/Employee-Portal feature — DI registrations (ConsentService + portal auth).
builder.Services.AddScoped<IConsentService>(provider =>
    new ConsentService(
        connectionString,
        provider.GetRequiredService<ILogger<ConsentService>>()
    ));
builder.Services.AddScoped<IEmployeePortalAuthService>(provider =>
    new EmployeePortalAuthService(
        connectionString,
        provider.GetRequiredService<ILogger<EmployeePortalAuthService>>()
    ));
// end Consent/Employee-Portal feature


// ACA Tracking
builder.Services.AddScoped<IAcaTrackingService>(sp =>
    new AcaTrackingService(
        connectionString,
        sp.GetRequiredService<ICommunicationRepository>(),
        sp.GetRequiredService<ILogger<AcaTrackingService>>()
    ));


// ======================================================
// 8. VIEW MODELS
// ======================================================

builder.Services.AddScoped<EmployerViewModelBuilder>();
builder.Services.AddScoped<EmployeeViewModelBuilder>();
builder.Services.AddScoped<FileUploadViewModelBuilder>();

// ======================================================
// 9. HANGFIRE
// ======================================================

builder.Services.AddHangfire(config => config
    .UseSqlServerStorage(connectionString));

builder.Services.AddHangfireServer();

// ======================================================
//  UPPERCASE BINDERS & CONVERTERS
// ======================================================

builder.Services.AddControllersWithViews(options =>
{
    // 1. This handles standard HTML Forms (your existing code)
    options.ModelBinderProviders.Insert(0, new UppercaseStringModelBinderProvider());
})
.AddJsonOptions(options =>
{
    // 2. This handles AJAX / API / JSON requests
    options.JsonSerializerOptions.Converters.Add(new UppercaseStringJsonConverter());
});
builder.Services.AddScoped<IDataAuditlogService, DataAuditlogService>();
builder.Services.AddScoped<IFaqService, FaqService>();
// ======================================================
// 10 ApplicationInfo
// ======================================================

builder.Services.Configure<ApplicationInfo>(
    builder.Configuration.GetSection(ApplicationInfo.SectionName));

// ======================================================
// 11. BUILD
// ======================================================

var app = builder.Build();

// ======================================================
// 11. PIPELINE
// ======================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    //context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    await next();
});

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new ACA360.Web.Filters.HangfireAuthorizationFilter() }
});
// Releases files left at "Importing" by a job that never reported a result.
// Hourly is deliberate: the sweeper only ever touches files already well past
// any plausible run time, so there is nothing to gain from checking often.
RecurringJob.AddOrUpdate<IDataProcessingService>(
    "recover-stuck-imports",
    svc => svc.RecoverStuckImportsAsync(90),
    Cron.Hourly);

// Consent/Employee-Portal feature — MVC area route (MUST be before the default route).
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();