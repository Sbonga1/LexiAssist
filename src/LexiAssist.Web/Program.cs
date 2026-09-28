using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();
var databaseProvider = builder.Configuration["Database:Provider"] ?? (isDevelopment ? "Sqlite" : string.Empty);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var allowedHosts = builder.Configuration["AllowedHosts"];
var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyPath"];
var learnerAudioStoragePath = builder.Configuration["LearnerAudioRecording:StorageDirectory"];
var screeningAudioStoragePath = builder.Configuration["ScreeningAudio:StorageDirectory"];
var questionImageStoragePath = builder.Configuration["QuestionImages:StorageDirectory"];

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
}

if (!isDevelopment)
{
    if (!string.Equals(databaseProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Database:Provider must be SqlServer outside Development. SQLite files are not supported for Azure production hosting.");
    }

    if (string.IsNullOrWhiteSpace(allowedHosts) || string.Equals(allowedHosts.Trim(), "*", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("AllowedHosts must be configured to the deployed host name outside Development.");
    }

    if (string.IsNullOrWhiteSpace(dataProtectionKeyPath) || !Path.IsPathRooted(dataProtectionKeyPath))
    {
        throw new InvalidOperationException("DataProtection:KeyPath must be configured as an absolute persistent path outside Development.");
    }

    if (string.IsNullOrWhiteSpace(learnerAudioStoragePath) || !Path.IsPathRooted(learnerAudioStoragePath))
    {
        throw new InvalidOperationException("LearnerAudioRecording:StorageDirectory must be configured as an absolute persistent path outside Development.");
    }

    if (string.IsNullOrWhiteSpace(screeningAudioStoragePath) || !Path.IsPathRooted(screeningAudioStoragePath))
    {
        throw new InvalidOperationException("ScreeningAudio:StorageDirectory must be configured as an absolute persistent path outside Development.");
    }

    if (string.IsNullOrWhiteSpace(questionImageStoragePath) || !Path.IsPathRooted(questionImageStoragePath))
    {
        throw new InvalidOperationException("QuestionImages:StorageDirectory must be configured as an absolute persistent path outside Development.");
    }
}

// Keep operational logs on the hosting process' standard output. This avoids
// an implicit Windows Event Log dependency and keeps logging hosting-neutral.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// SQLite remains the local-development provider. Production uses an independent
// Azure SQL migration set so provider-specific schema is never mixed.
if (string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
}
else if (string.Equals(databaseProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<AzureSqlApplicationDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 5);
            sql.MigrationsHistoryTable("__EFMigrationsHistory_AzureSql");
        }));
    builder.Services.AddScoped<ApplicationDbContext>(services =>
        services.GetRequiredService<AzureSqlApplicationDbContext>());
}
else
{
    throw new InvalidOperationException("Database:Provider must be either Sqlite or SqlServer.");
}

if (builder.Configuration.GetValue<bool>("AzureAppService:ForwardedHeaders"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        // App Service terminates TLS at its managed frontend and worker proxy
        // addresses are dynamic. Enable this only for an App Service deployment.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}
// Keep development keys local to the application while requiring an explicit,
// persistent absolute path for non-development deployments.
var keyDirectory = isDevelopment
    ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys")
    : Path.GetFullPath(dataProtectionKeyPath!);
Directory.CreateDirectory(keyDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("LexiAssist");
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = !isDevelopment;

        options.Password.RequiredLength = isDevelopment ? 10 : 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = !isDevelopment;

        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;

        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = isDevelopment
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    var identityPrincipalValidation = options.Events.OnValidatePrincipal;
    options.Events.OnValidatePrincipal = async context =>
    {
        if (identityPrincipalValidation is not null)
        {
            await identityPrincipalValidation(context);
        }

        if (context.Principal is not { } principal)
        {
            return;
        }

        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(principal);
        if (user?.IsActive == true)
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    };
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = isDevelopment
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddScoped<DatabaseInitializationService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAssessmentReadinessService, AssessmentReadinessService>();
builder.Services.AddHttpClient<IAssessmentDraftGenerator, OpenAiAssessmentDraftGenerator>();
builder.Services.AddScoped<AppNotificationService>();
builder.Services.AddScoped<IUserAccessibilityPreferenceService, UserAccessibilityPreferenceService>();
builder.Services.Configure<LearnerInterestOptions>(builder.Configuration.GetSection(LearnerInterestOptions.SectionName));
builder.Services.AddScoped<IUserInterestService, UserInterestService>();
builder.Services.AddScoped<IUserReminderPreferenceService, UserReminderPreferenceService>();
builder.Services.Configure<AdministratorAlertOptions>(builder.Configuration.GetSection(AdministratorAlertOptions.SectionName));
builder.Services.AddScoped<IAdministratorAlertService, AdministratorAlertService>();
builder.Services.AddSingleton<IPracticeContentGenerator, TemplatePracticeContentGenerator>();
builder.Services.AddScoped<IPracticeStoryService, PracticeStoryService>();
builder.Services.Configure<ScreeningScoringOptions>(builder.Configuration.GetSection("ScreeningScoring"));
builder.Services.AddSingleton<IScreeningScoringService, ScreeningScoringService>();
builder.Services.AddSingleton<IScreeningReportGenerationService, ScreeningReportGenerationService>();
builder.Services.Configure<ScreeningRiskClassificationOptions>(builder.Configuration.GetSection("ScreeningRiskClassification"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IScreeningRiskClassificationService, ScreeningRiskClassificationService>();
builder.Services.Configure<ActivityRecommendationOptions>(builder.Configuration.GetSection("ActivityRecommendations"));
builder.Services.AddScoped<ILearningActivityService, LearningActivityService>();
builder.Services.Configure<SoundBuilderOptions>(builder.Configuration.GetSection("SoundBuilder"));
builder.Services.AddScoped<ISoundBuilderGameService, SoundBuilderGameService>();
builder.Services.Configure<FastNameRaceOptions>(builder.Configuration.GetSection("FastNameRace"));
builder.Services.AddScoped<IFastNameRaceGameService, FastNameRaceGameService>();
builder.Services.Configure<WordReaderOptions>(builder.Configuration.GetSection("WordReader"));
builder.Services.AddSingleton<IWordReaderAttemptEvaluator, WordReaderSelfReportedAttemptEvaluator>();
builder.Services.AddScoped<IWordReaderGameService, WordReaderGameService>();
builder.Services.Configure<SyllableSlicerOptions>(builder.Configuration.GetSection("SyllableSlicer"));
builder.Services.AddScoped<ISyllableSlicerGameService, SyllableSlicerGameService>();
builder.Services.Configure<SayWriteMatchOptions>(builder.Configuration.GetSection("SayWriteMatch"));
builder.Services.AddScoped<ISayWriteMatchGameService, SayWriteMatchGameService>();
builder.Services.Configure<AdaptiveSupportOptions>(builder.Configuration.GetSection(AdaptiveSupportOptions.SectionName));
builder.Services.AddSingleton<AdaptiveSupportService>();
builder.Services.AddSingleton<ISupportiveFeedbackService, SupportiveFeedbackService>();
builder.Services.Configure<LearnerAudioRecordingOptions>(builder.Configuration.GetSection(LearnerAudioRecordingOptions.SectionName));
builder.Services.Configure<ScreeningAudioOptions>(builder.Configuration.GetSection(ScreeningAudioOptions.SectionName));
builder.Services.Configure<QuestionImagesOptions>(builder.Configuration.GetSection(QuestionImagesOptions.SectionName));
builder.Services.AddScoped<QuestionImageStorageService>();
builder.Services.AddScoped<ILearnerAudioRecordingService, LearnerAudioRecordingService>();
builder.Services.AddScoped<ListeningAudioService>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddSingleton<ScreeningErrorCategoryAnalyticsCalculator>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("AzureAppService:ForwardedHeaders"))
{
    app.UseForwardedHeaders();
}

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializationService>().InitializeAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    // Keep detailed diagnostics available for local development only.
    app.UseDeveloperExceptionPage();
}

// Render framework status responses through MVC so users receive a consistent,
// helpful page without framework internals or exception details.
app.UseStatusCodePagesWithReExecute("/Home/StatusCodePage", "?statusCode={0}");

// Local development profiles may intentionally expose HTTP only. Redirecting
// without a configured HTTPS endpoint produces an unusable request pipeline;
// production still enforces HTTPS below the development boundary.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    AllowCachingResponses = false
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program { }
