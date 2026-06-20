using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Events;

public sealed class OperationalEventService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<OperationalEventService> logger) : IOperationalEventService
{
    private const int MaxRemoteIpAddressLength = 128;
    private const int MaxMessageLength = 2048;
    private const int MaxDetailLength = 8192;

    public async Task WriteAsync(OperationalEventRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.Log(
            request.Severity switch
            {
                EventSeverity.Warning => LogLevel.Warning,
                EventSeverity.Error => LogLevel.Error,
                _ => LogLevel.Information
            },
            "{Category}: {Message}",
            request.Category,
            request.Message);

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            dbContext.OperationalEvents.Add(new OperationalEvent
            {
                OccurredUtc = DateTimeOffset.UtcNow,
                Severity = request.Severity,
                Category = request.Category,
                SessionId = request.SessionId,
                QueuedMessageId = request.QueuedMessageId,
                RemoteIpAddress = Truncate(request.RemoteIpAddress, MaxRemoteIpAddressLength),
                Message = Truncate(request.Message, MaxMessageLength) ?? string.Empty,
                Detail = Truncate(request.Detail, MaxDetailLength)
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to persist operational event {Category}: {Message}", request.Category, request.Message);
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
