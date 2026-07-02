using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;
using Relaywright.Web.Options;
using Relaywright.Web.Pages.Account;
using Relaywright.Web.Services.Runtime;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class AdminSecurityVisibilityTests
{
    [Fact]
    public async Task LoginWritesSecurityEventForSuccessfulAttempt()
    {
        await using var fixture = await IdentityPageFixture.CreateAsync();
        await fixture.CreateUserAsync("admin", "Password12345");
        var model = fixture.CreateLoginModel();
        model.Input.UserName = "admin";
        model.Input.Password = "Password12345";
        model.Input.RememberMe = true;
        model.Input.ReturnUrl = "/Index";

        var result = await model.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Index", redirect.Url);
        var securityEvent = Assert.Single(fixture.Events.Events);
        Assert.Equal(OperationalEventCategory.Security, securityEvent.Category);
        Assert.Equal(EventSeverity.Information, securityEvent.Severity);
        Assert.Equal("Admin sign-in succeeded.", securityEvent.Message);
        Assert.Equal("203.0.113.10", securityEvent.RemoteIpAddress);
        Assert.Contains("UserName=admin", securityEvent.Detail, StringComparison.Ordinal);
        Assert.Contains("RememberMe=True", securityEvent.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Password12345", securityEvent.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginWritesSecurityEventForFailedAttempt()
    {
        await using var fixture = await IdentityPageFixture.CreateAsync();
        await fixture.CreateUserAsync("admin", "Password12345");
        var model = fixture.CreateLoginModel();
        model.Input.UserName = "admin";
        model.Input.Password = "wrong-password";

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        var securityEvent = Assert.Single(fixture.Events.Events);
        Assert.Equal(OperationalEventCategory.Security, securityEvent.Category);
        Assert.Equal(EventSeverity.Warning, securityEvent.Severity);
        Assert.Equal("Admin sign-in failed.", securityEvent.Message);
        Assert.Contains("Result=InvalidCredentials", securityEvent.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-password", securityEvent.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordChangeSignsOutAndRedirectsToLogin()
    {
        await using var fixture = await IdentityPageFixture.CreateAsync();
        var user = await fixture.CreateUserAsync("admin", "Password12345");
        var userManager = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var originalStamp = await userManager.GetSecurityStampAsync(user);
        var model = fixture.CreateChangePasswordModel(user);
        model.Input.CurrentPassword = "Password12345";
        model.Input.NewPassword = "NewPassword12345";
        model.Input.ConfirmPassword = "NewPassword12345";

        var result = await model.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Account/Login", redirect.PageName);
        Assert.Equal("Password changed. Sign in again with the new password.", model.StatusMessage);

        var updatedUser = await userManager.FindByIdAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.True(await userManager.CheckPasswordAsync(updatedUser, "NewPassword12345"));
        Assert.NotEqual(originalStamp, await userManager.GetSecurityStampAsync(updatedUser));

        var setCookie = model.HttpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains("Identity.Application", setCookie, StringComparison.Ordinal);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignOutAllSessionsRotatesStampSignsOutAndWritesSecurityEvent()
    {
        await using var fixture = await IdentityPageFixture.CreateAsync();
        var user = await fixture.CreateUserAsync("admin", "Password12345");
        var userManager = fixture.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var originalStamp = await userManager.GetSecurityStampAsync(user);
        var model = fixture.CreateChangePasswordModel(user);

        var result = await model.OnPostSignOutAllSessionsAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Account/Login", redirect.PageName);
        Assert.Equal("All admin sessions were signed out. Sign in again to continue.", model.StatusMessage);

        var updatedUser = await userManager.FindByIdAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.NotEqual(originalStamp, await userManager.GetSecurityStampAsync(updatedUser));

        var securityEvent = Assert.Single(fixture.Events.Events);
        Assert.Equal(OperationalEventCategory.Security, securityEvent.Category);
        Assert.Equal("Admin sessions invalidated.", securityEvent.Message);
        Assert.Equal("203.0.113.10", securityEvent.RemoteIpAddress);
        Assert.DoesNotContain("Password12345", securityEvent.Detail, StringComparison.Ordinal);

        var setCookie = model.HttpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains("Identity.Application", setCookie, StringComparison.Ordinal);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangePasswordLoadsLoginActivityAndRecoveryGuidance()
    {
        await using var fixture = await IdentityPageFixture.CreateAsync();
        var user = await fixture.CreateUserAsync("admin", "Password12345");
        fixture.SecurityActivity.LoginActivity = new AdminLoginActivitySummary(
            "admin",
            new AdminLoginObservation(DateTimeOffset.UtcNow.AddMinutes(-3), "admin", true, "Succeeded", false, "203.0.113.10"),
            new AdminLoginObservation(DateTimeOffset.UtcNow.AddMinutes(-1), "admin", false, "InvalidCredentials", false, "203.0.113.11"),
            1,
            2);
        var model = fixture.CreateChangePasswordModel(user);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal("admin", model.LoginActivity.UserName);
        Assert.Equal(1, model.LoginActivity.FailedLast24Hours);
        Assert.NotEmpty(model.RecoveryGuidance);
        Assert.Contains(model.RecoveryGuidance, x => x.Label == "Password recovery");
    }

    [Fact]
    public async Task SecurityActivityIgnoresMalformedLoginEventsAndKeepsMissingRemoteIp()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await AddSecurityEventAsync(
            store,
            AdminSecurityActivityService.SignInFailedMessage,
            now.AddMinutes(-1),
            "UserName=admin; Result=InvalidCredentials; RememberMe=False",
            remoteIpAddress: null);
        await AddSecurityEventAsync(
            store,
            AdminSecurityActivityService.SignInFailedMessage,
            now,
            "Result=InvalidCredentials; RememberMe=False",
            remoteIpAddress: "203.0.113.10");
        var service = new AdminSecurityActivityService(store.DbContextFactory);

        var summary = await service.GetLoginActivityAsync("admin", now, CancellationToken.None);

        Assert.Equal(1, summary.FailedLast24Hours);
        Assert.NotNull(summary.LastFailedLogin);
        Assert.Null(summary.LastFailedLogin.RemoteIpAddress);
    }

    [Fact]
    public async Task LoginActivitySummarySelectsLatestAndCountsFailures()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await AddSecurityEventAsync(
            store,
            AdminSecurityActivityService.SignInSucceededMessage,
            now.AddDays(-2),
            "UserName=admin; Result=Succeeded; RememberMe=True",
            "203.0.113.1");
        await AddSecurityEventAsync(
            store,
            AdminSecurityActivityService.SignInSucceededMessage,
            now.AddMinutes(-10),
            "UserName=admin; Result=Succeeded; RememberMe=False",
            "203.0.113.2");
        await AddSecurityEventAsync(
            store,
            AdminSecurityActivityService.SignInFailedMessage,
            now.AddHours(-2),
            "UserName=admin; Result=InvalidCredentials; RememberMe=False",
            "203.0.113.3");
        await AddSecurityEventAsync(
            store,
            AdminSecurityActivityService.SignInFailedMessage,
            now.AddDays(-3),
            "UserName=admin; Result=InvalidCredentials; RememberMe=False",
            "203.0.113.4");
        await AddSecurityEventAsync(
            store,
            AdminSecurityActivityService.SignInFailedMessage,
            now.AddHours(-1),
            "UserName=other; Result=InvalidCredentials; RememberMe=False",
            "203.0.113.5");
        var service = new AdminSecurityActivityService(store.DbContextFactory);

        var summary = await service.GetLoginActivityAsync("admin", now, CancellationToken.None);

        Assert.Equal("203.0.113.2", summary.LastSuccessfulLogin?.RemoteIpAddress);
        Assert.Equal("203.0.113.3", summary.LastFailedLogin?.RemoteIpAddress);
        Assert.Equal(1, summary.FailedLast24Hours);
        Assert.Equal(2, summary.FailedLast7Days);
    }

    [Fact]
    public async Task SuspiciousLoginSummaryFlagsConfiguredThresholds()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 10; i++)
        {
            await AddSecurityEventAsync(
                store,
                AdminSecurityActivityService.SignInFailedMessage,
                now.AddMinutes(-i),
                "UserName=admin; Result=InvalidCredentials; RememberMe=False",
                i < 3 ? "203.0.113.10" : $"203.0.113.{20 + i}");
        }

        var service = new AdminSecurityActivityService(store.DbContextFactory);

        var summary = await service.GetSuspiciousLoginSummaryAsync(now, CancellationToken.None);

        Assert.True(summary.IsSuspicious);
        Assert.Equal(10, summary.FailedLast15Minutes);
        Assert.Equal(10, summary.FailedLast24Hours);
        Assert.Equal("203.0.113.10", summary.MostActiveRemoteIpAddress);
        Assert.Equal(3, summary.MostActiveRemoteIpFailureCount);
        Assert.Contains(summary.Findings, x => x.Label == "Failed logins");
        Assert.Contains(summary.Findings, x => x.Label == "Daily failed logins");
        Assert.Contains(summary.Findings, x => x.Label == "Repeated remote IP");
    }

    [Fact]
    public void SecurityStampValidationDefaultsToEveryRequest()
    {
        var options = new SecurityStampValidatorOptions
        {
            ValidationInterval = TimeSpan.FromMinutes(30)
        };

        AdminSecurityDefaults.ConfigureSecurityStampValidator(options);

        Assert.Equal(TimeSpan.Zero, options.ValidationInterval);
    }

    [Fact]
    public void PasswordPolicySummaryReflectsIdentityOptions()
    {
        var options = new IdentityOptions();
        options.Password.RequiredLength = 14;
        options.Password.RequiredUniqueChars = 3;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = true;

        var summary = PasswordPolicySummary.FromOptions(options);

        Assert.Equal(14, summary.RequiredLength);
        Assert.Contains("At least 14 characters", summary.Requirements);
        Assert.Contains("At least 3 unique characters", summary.Requirements);
        Assert.Contains("At least one number", summary.Requirements);
        Assert.Contains("At least one lowercase letter", summary.Requirements);
        Assert.Contains("At least one symbol", summary.Requirements);
        Assert.DoesNotContain("At least one uppercase letter", summary.Requirements);
    }

    [Fact]
    public void SessionSummaryFormatsCookieAndStampSettings()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "admin")
        ], IdentityConstants.ApplicationScheme));
        var issued = new DateTimeOffset(2026, 7, 2, 9, 15, 0, TimeSpan.Zero);
        var expires = new DateTimeOffset(2026, 7, 2, 17, 15, 0, TimeSpan.Zero);
        var ticket = new AuthenticationTicket(
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = issued,
                ExpiresUtc = expires
            },
            IdentityConstants.ApplicationScheme);
        var cookieOptions = new CookieAuthenticationOptions
        {
            SlidingExpiration = true
        };
        cookieOptions.Cookie.HttpOnly = true;
        cookieOptions.Cookie.SameSite = SameSiteMode.Strict;
        cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        var stampOptions = new SecurityStampValidatorOptions
        {
            ValidationInterval = TimeSpan.Zero
        };

        var summary = AdminSessionSummary.Create(
            principal,
            AuthenticateResult.Success(ticket),
            cookieOptions,
            stampOptions);

        Assert.Equal("admin", summary.UserName);
        Assert.Equal("Persistent", summary.PersistenceLabel);
        Assert.Equal(issued, summary.IssuedUtc);
        Assert.Equal(expires, summary.ExpiresUtc);
        Assert.Equal("HTTPS only", summary.SecurePolicyLabel);
        Assert.Equal("Every request", summary.SecurityStampValidationLabel);
    }

    [Fact]
    public void WebSecuritySummaryLabelsHttpCertificateAndRestartRisks()
    {
        var now = DateTimeOffset.UtcNow;
        var listener = new AdminWebListenerConfiguration
        {
            HttpsPort = 5443,
            EnableHttp = true,
            HttpPort = 5080,
            UpdatedUtc = now.AddMinutes(-30)
        };
        var certificate = new AdminHttpsCertificateConfiguration
        {
            Mode = AdminHttpsCertificateMode.SelfSigned,
            DnsNames = ["relaywright.local"],
            NotAfterUtc = now.AddDays(10),
            UpdatedUtc = now.AddDays(-2)
        };
        var runtime = new RuntimeStatusSnapshot
        {
            RestartRequired = true,
            RestartReason = "Admin web HTTPS certificate changed.",
            RestartRequestedBy = "admin",
            RestartRequestedUtc = now.AddMinutes(-2)
        };

        var summary = AdminWebSecuritySummary.Create(listener, certificate, runtime, now);

        Assert.Equal("Relaywright-managed", summary.ListenerSourceLabel);
        Assert.Equal("Enabled on port 5080", summary.HttpStatusLabel);
        Assert.Equal("severity-warning", summary.HttpBadgeClass);
        Assert.Equal("SelfSigned", summary.CertificateModeLabel);
        Assert.Equal("relaywright.local", summary.CertificateDnsLabel);
        Assert.Equal("Expiring soon", summary.CertificateStatusLabel(now));
        Assert.Equal("severity-warning", summary.CertificateBadgeClass(now));
        Assert.Equal("Restart required", summary.RestartStatusLabel);
    }

    [Fact]
    public void SetupChecklistReflectsAdminPasswordHttpsAndListenerState()
    {
        var passwordPolicy = PasswordPolicySummary.FromOptions(new IdentityOptions());
        var certificate = new AdminHttpsCertificateConfiguration
        {
            Mode = AdminHttpsCertificateMode.Pfx
        };
        var listener = new AdminWebListenerConfiguration
        {
            EnableHttp = true,
            HttpPort = 5080
        };
        var options = new BootstrapAdminOptions
        {
            Password = BootstrapAdminOptions.DefaultDevelopmentPassword
        };

        var checklist = SetupHardeningChecklist.Create(
            adminExists: false,
            passwordPolicy,
            certificate,
            listener,
            options,
            new TestHostEnvironment(Environments.Development));

        Assert.Contains(checklist.Items, x => x.Label == "Admin account" && x.Status == "Required");
        Assert.Contains(checklist.Items, x => x.Label == "Default password guard" && x.Status == "Development only");
        Assert.Contains(checklist.Items, x => x.Label == "HTTPS certificate" && x.Status == "Configured");
        Assert.Contains(checklist.Items, x => x.Label == "HTTP listener" && x.Status == "HTTP enabled");
    }

    private static async Task AddSecurityEventAsync(
        SqliteTestStore store,
        string message,
        DateTimeOffset occurredUtc,
        string? detail,
        string? remoteIpAddress)
    {
        await using var dbContext = store.CreateDbContext();
        dbContext.OperationalEvents.Add(new OperationalEvent
        {
            Category = OperationalEventCategory.Security,
            Severity = message == AdminSecurityActivityService.SignInFailedMessage
                ? EventSeverity.Warning
                : EventSeverity.Information,
            Message = message,
            Detail = detail,
            RemoteIpAddress = remoteIpAddress,
            OccurredUtc = occurredUtc
        });
        await dbContext.SaveChangesAsync();
    }

    private sealed class IdentityPageFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private IdentityPageFixture(
            SqliteConnection connection,
            ServiceProvider serviceProvider,
            RecordingOperationalEventService events,
            IHttpContextAccessor httpContextAccessor)
        {
            _connection = connection;
            _serviceProvider = serviceProvider;
            Events = events;
            _httpContextAccessor = httpContextAccessor;
            SecurityActivity = new TestAdminSecurityActivityService();
        }

        public IServiceProvider Services => _serviceProvider;

        public RecordingOperationalEventService Events { get; }

        public TestAdminSecurityActivityService SecurityActivity { get; }

        public static async Task<IdentityPageFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var events = new RecordingOperationalEventService();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.AddHttpContextAccessor();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            services
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
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();
            services.ConfigureApplicationCookie(options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.SlidingExpiration = true;
            });
            services.Configure<SecurityStampValidatorOptions>(AdminSecurityDefaults.ConfigureSecurityStampValidator);

            var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
            }

            return new IdentityPageFixture(
                connection,
                provider,
                events,
                provider.GetRequiredService<IHttpContextAccessor>());
        }

        public async Task<ApplicationUser> CreateUserAsync(string userName, string password)
        {
            var userManager = _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = userName,
                DisplayName = userName,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, password);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));
            return user;
        }

        public LoginModel CreateLoginModel()
        {
            var httpContext = CreateHttpContext();
            var model = new LoginModel(
                _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                _serviceProvider.GetRequiredService<SignInManager<ApplicationUser>>(),
                Events,
                NullLogger<LoginModel>.Instance);

            AttachPageContext(model, httpContext);
            return model;
        }

        public ChangePasswordModel CreateChangePasswordModel(ApplicationUser user)
        {
            var httpContext = CreateHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName!)
            ], IdentityConstants.ApplicationScheme));

            var model = new ChangePasswordModel(
                _serviceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                _serviceProvider.GetRequiredService<SignInManager<ApplicationUser>>(),
                Events,
                SecurityActivity,
                _serviceProvider.GetRequiredService<IOptions<IdentityOptions>>(),
                _serviceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>(),
                _serviceProvider.GetRequiredService<IOptions<SecurityStampValidatorOptions>>(),
                NullLogger<ChangePasswordModel>.Instance);

            AttachPageContext(model, httpContext);
            return model;
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }

        private DefaultHttpContext CreateHttpContext()
        {
            var httpContext = new DefaultHttpContext
            {
                RequestServices = _serviceProvider
            };
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
            _httpContextAccessor.HttpContext = httpContext;
            return httpContext;
        }

        private static void AttachPageContext(PageModel model, HttpContext httpContext)
        {
            model.PageContext = new PageContext
            {
                HttpContext = httpContext
            };
        }
    }

    private sealed class TestAdminSecurityActivityService : IAdminSecurityActivityService
    {
        public AdminLoginActivitySummary LoginActivity { get; set; } =
            new(null, null, null, 0, 0);

        public SuspiciousLoginSummary SuspiciousLogins { get; set; } = SuspiciousLoginSummary.Empty;

        public Task<AdminLoginActivitySummary> GetLoginActivityAsync(
            string? userName,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(LoginActivity);
        }

        public Task<SuspiciousLoginSummary> GetSuspiciousLoginSummaryAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(SuspiciousLogins);
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Relaywright.Web.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
