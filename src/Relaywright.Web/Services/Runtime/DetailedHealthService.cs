using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Services.Runtime;

public sealed class DetailedHealthService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    AppPaths paths,
    IRelayConfigurationService relayConfigurationService,
    ILogger<DetailedHealthService> logger)
{
    public async Task<DetailedHealthSnapshot> CheckAsync(CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var healthy = true;

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var connected = await dbContext.Database.CanConnectAsync(cancellationToken);
            healthy &= connected;
            checks["database"] = connected ? "ok" : "unavailable";
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
            var valid = configuration.ListenerPort is >= ValidationLimits.MinimumPort and <= ValidationLimits.MaximumPort;
            checks["configuration"] = valid ? "ok" : "invalid listener port";
            healthy &= valid;
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

        return new DetailedHealthSnapshot(healthy, checks);
    }
}

public sealed record DetailedHealthSnapshot(bool Healthy, IReadOnlyDictionary<string, string> Checks);
