using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;

namespace Relaywright.Web.Services.Runtime;

public sealed class ApplicationRestartService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IHostApplicationLifetime applicationLifetime,
    IHostEnvironment environment,
    IOperationalEventService eventService,
    ILogger<ApplicationRestartService> logger) : IApplicationRestartService
{
    private readonly DateTimeOffset _processStartedUtc = DateTimeOffset.UtcNow;
    private int _restartScheduled;

    public async Task<ApplicationRestartRequestResult> RequestRestartAsync(
        string reason,
        string? userName,
        CancellationToken cancellationToken)
    {
        var supported = IsRestartSupported();
        var now = DateTimeOffset.UtcNow;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var state = await GetOrCreateStateAsync(dbContext, cancellationToken);
        state.RestartRequired = true;
        state.RestartReason = Trim(reason, 512);
        state.RestartRequestedBy = Trim(userName, 256);
        state.RestartRequestedUtc = now;
        state.RestartSupported = supported;
        state.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.System,
            Message = supported
                ? "Application restart requested."
                : "Application restart required.",
            Detail = reason
        }, cancellationToken);

        if (supported && Interlocked.Exchange(ref _restartScheduled, 1) == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    logger.LogWarning(
                        "Stopping application to apply restart-required settings. Reason={Reason}; User={UserName}",
                        reason,
                        userName);
                    applicationLifetime.StopApplication();
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Application restart scheduling failed.");
                }
            });
        }

        return new ApplicationRestartRequestResult
        {
            RestartScheduled = supported,
            RestartSupported = supported,
            Message = supported
                ? "Restart scheduled. The service manager should bring Relaywright back online shortly."
                : "Restart required. Restart Relaywright from the host service manager to apply this change."
        };
    }

    public async Task ClearAppliedRestartIfNeededAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var state = await GetOrCreateStateAsync(dbContext, cancellationToken);
        if (!state.RestartRequired || state.RestartRequestedUtc is null || state.RestartRequestedUtc >= _processStartedUtc)
        {
            return;
        }

        var reason = state.RestartReason;
        state.RestartRequired = false;
        state.RestartReason = null;
        state.RestartRequestedBy = null;
        state.RestartRequestedUtc = null;
        state.RestartSupported = false;
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Cleared restart-required state after process start. PreviousReason={Reason}", reason);
        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.System,
            Message = "Application restart completed.",
            Detail = reason
        }, cancellationToken);
    }

    private bool IsRestartSupported()
    {
        if (environment.IsDevelopment())
        {
            return false;
        }

        return (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
            || SystemdHelpers.IsSystemdService();
    }

    private static async Task<RuntimeControlState> GetOrCreateStateAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.RuntimeControlStates.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new RuntimeControlState();
        dbContext.RuntimeControlStates.Add(state);
        await dbContext.SaveChangesAsync(cancellationToken);
        return state;
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
