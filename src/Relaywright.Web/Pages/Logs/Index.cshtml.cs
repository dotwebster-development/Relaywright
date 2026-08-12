using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Pages.Logs;

public sealed class IndexModel(
    OperationalLogQueryService queryService,
    ILogger<IndexModel> logger) : PageModel
{
    private const int PageSize = 50;

    public sealed record LogSection(string? CategoryValue, string Label);

    [BindProperty(SupportsGet = true)]
    public EventSeverity? Severity { get; set; }

    [BindProperty(SupportsGet = true)]
    public OperationalEventCategory? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    [StringLength(256)]
    [NoControlCharacters]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    [Range(1, int.MaxValue)]
    public int PageNumber { get; set; } = 1;

    public int TotalCount { get; private set; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public IReadOnlyList<LogSection> Sections { get; } =
    [
        new(null, "All"),
        new(nameof(OperationalEventCategory.System), "System"),
        new(nameof(OperationalEventCategory.Configuration), "Configuration"),
        new(nameof(OperationalEventCategory.Security), "Security"),
        new(nameof(OperationalEventCategory.SmtpSession), "SMTP Session"),
        new(nameof(OperationalEventCategory.Queue), "Queue"),
        new(nameof(OperationalEventCategory.Delivery), "Delivery"),
        new(nameof(OperationalEventCategory.Diagnostics), "Diagnostics"),
        new(nameof(OperationalEventCategory.Alert), "Alerts")
    ];

    public IReadOnlyList<OperationalEvent> Events { get; private set; } = [];

    public bool IsActiveSection(string? categoryValue) =>
        string.IsNullOrWhiteSpace(categoryValue)
            ? Category is null
            : string.Equals(Category?.ToString(), categoryValue, StringComparison.OrdinalIgnoreCase);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        PageNumber = Math.Max(1, PageNumber);
        if (!ModelState.IsValid)
        {
            logger.LogWarning(
                "Logs page rejected invalid query values. ErrorCount={ErrorCount}; User={UserName}",
                ModelState.ErrorCount,
                User.Identity?.Name);
            return;
        }

        var result = await queryService.QueryAsync(
            new OperationalLogQueryRequest(Severity, Category, Search, PageNumber, PageSize),
            cancellationToken);
        PageNumber = result.PageNumber;
        TotalCount = result.TotalCount;
        Events = result.Events;

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
}
