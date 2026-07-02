using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Options;

namespace Relaywright.Web.Pages.Logs;

public sealed class IndexModel(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    DatabaseConfiguration databaseConfiguration,
    ILogger<IndexModel> logger) : PageModel
{
    private const int PageSize = 50;

    public sealed record LogSection(string? CategoryValue, string Label);

    [BindProperty(SupportsGet = true)]
    public EventSeverity? Severity { get; set; }

    [BindProperty(SupportsGet = true)]
    public OperationalEventCategory? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? SessionId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? QueuedMessageId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? RemoteIp { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public int TotalCount { get; private set; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public IReadOnlyList<LogSection> Sections { get; } =
    [
        new LogSection(null, "All"),
        new LogSection(nameof(OperationalEventCategory.System), "System"),
        new LogSection(nameof(OperationalEventCategory.Configuration), "Configuration"),
        new LogSection(nameof(OperationalEventCategory.Security), "Security"),
        new LogSection(nameof(OperationalEventCategory.SmtpSession), "SMTP Session"),
        new LogSection(nameof(OperationalEventCategory.Queue), "Queue"),
        new LogSection(nameof(OperationalEventCategory.Delivery), "Delivery"),
        new LogSection(nameof(OperationalEventCategory.Diagnostics), "Diagnostics"),
        new LogSection(nameof(OperationalEventCategory.Alert), "Alerts")
    ];

    public IReadOnlyList<OperationalEvent> Events { get; private set; } = Array.Empty<OperationalEvent>();

    public LogFilterSummary Summary { get; private set; } = LogFilterSummary.Empty;

    public bool IsActiveSection(string? categoryValue)
    {
        return string.IsNullOrWhiteSpace(categoryValue)
            ? Category is null
            : string.Equals(Category?.ToString(), categoryValue, StringComparison.OrdinalIgnoreCase);
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        PageNumber = Math.Max(1, PageNumber);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.OperationalEvents.AsNoTracking().AsQueryable();

        if (Severity is not null)
        {
            query = query.Where(x => x.Severity == Severity);
        }

        if (Category is not null)
        {
            query = query.Where(x => x.Category == Category);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var search = Search.Trim();
            query = query.Where(x =>
                x.Message.Contains(search)
                || (x.Detail != null && x.Detail.Contains(search))
                || (x.RemoteIpAddress != null && x.RemoteIpAddress.Contains(search)));
        }

        if (SessionId is not null)
        {
            query = query.Where(x => x.SessionId == SessionId.Value);
        }

        if (QueuedMessageId is not null)
        {
            query = query.Where(x => x.QueuedMessageId == QueuedMessageId.Value);
        }

        if (!string.IsNullOrWhiteSpace(RemoteIp))
        {
            var remoteIp = RemoteIp.Trim();
            query = query.Where(x => x.RemoteIpAddress == remoteIp);
        }

        TotalCount = await query.CountAsync(cancellationToken);
        Summary = await BuildSummaryAsync(query, TotalCount, cancellationToken);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        var offset = (PageNumber - 1) * PageSize;
        if (databaseConfiguration.IsSqlite && (SessionId is not null || QueuedMessageId is not null))
        {
            Events = await GetClientOrderedPagedEventsAsync(query, offset, PageSize, cancellationToken);
        }
        else if (databaseConfiguration.IsSqlite)
        {
            Events = await GetSqlitePagedEventsAsync(
                dbContext,
                Severity,
                Category,
                Search?.Trim(),
                SessionId,
                QueuedMessageId,
                RemoteIp?.Trim(),
                offset,
                PageSize,
                cancellationToken);
        }
        else
        {
            Events = await query
                .OrderByDescending(x => x.OccurredUtc)
                .Skip(offset)
                .Take(PageSize)
                .ToListAsync(cancellationToken);
        }

        logger.LogDebug(
            "Logs page loaded. Severity={Severity}; Category={Category}; SearchPresent={SearchPresent}; PageNumber={PageNumber}; TotalCount={TotalCount}; ReturnedCount={ReturnedCount}; User={UserName}",
            Severity,
            Category,
            !string.IsNullOrWhiteSpace(Search),
            PageNumber,
            TotalCount,
            Events.Count,
            User.Identity?.Name);
    }

    private static async Task<IReadOnlyList<OperationalEvent>> GetClientOrderedPagedEventsAsync(
        IQueryable<OperationalEvent> query,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var events = await query
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return events
            .OrderByDescending(x => x.OccurredUtc)
            .Skip(offset)
            .Take(pageSize)
            .ToList();
    }

    private static async Task<LogFilterSummary> BuildSummaryAsync(
        IQueryable<OperationalEvent> query,
        int totalCount,
        CancellationToken cancellationToken)
    {
        if (totalCount == 0)
        {
            return LogFilterSummary.Empty;
        }

        var summaryRows = await query
            .Select(x => new { x.Severity, x.Category, x.OccurredUtc })
            .ToListAsync(cancellationToken);
        var severityCounts = summaryRows
            .GroupBy(x => x.Severity)
            .ToDictionary(x => x.Key, x => x.Count());
        var categoryCounts = summaryRows
            .GroupBy(x => x.Category)
            .Select(x => new LogCategoryCount(x.Key, x.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Category)
            .ToList();
        var oldestUtc = summaryRows.Min(x => x.OccurredUtc);
        var newestUtc = summaryRows.Max(x => x.OccurredUtc);

        return new LogFilterSummary(
            totalCount,
            severityCounts.GetValueOrDefault(EventSeverity.Information),
            severityCounts.GetValueOrDefault(EventSeverity.Warning),
            severityCounts.GetValueOrDefault(EventSeverity.Error),
            oldestUtc,
            newestUtc,
            categoryCounts);
    }

    private static (string Sql, object[] Parameters) BuildPagedEventsSql(
        EventSeverity? severity,
        OperationalEventCategory? category,
        string? search,
        Guid? sessionId,
        Guid? queuedMessageId,
        string? remoteIp,
        int offset,
        int pageSize)
    {
        var filters = new List<string>();
        var parameters = new List<object>
        {
            IntegerParameter("$limit", pageSize),
            IntegerParameter("$offset", offset)
        };

        if (severity is not null)
        {
            filters.Add(@"oe.""Severity"" = $severity");
            parameters.Add(IntegerParameter("$severity", (int)severity.Value));
        }

        if (category is not null)
        {
            filters.Add(@"oe.""Category"" = $category");
            parameters.Add(IntegerParameter("$category", (int)category.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filters.Add("""
                (instr(oe."Message", $search) > 0
                    OR (oe."Detail" IS NOT NULL AND instr(oe."Detail", $search) > 0)
                    OR (oe."RemoteIpAddress" IS NOT NULL AND instr(oe."RemoteIpAddress", $search) > 0))
                """);
            parameters.Add(new SqliteParameter("$search", search));
        }

        if (sessionId is not null)
        {
            filters.Add(@"oe.""SessionId"" = $sessionId");
            parameters.Add(new SqliteParameter("$sessionId", sessionId.Value.ToString()));
        }

        if (queuedMessageId is not null)
        {
            filters.Add(@"oe.""QueuedMessageId"" = $queuedMessageId");
            parameters.Add(new SqliteParameter("$queuedMessageId", queuedMessageId.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            filters.Add(@"oe.""RemoteIpAddress"" = $remoteIp");
            parameters.Add(new SqliteParameter("$remoteIp", remoteIp));
        }

        var whereSql = filters.Count == 0
            ? string.Empty
            : $"{Environment.NewLine}WHERE {string.Join($"{Environment.NewLine}    AND ", filters)}";

        var sql = $"""
            SELECT oe.*
            FROM "OperationalEvents" AS oe{whereSql}
            ORDER BY oe."OccurredUtc" DESC
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

    private static async Task<IReadOnlyList<OperationalEvent>> GetSqlitePagedEventsAsync(
        ApplicationDbContext dbContext,
        EventSeverity? severity,
        OperationalEventCategory? category,
        string? search,
        Guid? sessionId,
        Guid? queuedMessageId,
        string? remoteIp,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var (sql, parameters) = BuildPagedEventsSql(
            severity,
            category,
            search,
            sessionId,
            queuedMessageId,
            remoteIp,
            offset,
            pageSize);
        return await dbContext.OperationalEvents
            .FromSqlRaw(sql, parameters)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}

public sealed record LogCategoryCount(
    OperationalEventCategory Category,
    int Count);

public sealed record LogFilterSummary(
    int TotalCount,
    int InformationCount,
    int WarningCount,
    int ErrorCount,
    DateTimeOffset? OldestUtc,
    DateTimeOffset? NewestUtc,
    IReadOnlyList<LogCategoryCount> CategoryCounts)
{
    public static LogFilterSummary Empty { get; } = new(
        0,
        0,
        0,
        0,
        null,
        null,
        Array.Empty<LogCategoryCount>());
}
