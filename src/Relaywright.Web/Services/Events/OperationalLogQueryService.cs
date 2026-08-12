using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Options;

namespace Relaywright.Web.Services.Events;

public sealed record OperationalLogQueryRequest(
    EventSeverity? Severity,
    OperationalEventCategory? Category,
    string? Search,
    int PageNumber,
    int PageSize = 50);

public sealed record OperationalLogQueryResult(
    int PageNumber,
    int TotalCount,
    IReadOnlyList<OperationalEvent> Events);

public sealed class OperationalLogQueryService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    DatabaseConfiguration databaseConfiguration)
{
    public async Task<OperationalLogQueryResult> QueryAsync(
        OperationalLogQueryRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Max(1, request.PageSize);
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.OperationalEvents.AsNoTracking().AsQueryable();
        if (request.Severity is not null)
            query = query.Where(x => x.Severity == request.Severity);
        if (request.Category is not null)
            query = query.Where(x => x.Category == request.Category);
        if (search is not null)
        {
            query = query.Where(x =>
                x.Message.Contains(search)
                || (x.Detail != null && x.Detail.Contains(search))
                || (x.RemoteIpAddress != null && x.RemoteIpAddress.Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        pageNumber = Math.Min(pageNumber, totalPages);
        var offset = (pageNumber - 1) * pageSize;
        var events = databaseConfiguration.IsSqlite
            ? await GetSqlitePageAsync(
                dbContext,
                request.Severity,
                request.Category,
                search,
                offset,
                pageSize,
                cancellationToken)
            : await query
                .OrderByDescending(x => x.OccurredUtc)
                .Skip(offset)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        return new OperationalLogQueryResult(pageNumber, totalCount, events);
    }

    private static async Task<IReadOnlyList<OperationalEvent>> GetSqlitePageAsync(
        ApplicationDbContext dbContext,
        EventSeverity? severity,
        OperationalEventCategory? category,
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
        if (search is not null)
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
        return await dbContext.OperationalEvents
            .FromSqlRaw(sql, parameters.ToArray())
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private static SqliteParameter IntegerParameter(string name, int value) =>
        new(name, SqliteType.Integer) { Value = value };
}
