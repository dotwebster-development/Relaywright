using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Identity;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Delivery;
using Relaywright.Web.Services.Diagnostics;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Services.Smtp;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();
builder.Host.UseSystemd();

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<BootstrapAdminOptions>(builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));

var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
var appPaths = new AppPaths(builder.Environment.ContentRootPath, storageOptions);
appPaths.EnsureCreated();

builder.Services.AddSingleton(appPaths);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(appPaths.KeyRingDirectory))
    .SetApplicationName("Relaywright");

builder.Services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlite($"Data Source={appPaths.DatabasePath}"));
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

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
});

builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddSingleton<IOperationalEventService, OperationalEventService>();
builder.Services.AddSingleton<IRuntimeConfigurationNotifier, RuntimeConfigurationNotifier>();
builder.Services.AddSingleton<IQueueSignal, QueueSignal>();
builder.Services.AddSingleton<IRelayConfigurationService, RelayConfigurationService>();
builder.Services.AddSingleton<ITrustedNetworkService, TrustedNetworkService>();
builder.Services.AddSingleton<IMessageSpoolService, MessageSpoolService>();
builder.Services.AddSingleton<IMessageMetadataService, MessageMetadataService>();
builder.Services.AddSingleton<RetryDelayCalculator>();
builder.Services.AddSingleton<IMessageQueueService, MessageQueueService>();
builder.Services.AddSingleton<DeliveryFailureClassifier>();
builder.Services.AddSingleton<MicrosoftOAuthTokenProvider>();
builder.Services.AddSingleton<IUpstreamAuthenticationService, UpstreamAuthenticationService>();
builder.Services.AddSingleton<IUpstreamDeliveryService, UpstreamDeliveryService>();
builder.Services.AddSingleton<IUpstreamConnectivityTester, UpstreamConnectivityTester>();
builder.Services.AddSingleton<IUpstreamTestEmailSender, UpstreamTestEmailSender>();
builder.Services.AddSingleton<SmtpOptionsFactory>();
builder.Services.AddSingleton<RelayMessageStore>();
builder.Services.AddSingleton<TrustedNetworkMailboxFilter>();
builder.Services.AddSingleton<DataSeeder>();
builder.Services.AddHttpClient();

builder.Services.AddHostedService<SmtpRelayHostedService>();
builder.Services.AddHostedService<QueueDeliveryWorker>();
builder.Services.AddHostedService<MaintenanceWorker>();

var app = builder.Build();

app.Logger.LogInformation(
    "Starting Relaywright. Environment={Environment}; ContentRoot={ContentRoot}; DataDirectory={DataDirectory}; DatabasePath={DatabasePath}; SpoolRoot={SpoolRoot}; KeyRing={KeyRing}",
    app.Environment.EnvironmentName,
    app.Environment.ContentRootPath,
    appPaths.DataDirectory,
    appPaths.DatabasePath,
    appPaths.SpoolRootDirectory,
    appPaths.KeyRingDirectory);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();
    var path = context.Request.Path.HasValue ? context.Request.Path.Value : "/";
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Relaywright.Web.Requests");

    logger.LogDebug(
        "HTTP request started. Method={Method}; Path={Path}; RemoteIp={RemoteIp}",
        context.Request.Method,
        path,
        context.Connection.RemoteIpAddress?.ToString());

    try
    {
        await next();

        var level = context.Response.StatusCode >= 500
            ? LogLevel.Error
            : context.Response.StatusCode >= 400
                ? LogLevel.Warning
                : LogLevel.Information;

        logger.Log(
            level,
            "HTTP request completed. Method={Method}; Path={Path}; StatusCode={StatusCode}; ElapsedMs={ElapsedMs}; User={UserName}",
            context.Request.Method,
            path,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : "anonymous");
    }
    catch (Exception exception)
    {
        logger.LogError(
            exception,
            "HTTP request failed. Method={Method}; Path={Path}; ElapsedMs={ElapsedMs}",
            context.Request.Method,
            path,
            stopwatch.ElapsedMilliseconds);
        throw;
    }
});

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.InitializeAsync(CancellationToken.None);
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapGet("/health", () => Results.Json(new { status = "ok" })).AllowAnonymous();

app.MapGet("/health/details", async (
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    AppPaths paths,
    IRelayConfigurationService relayConfigurationService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("Relaywright.Web.Health");
    var checks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var healthy = true;

    try
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        healthy &= await dbContext.Database.CanConnectAsync(cancellationToken);
        checks["database"] = healthy ? "ok" : "unavailable";
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "Health check database probe failed.");
        healthy = false;
        checks["database"] = exception.GetType().Name;
    }

    try
    {
        Directory.CreateDirectory(paths.SpoolRootDirectory);
        var healthDirectory = Path.Combine(paths.SpoolRootDirectory, ".health");
        Directory.CreateDirectory(healthDirectory);
        var probePath = Path.Combine(healthDirectory, $"{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
        File.Delete(probePath);
        checks["spool"] = "ok";
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "Health check spool probe failed. SpoolRoot={SpoolRoot}", paths.SpoolRootDirectory);
        healthy = false;
        checks["spool"] = exception.GetType().Name;
    }

    try
    {
        var configuration = await relayConfigurationService.GetSnapshotAsync(cancellationToken);
        checks["configuration"] = configuration.ListenerPort is >= 1 and <= 65535
            ? "ok"
            : "invalid listener port";
        healthy &= checks["configuration"] == "ok";
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "Health check configuration probe failed.");
        healthy = false;
        checks["configuration"] = exception.GetType().Name;
    }

    if (!healthy)
    {
        logger.LogWarning(
            "Health check degraded. Database={Database}; Spool={Spool}; Configuration={Configuration}",
            checks.GetValueOrDefault("database"),
            checks.GetValueOrDefault("spool"),
            checks.GetValueOrDefault("configuration"));
    }

    return Results.Json(
        new { status = healthy ? "ok" : "degraded", checks },
        statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
}).RequireAuthorization();

app.Run();
