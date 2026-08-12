using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Pages.Queue;

public sealed class IndexModel(
    QueueQueryService queryService,
    IQueueOperatorService messageQueueService,
    ILogger<IndexModel> logger) : PageModel
{
    private const int PageSize = 50;

    public string SelectedStatus { get; private set; } = "active";

    [BindProperty(SupportsGet = true)]
    [StringLength(256)]
    [NoControlCharacters]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    [Range(1, int.MaxValue)]
    public int PageNumber { get; set; } = 1;

    [BindProperty]
    public List<Guid> SelectedMessageIds { get; set; } = [];

    [BindProperty]
    [StringLength(256)]
    [NoControlCharacters]
    public string? ReturnStatus { get; set; }

    [BindProperty]
    [StringLength(256)]
    [NoControlCharacters]
    public string? ReturnSearch { get; set; }

    [BindProperty]
    [Range(1, int.MaxValue)]
    public int ReturnPageNumber { get; set; } = 1;

    [TempData]
    public string? StatusMessage { get; set; }

    public int TotalCount { get; private set; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public IReadOnlyList<QueuedMessage> Messages { get; private set; } = [];

    public async Task OnGetAsync(string? status, CancellationToken cancellationToken)
    {
        SelectedStatus = QueueQueryService.NormalizeStatus(status);
        PageNumber = Math.Max(1, PageNumber);
        if (!ModelState.IsValid)
        {
            logger.LogWarning(
                "Queue page rejected invalid query values. Status={Status}; ErrorCount={ErrorCount}; User={UserName}",
                status,
                ModelState.ErrorCount,
                User.Identity?.Name);
            return;
        }

        var result = await queryService.QueryAsync(
            new QueueQueryRequest(SelectedStatus, Search, PageNumber, PageSize),
            cancellationToken);
        SelectedStatus = result.Status;
        Search = result.Search;
        PageNumber = result.PageNumber;
        TotalCount = result.TotalCount;
        Messages = result.Messages;

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
        if (!ModelState.IsValid)
        {
            StatusMessage = "Queue action rejected because the request contained invalid values.";
            return RedirectToPage();
        }

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
        if (!ModelState.IsValid)
        {
            StatusMessage = "Queue action rejected because the request contained invalid values.";
            return RedirectToPage();
        }

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

    private IActionResult RedirectToQueue() =>
        RedirectToPage(new
        {
            status = QueueQueryService.NormalizeStatus(ReturnStatus),
            search = ReturnSearch,
            pageNumber = Math.Max(1, ReturnPageNumber)
        });
}
