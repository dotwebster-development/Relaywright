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
        }
    }
}
