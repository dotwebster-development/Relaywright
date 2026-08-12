using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Options;

namespace Relaywright.Web.Services.Queueing;

public sealed record QueueQueryRequest(string Status, string? Search, int PageNumber, int PageSize = 50);

public sealed record QueueQueryResult(
    string Status,
    string? Search,
    int PageNumber,
    int TotalCount,
    IReadOnlyList<QueuedMessage> Messages)
{
    public int TotalPages(int pageSize) => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)pageSize));
}

public sealed class QueueQueryService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    DatabaseConfiguration databaseConfiguration)
{
    public async Task<QueueQueryResult> QueryAsync(
        QueueQueryRequest request,
        CancellationToken cancellationToken)
    {
        var status = NormalizeStatus(request.Status);
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Max(1, request.PageSize);
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = ApplyFilters(dbContext.QueuedMessages.AsNoTracking(), status, search);
        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        pageNumber = Math.Min(pageNumber, totalPages);
        var offset = (pageNumber - 1) * pageSize;

        IReadOnlyList<QueuedMessage> messages;
        if (databaseConfiguration.IsSqlite)
        {
            var orderedIds = await GetSqliteOrderedIdsAsync(
                dbContext,
                status,
                search,
                offset,
                pageSize,
                cancellationToken);
            var order = orderedIds.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index);
            messages = orderedIds.Length == 0
                ? []
                : await dbContext.QueuedMessages
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(x => x.Recipients)
                    .Where(x => orderedIds.Contains(x.Id))
                    .ToListAsync(cancellationToken);
            messages = messages.OrderBy(x => order[x.Id]).ToList();
        }
        else
        {
            messages = await query
                .AsSplitQuery()
                .Include(x => x.Recipients)
                .OrderByDescending(x => x.AcceptedUtc)
                .Skip(offset)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }

        return new QueueQueryResult(status, search, pageNumber, totalCount, messages);
    }

    public static string NormalizeStatus(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? "active"
            : status.Trim().ToLowerInvariant() switch
            {
                "active" => "active",
                "failed" => "failed",
                "delivered" => "delivered",
                "all" => "all",
                _ => "active"
            };

    private static IQueryable<QueuedMessage> ApplyFilters(
        IQueryable<QueuedMessage> query,
        string status,
        string? search)
    {
        query = status switch
        {
            "failed" => query.Where(x => x.Status == QueuedMessageStatus.Failed || x.Status == QueuedMessageStatus.Expired),
            "delivered" => query.Where(x => x.Status == QueuedMessageStatus.Delivered),
            "all" => query,
            _ => query.Where(x =>
                x.Status == QueuedMessageStatus.Pending
                || x.Status == QueuedMessageStatus.RetryScheduled
                || x.Status == QueuedMessageStatus.InProgress)
        };

        if (search is not null)
        {
            query = query.Where(x =>
                x.CorrelationId.Contains(search)
                || x.EnvelopeFrom.Contains(search)
                || (x.RemoteIpAddress != null && x.RemoteIpAddress.Contains(search))
                || x.Recipients.Any(recipient => recipient.RecipientAddress.Contains(search)));
        }

        return query;
    }

    private static async Task<Guid[]> GetSqliteOrderedIdsAsync(
        ApplicationDbContext dbContext,
        string status,
        string? search,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var filters = new List<string>();
        var parameters = new List<object>
        {
            IntegerParameter("$limit", pageSize),
            IntegerParameter("$offset", offset)
        };
        switch (status)
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

        if (search is not null)
        {
            filters.Add("""
                (instr(qm."CorrelationId", $search) > 0
                    OR instr(qm."EnvelopeFrom", $search) > 0
                    OR (qm."RemoteIpAddress" IS NOT NULL AND instr(qm."RemoteIpAddress", $search) > 0)
                    OR EXISTS (
                        SELECT 1 FROM "QueuedMessageRecipients" AS qmr
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
        var page = await dbContext.QueuedMessages
            .FromSqlRaw(sql, parameters.ToArray())
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return page.Select(x => x.Id).ToArray();
    }

    private static SqliteParameter IntegerParameter(string name, int value) =>
        new(name, SqliteType.Integer) { Value = value };
}
