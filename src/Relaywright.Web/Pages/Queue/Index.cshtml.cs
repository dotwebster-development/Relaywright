using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Pages.Queue;

public sealed class IndexModel(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<IndexModel> logger) : PageModel
{
    private const int PageSize = 50;

    public string SelectedStatus { get; private set; } = "active";

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public int TotalCount { get; private set; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public IReadOnlyList<QueuedMessage> Messages { get; private set; } = Array.Empty<QueuedMessage>();

    public async Task OnGetAsync(string? status, CancellationToken cancellationToken)
    {
        SelectedStatus = string.IsNullOrWhiteSpace(status) ? "active" : status.ToLowerInvariant();
        PageNumber = Math.Max(1, PageNumber);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.QueuedMessages
            .AsNoTracking()
            .AsQueryable();

        query = SelectedStatus switch
        {
            "failed" => query.Where(x => x.Status == QueuedMessageStatus.Failed || x.Status == QueuedMessageStatus.Expired),
            "delivered" => query.Where(x => x.Status == QueuedMessageStatus.Delivered),
            "all" => query,
            _ => query.Where(x => x.Status == QueuedMessageStatus.Pending || x.Status == QueuedMessageStatus.RetryScheduled || x.Status == QueuedMessageStatus.InProgress)
        };

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var search = Search.Trim();
            query = query.Where(x =>
                x.CorrelationId.Contains(search)
                || x.EnvelopeFrom.Contains(search)
                || (x.RemoteIpAddress != null && x.RemoteIpAddress.Contains(search))
                || x.Recipients.Any(recipient => recipient.RecipientAddress.Contains(search)));
        }

        TotalCount = await query.CountAsync(cancellationToken);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        var messages = await query
            .Include(x => x.Recipients)
            .ToListAsync(cancellationToken);

        Messages = messages
            .OrderByDescending(x => x.AcceptedUtc)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        logger.LogDebug(
            "Queue page loaded. Status={Status}; SearchPresent={SearchPresent}; PageNumber={PageNumber}; TotalCount={TotalCount}; ReturnedCount={ReturnedCount}; User={UserName}",
            SelectedStatus,
            !string.IsNullOrWhiteSpace(Search),
            PageNumber,
            TotalCount,
            Messages.Count,
            User.Identity?.Name);
    }

    public bool IsStatusActive(string status) =>
        string.Equals(SelectedStatus, status, StringComparison.OrdinalIgnoreCase);
}
