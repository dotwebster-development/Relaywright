using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Identity;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Alerts;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Delivery;
using Relaywright.Web.Services.Diagnostics;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Services.Smtp;
using Relaywright.Web.Services.Updates;
using Relaywright.Web.Validation;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();
builder.Host.UseSystemd();

var startupOptions = builder.Services.AddRelaywrightOptions(builder.Configuration);
var storageOptions = startupOptions.Storage;
var appPaths = new AppPaths(builder.Environment.ContentRootPath, storageOptions);
appPaths.EnsureCreated();
var databaseOptions = startupOptions.Database;
var databaseConfiguration = DatabaseConfiguration.Create(databaseOptions, appPaths);
if (databaseConfiguration.IsSqlite)
{
    BackupRestoreFileSystem.ApplyPendingRestore(appPaths);
}

var startupDataProtectionProvider = DataProtectionProvider.Create(
    new DirectoryInfo(appPaths.KeyRingDirectory),
    options => options.SetApplicationName("Relaywright"));
var configuredAdminWebListener = AdminWebListenerConfigurationService.LoadConfiguration(appPaths);
if (configuredAdminWebListener is not null)
{
    builder.WebHost.UseUrls(configuredAdminWebListener.GetUrls());
    builder.Services.AddHttpsRedirection(options =>
    {
        options.HttpsPort = configuredAdminWebListener.HttpsPort;
    });
}

var configuredAdminHttpsCertificate = AdminHttpsCertificateService.LoadConfiguredCertificate(appPaths, startupDataProtectionProvider);
if (configuredAdminHttpsCertificate is not null)
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ConfigureHttpsDefaults(httpsOptions =>
        {
            httpsOptions.ServerCertificate = configuredAdminHttpsCertificate;
        });
    });
}

builder.Services.AddSingleton(appPaths);
builder.Services.AddSingleton(databaseConfiguration);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(appPaths.KeyRingDirectory))
    .SetApplicationName("Relaywright");

builder.Services.AddDbContextFactory<ApplicationDbContext>(options => databaseConfiguration.Configure(options));
builder.Services.AddScoped(serviceProvider =>
    serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;
});
builder.Services.Configure<SecurityStampValidatorOptions>(AdminSecurityDefaults.ConfigureSecurityStampValidator);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Setup");
});

builder.Services
    .AddRelaywrightApplicationServices()
    .AddRelaywrightHostedServices();

var app = builder.Build();

app.Logger.LogInformation(
    "Starting Relaywright. Environment={Environment}; ContentRoot={ContentRoot}; DataDirectory={DataDirectory}; DatabaseProvider={DatabaseProvider}; Database={Database}; SpoolRoot={SpoolRoot}; KeyRing={KeyRing}",
    app.Environment.EnvironmentName,
    app.Environment.ContentRootPath,
    appPaths.DataDirectory,
    databaseConfiguration.Provider,
    databaseConfiguration.Description,
    appPaths.SpoolRootDirectory,
    appPaths.KeyRingDirectory);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRelaywrightHttpPipeline();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.InitializeAsync(CancellationToken.None);
    var restartService = scope.ServiceProvider.GetRequiredService<IApplicationRestartService>();
    await restartService.ClearAppliedRestartIfNeededAsync(CancellationToken.None);
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapGet("/health", () => Results.Json(new { status = "ok" })).AllowAnonymous();

app.MapGet("/health/details", async (
    DetailedHealthService healthService,
    CancellationToken cancellationToken) =>
{
    var snapshot = await healthService.CheckAsync(cancellationToken);
    return Results.Json(
        new
        {
            status = snapshot.Healthy ? "ok" : "degraded",
            version = ApplicationVersion.DisplayVersion,
            informationalVersion = ApplicationVersion.InformationalVersion,
            checks = snapshot.Checks
        },
        statusCode: snapshot.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
}).RequireAuthorization();

app.Run();
