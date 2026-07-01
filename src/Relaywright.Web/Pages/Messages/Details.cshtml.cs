using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Queueing;

namespace Relaywright.Web.Pages.Messages;

public sealed class DetailsModel(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IMessageQueueService messageQueueService,
    IMessageMetadataService messageMetadataService,
    ILogger<DetailsModel> logger) : PageModel
{
    public QueuedMessage? Message { get; private set; }

    public MessageMetadataSummary? Metadata { get; private set; }

    public IReadOnlyList<MessageTimelineItem> Timeline { get; private set; } = Array.Empty<MessageTimelineItem>();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        await LoadAsync(id, cancellationToken);
        if (Message is null)
        {
            logger.LogWarning(
                "Message details requested for missing message. MessageId={MessageId}; User={UserName}",
                id,
                User.Identity?.Name);

            return NotFound();
        }

        logger.LogDebug(
            "Message details page loaded. MessageId={MessageId}; Status={Status}; AttemptCount={AttemptCount}; RecipientCount={RecipientCount}; MetadataLoaded={MetadataLoaded}; User={UserName}",
            Message.Id,
            Message.Status,
            Message.DeliveryAttempts.Count,
            Message.Recipients.Count,
            Metadata is not null,
            User.Identity?.Name);

        return Page();
    }

    public async Task<IActionResult> OnPostRetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await messageQueueService.RetryNowAsync(id, cancellationToken);
        StatusMessage = result.Message;
        logger.LogInformation(
            "Message retry requested from details page. MessageId={MessageId}; Succeeded={Succeeded}; ResultMessage={ResultMessage}; User={UserName}",
            id,
            result.Succeeded,
            result.Message,
            User.Identity?.Name);

        if (result.Succeeded)
        {
            return RedirectToPage(new { id });
        }

        await LoadAsync(id, cancellationToken);
        return Message is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostPurgeAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await messageQueueService.PurgeAsync(id, cancellationToken);
        StatusMessage = result.Message;
        logger.LogInformation(
            "Message purge requested from details page. MessageId={MessageId}; Succeeded={Succeeded}; ResultMessage={ResultMessage}; User={UserName}",
            id,
            result.Succeeded,
            result.Message,
            User.Identity?.Name);

        if (result.Succeeded)
        {
            return RedirectToPage("/Queue/Index");
        }

        await LoadAsync(id, cancellationToken);
        return Message is null ? NotFound() : Page();
    }

    private async Task LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        Message = await dbContext.QueuedMessages
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Recipients)
            .Include(x => x.DeliveryAttempts)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (Message is not null)
        {
            Metadata = await messageMetadataService.ReadAsync(Message.SpoolFileRelativePath, cancellationToken);
            Timeline = BuildTimeline(Message);
        }
    }

    private static IReadOnlyList<MessageTimelineItem> BuildTimeline(QueuedMessage message)
    {
        var items = new List<MessageTimelineItem>
        {
            new(message.AcceptedUtc, "Accepted", "SMTP DATA accepted from trusted device.", "status-enabled"),
            new(message.CreatedUtc, "Queued", "Spool file and queue metadata were recorded.", "status-enabled")
        };

        foreach (var attempt in message.DeliveryAttempts)
        {
            items.Add(new MessageTimelineItem(
                attempt.StartedUtc,
                $"Attempt {attempt.AttemptNumber} started",
                "Delivery worker claimed this message for upstream delivery.",
                "status-inprogress"));

            if (attempt.CompletedUtc is not null)
            {
                var detail = attempt.Succeeded
                    ? FirstNonEmpty(attempt.ResponseText, "Upstream relay accepted the message.")
                    : FirstNonEmpty(attempt.ResponseText, attempt.ExceptionMessage, attempt.FailureCategory.ToString());
                items.Add(new MessageTimelineItem(
                    attempt.CompletedUtc.Value,
                    attempt.Succeeded ? $"Attempt {attempt.AttemptNumber} delivered" : $"Attempt {attempt.AttemptNumber} failed",
                    detail,
                    attempt.Succeeded ? "status-delivered" : "status-failed"));
            }
        }

        if (message.Status == QueuedMessageStatus.RetryScheduled)
        {
            items.Add(new MessageTimelineItem(
                message.NextAttemptAtUtc,
                "Retry scheduled",
                "Message is waiting for its next upstream attempt.",
                "status-retryscheduled"));
        }

        if (message.DeliveredUtc is not null)
        {
            items.Add(new MessageTimelineItem(
                message.DeliveredUtc.Value,
                "Delivered",
                "Message reached a terminal delivered state.",
                "status-delivered"));
        }

        if (message.Status == QueuedMessageStatus.Failed)
        {
            items.Add(new MessageTimelineItem(
                message.LastAttemptCompletedUtc ?? message.CreatedUtc,
                "Failed",
                FirstNonEmpty(message.LastError, message.LastResponseText, "Message reached a terminal failed state."),
                "status-failed"));
        }

        if (message.Status == QueuedMessageStatus.Expired)
        {
            items.Add(new MessageTimelineItem(
                message.ExpiresUtc,
                "Expired",
                "Message expired before successful delivery.",
                "status-expired"));
        }

        return items
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.Label)
            .ToList();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
    }
}

public sealed record MessageTimelineItem(
    DateTimeOffset Timestamp,
    string Label,
    string Detail,
    string BadgeClass);
