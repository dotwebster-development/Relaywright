using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;
using Relaywright.Web.Options;

namespace Relaywright.Web.Services.Relay;

public sealed class DataSeeder(
    IServiceProvider serviceProvider,
    IOptions<BootstrapAdminOptions> bootstrapAdminOptions,
    IHostEnvironment environment,
    DatabaseSchemaInitializer schemaInitializer,
    ILogger<DataSeeder> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing data store and bootstrap data.");

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await schemaInitializer.InitializeAsync(dbContext, cancellationToken);

        if (!await dbContext.RelayConfigurations.AnyAsync(cancellationToken))
        {
            dbContext.RelayConfigurations.Add(new RelayConfiguration());
            logger.LogInformation("Seeded default relay configuration.");
        }

        if (!await dbContext.TrustedNetworks.AnyAsync(cancellationToken))
        {
            dbContext.TrustedNetworks.AddRange(
                new TrustedNetwork
                {
                    Cidr = "127.0.0.1/32",
                    Description = "Localhost IPv4"
                },
                new TrustedNetwork
                {
                    Cidr = "::1/128",
                    Description = "Localhost IPv6"
                });
            logger.LogInformation("Seeded default localhost trusted networks.");
        }

        if (!await dbContext.RuntimeControlStates.AnyAsync(cancellationToken))
        {
            dbContext.RuntimeControlStates.Add(new RuntimeControlState());
            logger.LogInformation("Seeded runtime control state.");
        }

        if (!await dbContext.SubmissionPolicies.AnyAsync(cancellationToken))
        {
            dbContext.SubmissionPolicies.Add(new SubmissionPolicy());
            logger.LogInformation("Seeded default submission policy.");
        }

        if (!await dbContext.BackupScheduleStates.AnyAsync(cancellationToken))
        {
            dbContext.BackupScheduleStates.Add(new BackupScheduleState());
            logger.LogInformation("Seeded backup schedule state.");
        }

        await SeedAlertRulesAsync(dbContext, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await RejectDefaultPasswordsOutsideDevelopmentAsync(userManager, cancellationToken);

        if (string.IsNullOrWhiteSpace(bootstrapAdminOptions.Value.Password))
        {
            if (!await userManager.Users.AnyAsync(cancellationToken))
            {
                logger.LogWarning("No bootstrap admin password is configured. First-run admin setup is required through the web UI.");
            }
            else
            {
                logger.LogInformation("Bootstrap admin password is not configured. Automatic admin seeding skipped.");
            }

            logger.LogInformation("Data store initialization completed.");
            return;
        }

        ValidateBootstrapUserName();

        var existingAdmin = await userManager.FindByNameAsync(bootstrapAdminOptions.Value.UserName);
        if (existingAdmin is null)
        {
            ValidateBootstrapCreateOptions();

            var admin = new ApplicationUser
            {
                UserName = bootstrapAdminOptions.Value.UserName,
                Email = bootstrapAdminOptions.Value.Email,
                DisplayName = "Administrator",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(admin, bootstrapAdminOptions.Value.Password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create bootstrap admin user: {string.Join("; ", result.Errors.Select(x => x.Description))}");
            }

            logger.LogWarning("Created bootstrap admin user '{UserName}'. Change the password immediately.", admin.UserName);
        }
        else
        {
            logger.LogInformation("Bootstrap admin user already exists. UserName={UserName}", bootstrapAdminOptions.Value.UserName);
        }

        logger.LogInformation("Data store initialization completed.");
    }

    private void ValidateBootstrapUserName()
    {
        if (string.IsNullOrWhiteSpace(bootstrapAdminOptions.Value.UserName))
        {
            throw new InvalidOperationException("Bootstrap admin user name is required.");
        }
    }

    private void ValidateBootstrapCreateOptions()
    {
        if (string.IsNullOrWhiteSpace(bootstrapAdminOptions.Value.Email))
        {
            throw new InvalidOperationException("Bootstrap admin email is required.");
        }

        if (!environment.IsDevelopment()
            && string.Equals(
                bootstrapAdminOptions.Value.Password,
                BootstrapAdminOptions.DefaultDevelopmentPassword,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Configure a non-default BootstrapAdmin password before starting outside Development.");
        }
    }

    private async Task RejectDefaultPasswordsOutsideDevelopmentAsync(
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var users = await userManager.Users.ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                continue;
            }

            var result = userManager.PasswordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                BootstrapAdminOptions.DefaultDevelopmentPassword);

            if (result != PasswordVerificationResult.Failed)
            {
                throw new InvalidOperationException(
                    "An admin account still uses the default development password. Change it before starting outside Development.");
            }
        }
    }

    private async Task SeedAlertRulesAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var existingKeys = await dbContext.AlertRules
            .Select(x => x.Key)
            .ToListAsync(cancellationToken);
        var existing = existingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in GetDefaultAlertRules())
        {
            if (existing.Contains(rule.Key))
            {
                continue;
            }

            dbContext.AlertRules.Add(rule);
            logger.LogInformation("Seeded alert rule. Key={AlertRuleKey}; DisplayName={DisplayName}", rule.Key, rule.DisplayName);
        }
    }

    private static IReadOnlyList<AlertRule> GetDefaultAlertRules()
    {
        return
        [
            new AlertRule
            {
                Key = "queue-depth",
                DisplayName = "Queue depth",
                Description = "Active queue depth is above the configured threshold.",
                Threshold = 100,
                CooldownMinutes = 60
            },
            new AlertRule
            {
                Key = "oldest-active-message-minutes",
                DisplayName = "Oldest active message age",
                Description = "The oldest pending or retrying message is older than the configured minutes.",
                Threshold = 60,
                CooldownMinutes = 60
            },
            new AlertRule
            {
                Key = "failed-message-count",
                DisplayName = "Failed message count",
                Description = "Failed or expired messages are above the configured threshold.",
                Threshold = 10,
                CooldownMinutes = 60
            },
            new AlertRule
            {
                Key = "listener-down",
                DisplayName = "SMTP listener down",
                Description = "The SMTP listener is not reporting a running state.",
                Threshold = 1,
                CooldownMinutes = 15
            },
            new AlertRule
            {
                Key = "disk-free-mb",
                DisplayName = "Disk space low",
                Description = "Free space on the data volume is below the configured megabytes.",
                Threshold = 1024,
                CooldownMinutes = 60
            },
            new AlertRule
            {
                Key = "admin-certificate-expiry-days",
                DisplayName = "Admin certificate expiry",
                Description = "The configured admin HTTPS certificate expires within the configured days.",
                Threshold = 30,
                CooldownMinutes = 1440
            },
            new AlertRule
            {
                Key = "recent-upstream-failures",
                DisplayName = "Recent upstream failures",
                Description = "Recent delivery errors are above the configured threshold.",
                Threshold = 5,
                CooldownMinutes = 60
            }
        ];
    }
}
