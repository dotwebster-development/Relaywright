using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;

namespace Relaywright.Web.Services.Runtime;

public sealed class RuntimeStatusService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOperationalEventService eventService,
    IQueueSignal queueSignal,
    ILogger<RuntimeStatusService> logger) : IRuntimeStatusService
{
    private readonly object _gate = new();
    private RuntimeComponentState _smtpListener = new();
    private RuntimeComponentState _deliveryWorker = new();
    private RuntimeComponentState _maintenanceWorker = new();
    private int _activeDeliveries;
    private int? _lastCleanupRemovedRecords;
    private DateTimeOffset? _lastCleanupUtc;

    public async Task<RuntimeStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var state = await GetOrCreateStateAsync(cancellationToken);

        lock (_gate)
        {
            return new RuntimeStatusSnapshot
            {
                IsDeliveryPaused = state.IsDeliveryPaused,
                DeliveryPauseReason = state.DeliveryPauseReason,
                DeliveryPausedBy = state.DeliveryPausedBy,
                DeliveryPausedUtc = state.DeliveryPausedUtc,
                RestartRequired = state.RestartRequired,
                RestartReason = state.RestartReason,
                RestartRequestedBy = state.RestartRequestedBy,
                RestartRequestedUtc = state.RestartRequestedUtc,
                RestartSupported = state.RestartSupported,
                SmtpListener = _smtpListener,
                DeliveryWorker = _deliveryWorker,
                MaintenanceWorker = _maintenanceWorker,
                ActiveDeliveries = _activeDeliveries,
                LastCleanupRemovedRecords = _lastCleanupRemovedRecords,
                LastCleanupUtc = _lastCleanupUtc
            };
        }
    }

    public async Task<bool> IsDeliveryPausedAsync(CancellationToken cancellationToken)
    {
        var state = await GetOrCreateStateAsync(cancellationToken);
        return state.IsDeliveryPaused;
    }

    public async Task PauseDeliveryAsync(string? reason, string? userName, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var state = await GetOrCreateStateAsync(dbContext, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        state.IsDeliveryPaused = true;
        state.DeliveryPauseReason = Trim(reason, 512);
        state.DeliveryPausedBy = Trim(userName, 256);
        state.DeliveryPausedUtc = now;
        state.UpdatedUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        ReportDeliveryWorkerState("Paused", 0, "Outbound delivery is paused by an administrator.");

        logger.LogWarning(
            "Outbound delivery paused. User={UserName}; ReasonPresent={ReasonPresent}",
            userName,
            !string.IsNullOrWhiteSpace(reason));

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.System,
            Message = "Outbound delivery paused.",
            Detail = string.IsNullOrWhiteSpace(reason) ? null : reason
        }, cancellationToken);
    }

    public async Task ResumeDeliveryAsync(string? userName, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var state = await GetOrCreateStateAsync(dbContext, cancellationToken);

        state.IsDeliveryPaused = false;
        state.DeliveryPauseReason = null;
        state.DeliveryPausedBy = null;
        state.DeliveryPausedUtc = null;
        state.UpdatedUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        queueSignal.Pulse();
        ReportDeliveryWorkerState("Running", 0, "Outbound delivery is enabled.");

        logger.LogInformation("Outbound delivery resumed. User={UserName}", userName);

        await eventService.WriteAsync(new OperationalEventRequest
        {
            Category = OperationalEventCategory.System,
            Message = "Outbound delivery resumed."
        }, cancellationToken);
    }

    public void ReportSmtpListenerState(string status, string? detail = null, Exception? exception = null)
    {
        lock (_gate)
        {
            _smtpListener = CreateComponentState(_smtpListener, status, detail, exception);
        }
    }

    public void ReportDeliveryWorkerState(string status, int activeDeliveries, string? detail = null, Exception? exception = null)
    {
        lock (_gate)
        {
            _activeDeliveries = Math.Max(0, activeDeliveries);
            _deliveryWorker = CreateComponentState(_deliveryWorker, status, detail, exception);
        }
    }

    public void ReportMaintenanceWorkerState(string status, int? removedRecords = null, string? detail = null, Exception? exception = null)
    {
        lock (_gate)
        {
            if (removedRecords is not null)
            {
                _lastCleanupRemovedRecords = removedRecords;
                _lastCleanupUtc = DateTimeOffset.UtcNow;
            }

            _maintenanceWorker = CreateComponentState(_maintenanceWorker, status, detail, exception);
        }
    }

    private async Task<RuntimeControlState> GetOrCreateStateAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await GetOrCreateStateAsync(dbContext, cancellationToken);
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

    private static RuntimeComponentState CreateComponentState(
        RuntimeComponentState previous,
        string status,
        string? detail,
        Exception? exception)
    {
        var now = DateTimeOffset.UtcNow;
        return new RuntimeComponentState
        {
            Status = string.IsNullOrWhiteSpace(status) ? "Unknown" : status,
            Detail = Trim(detail, 512),
            LastError = exception is null ? previous.LastError : Trim(exception.Message, 2048),
            LastChangedUtc = string.Equals(previous.Status, status, StringComparison.OrdinalIgnoreCase)
                ? previous.LastChangedUtc
                : now,
            LastHeartbeatUtc = now
        };
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
