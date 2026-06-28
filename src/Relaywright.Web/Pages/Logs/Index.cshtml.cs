using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Pages.Logs;

public sealed class IndexModel(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
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

        TotalCount = await query.CountAsync(cancellationToken);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        var (sql, parameters) = BuildPagedEventsSql(
            Severity,
            Category,
            Search?.Trim(),
            (PageNumber - 1) * PageSize,
            PageSize);

        Events = await dbContext.OperationalEvents
            .FromSqlRaw(sql, parameters)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

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

    private static (string Sql, object[] Parameters) BuildPagedEventsSql(
        EventSeverity? severity,
        OperationalEventCategory? category,
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
}
