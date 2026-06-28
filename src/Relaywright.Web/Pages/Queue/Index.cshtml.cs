using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Queueing;

namespace Relaywright.Web.Pages.Queue;

public sealed class IndexModel(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IMessageQueueService messageQueueService,
    ILogger<IndexModel> logger) : PageModel
{
    private const int PageSize = 50;

    public string SelectedStatus { get; private set; } = "active";

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty]
    public List<Guid> SelectedMessageIds { get; set; } = [];

    [BindProperty]
    public string? ReturnStatus { get; set; }

    [BindProperty]
    public string? ReturnSearch { get; set; }

    [BindProperty]
    public int ReturnPageNumber { get; set; } = 1;

    [TempData]
    public string? StatusMessage { get; set; }

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

        var (sql, parameters) = BuildPagedMessagesSql(
            SelectedStatus,
            Search?.Trim(),
            (PageNumber - 1) * PageSize,
            PageSize);

        var orderedPage = await dbContext.QueuedMessages
            .FromSqlRaw(sql, parameters)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var orderedIds = orderedPage.Select(x => x.Id).ToArray();
        var messageOrder = orderedIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index);

        var messages = orderedIds.Length == 0
            ? []
            : await dbContext.QueuedMessages
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Recipients)
                .Where(x => orderedIds.Contains(x.Id))
                .ToListAsync(cancellationToken);

        Messages = messages
            .OrderBy(x => messageOrder[x.Id])
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

    public async Task<IActionResult> OnPostBulkRetryAsync(CancellationToken cancellationToken)
    {
        var result = await messageQueueService.RetryNowAsync(SelectedMessageIds, cancellationToken);
        StatusMessage = result.Message;
        logger.LogInformation(
            "Bulk queue retry requested. Requested={Requested}; Succeeded={Succeeded}; Rejected={Rejected}; Missing={Missing}; User={UserName}",
            result.Requested,
            result.Succeeded,
            result.Rejected,
            result.Missing,
            User.Identity?.Name);
        return RedirectToQueue();
    }

    public async Task<IActionResult> OnPostBulkPurgeAsync(CancellationToken cancellationToken)
    {
        var result = await messageQueueService.PurgeAsync(SelectedMessageIds, cancellationToken);
        StatusMessage = result.Message;
        logger.LogInformation(
            "Bulk queue purge requested. Requested={Requested}; Succeeded={Succeeded}; Rejected={Rejected}; Missing={Missing}; SpoolDeleteFailures={SpoolDeleteFailures}; User={UserName}",
            result.Requested,
            result.Succeeded,
            result.Rejected,
            result.Missing,
            result.SpoolDeleteFailures,
            User.Identity?.Name);
        return RedirectToQueue();
    }

    private IActionResult RedirectToQueue()
    {
        return RedirectToPage(new
        {
            status = string.IsNullOrWhiteSpace(ReturnStatus) ? "active" : ReturnStatus,
            search = ReturnSearch,
            pageNumber = Math.Max(1, ReturnPageNumber)
        });
    }

    private static (string Sql, object[] Parameters) BuildPagedMessagesSql(
        string selectedStatus,
        string? search,
        int offset,
        int pageSize)
    {
        var filters = new List<string>();
        var parameters = new List<object>
        {
            IntegerParameter("$limit", pageSize),
            IntegerParameter("$offset", offset)
        };

        switch (selectedStatus)
        {
            case "failed":
                filters.Add(@"qm.""Status"" IN ($failedStatus, $expiredStatus)");
                parameters.Add(IntegerParameter("$failedStatus", (int)QueuedMessageStatus.Failed));
                parameters.Add(IntegerParameter("$expiredStatus", (int)QueuedMessageStatus.Expired));
                break;

            case "delivered":
                filters.Add(@"qm.""Status"" = $deliveredStatus");
                parameters.Add(IntegerParameter("$deliveredStatus", (int)QueuedMessageStatus.Delivered));
                break;

            case "all":
                break;

            default:
                filters.Add(@"qm.""Status"" IN ($pendingStatus, $retryStatus, $inProgressStatus)");
                parameters.Add(IntegerParameter("$pendingStatus", (int)QueuedMessageStatus.Pending));
                parameters.Add(IntegerParameter("$retryStatus", (int)QueuedMessageStatus.RetryScheduled));
                parameters.Add(IntegerParameter("$inProgressStatus", (int)QueuedMessageStatus.InProgress));
                break;
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filters.Add("""
                (instr(qm."CorrelationId", $search) > 0
                    OR instr(qm."EnvelopeFrom", $search) > 0
                    OR (qm."RemoteIpAddress" IS NOT NULL AND instr(qm."RemoteIpAddress", $search) > 0)
                    OR EXISTS (
                        SELECT 1
                        FROM "QueuedMessageRecipients" AS qmr
                        WHERE qmr."QueuedMessageId" = qm."Id"
                            AND instr(qmr."RecipientAddress", $search) > 0
                    ))
                """);
            parameters.Add(new SqliteParameter("$search", search));
        }

        var whereSql = filters.Count == 0
            ? string.Empty
            : $"{Environment.NewLine}WHERE {string.Join($"{Environment.NewLine}    AND ", filters)}";

        var sql = $"""
            SELECT qm.*
            FROM "QueuedMessages" AS qm{whereSql}
            ORDER BY qm."AcceptedUtc" DESC
            LIMIT $limit OFFSET $offset
            """;

        return (sql, parameters.ToArray());
    }

    private static SqliteParameter IntegerParameter(string name, int value)
    {
        return new SqliteParameter(name, SqliteType.Integer)
        {
            Value = value
        };
    }
}
