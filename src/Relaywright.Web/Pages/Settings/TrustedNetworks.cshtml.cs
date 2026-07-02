using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Pages.Settings;

public sealed class TrustedNetworksModel(
    ITrustedNetworkService trustedNetworkService,
    ITrustedDevicePolicyService trustedDevicePolicyService,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IConfigurationSnapshotService configurationSnapshotService,
    ILogger<TrustedNetworksModel> logger) : PageModel
{
    private const int ActivityEventLimit = 1000;
    private static readonly TimeSpan StaleActivityAge = TimeSpan.FromDays(30);

    [BindProperty]
    public TrustedNetwork Input { get; set; } = new();

    public IReadOnlyList<TrustedNetwork> Networks { get; private set; } = Array.Empty<TrustedNetwork>();

    public IReadOnlyDictionary<int, TrustedDevicePolicySummary> EffectivePolicies { get; private set; } =
        new Dictionary<int, TrustedDevicePolicySummary>();

    public IReadOnlyDictionary<int, TrustedNetworkActivitySummary> ActivitySummaries { get; private set; } =
        new Dictionary<int, TrustedNetworkActivitySummary>();

    public DateTimeOffset LoadedUtc { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(int? editId, CancellationToken cancellationToken)
    {
        await LoadNetworksAsync(cancellationToken);
        Input = editId is null
            ? new TrustedNetwork { IsEnabled = true }
            : Networks.FirstOrDefault(x => x.Id == editId.Value) ?? new TrustedNetwork { IsEnabled = true };

        logger.LogDebug(
            "Trusted networks page loaded. EditId={EditId}; NetworkCount={NetworkCount}; User={UserName}",
            editId,
            Networks.Count,
            User.Identity?.Name);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await configurationSnapshotService.CaptureAsync(
                ConfigurationSnapshotService.TrustedNetworksArea,
                User.Identity?.Name,
                "Snapshot before trusted network save.",
                cancellationToken);
            await trustedNetworkService.AddOrUpdateAsync(Input, cancellationToken);
            StatusMessage = "Trusted network saved.";
            logger.LogInformation(
                "Trusted network save completed from admin page. Id={TrustedNetworkId}; Cidr={Cidr}; Description={Description}; Enabled={Enabled}; User={UserName}",
                Input.Id,
                Input.Cidr,
                Input.Description,
                Input.IsEnabled,
                User.Identity?.Name);

            return RedirectToPage();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Trusted network save failed from admin page. Id={TrustedNetworkId}; Cidr={Cidr}; Description={Description}; Enabled={Enabled}; User={UserName}",
                Input.Id,
                Input.Cidr,
                Input.Description,
                Input.IsEnabled,
                User.Identity?.Name);

            await LoadNetworksAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        await configurationSnapshotService.CaptureAsync(
            ConfigurationSnapshotService.TrustedNetworksArea,
            User.Identity?.Name,
            "Snapshot before trusted network delete.",
            cancellationToken);
        await trustedNetworkService.DeleteAsync(id, cancellationToken);
        StatusMessage = "Trusted network deleted.";
        logger.LogInformation("Trusted network delete requested from admin page. Id={TrustedNetworkId}; User={UserName}", id, User.Identity?.Name);
        return RedirectToPage();
    }

    public static string FormatProfile(TrustedNetwork network)
    {
        var parts = new[] { network.Owner, network.Location }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return parts.Length == 0 ? "Unassigned" : string.Join(" / ", parts);
    }

    public static string FormatLimits(TrustedNetwork network)
    {
        var parts = new List<string>();
        if (network.MaxMessageSizeBytes is > 0)
        {
            parts.Add($"size {network.MaxMessageSizeBytes.Value:N0} B");
        }

        if (network.MaxRecipientsPerMessage is > 0)
        {
            parts.Add($"{network.MaxRecipientsPerMessage.Value:N0} recipients");
        }

        if (network.RateLimitMessagesPerHour is > 0)
        {
            parts.Add($"{network.RateLimitMessagesPerHour.Value:N0}/hour");
        }

        return parts.Count == 0 ? "Global defaults" : string.Join("; ", parts);
    }

    public static string FormatPolicySummary(TrustedNetwork network)
    {
        var parts = new List<string>();
        AddCount(parts, "allowed senders", network.AllowedSenderAddresses);
        AddCount(parts, "blocked senders", network.BlockedSenderAddresses);
        AddCount(parts, "allowed domains", network.AllowedRecipientDomains);
        AddCount(parts, "blocked domains", network.BlockedRecipientDomains);

        return parts.Count == 0 ? "No device-specific lists" : string.Join("; ", parts);
    }

    public TrustedDevicePolicySummary? GetEffectivePolicy(int trustedNetworkId)
    {
        return EffectivePolicies.TryGetValue(trustedNetworkId, out var summary) ? summary : null;
    }

    public TrustedNetworkActivitySummary? GetActivitySummary(int trustedNetworkId)
    {
        return ActivitySummaries.TryGetValue(trustedNetworkId, out var summary) ? summary : null;
    }

    public static string FormatEffectivePolicy(TrustedDevicePolicySummary? summary)
    {
        if (summary is null)
        {
            return "Not available";
        }

        var parts = new List<string>
        {
            $"size {FormatLimit(summary.MessageSizeLimit)}",
            $"recipients {FormatLimit(summary.RecipientLimit)}"
        };

        if (summary.RateLimitMessagesPerHour is > 0)
        {
            parts.Add($"{summary.RateLimitMessagesPerHour.Value:N0}/hour");
        }

        var senderRules = summary.AllowedSenderRuleCount + summary.BlockedSenderRuleCount;
        var recipientRules = summary.AllowedRecipientDomainRuleCount + summary.BlockedRecipientDomainRuleCount;
        parts.Add($"{senderRules:N0} sender rule(s)");
        parts.Add($"{recipientRules:N0} domain rule(s)");

        return string.Join("; ", parts);
    }

    private static void AddCount(List<string> parts, string label, string? value)
    {
        var count = value?
            .Split([',', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length ?? 0;

        if (count > 0)
        {
            parts.Add($"{count:N0} {label}");
        }
    }

    private async Task LoadNetworksAsync(CancellationToken cancellationToken)
    {
        LoadedUtc = DateTimeOffset.UtcNow;
        Networks = await trustedNetworkService.GetAllAsync(cancellationToken);
        var policy = await trustedDevicePolicyService.GetPolicyAsync(cancellationToken);
        EffectivePolicies = Networks
            .ToDictionary(
                x => x.Id,
                x => trustedDevicePolicyService.DescribeEffectivePolicy(x, policy));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var activityEvents = await dbContext.OperationalEvents
            .AsNoTracking()
            .Where(x => x.RemoteIpAddress != null
                && (x.Category == OperationalEventCategory.Security
                    || x.Category == OperationalEventCategory.SmtpSession
                    || x.Category == OperationalEventCategory.Queue
                    || x.Category == OperationalEventCategory.Delivery))
            .OrderByDescending(x => x.Id)
            .Take(ActivityEventLimit)
            .Select(x => new TrustedNetworkActivityEvent(
                x.OccurredUtc,
                x.Category,
                x.RemoteIpAddress!,
                x.Message))
            .ToListAsync(cancellationToken);

        ActivitySummaries = BuildActivitySummaries(Networks, activityEvents, LoadedUtc);
    }

    private static string FormatLimit(EffectivePolicyLimit limit)
    {
        return limit.Value is null
            ? $"not set ({limit.Source})"
            : $"{limit.Value.Value:N0} ({limit.Source})";
    }

    public static IReadOnlyDictionary<int, TrustedNetworkActivitySummary> BuildActivitySummaries(
        IReadOnlyList<TrustedNetwork> networks,
        IReadOnlyList<TrustedNetworkActivityEvent> events,
        DateTimeOffset loadedUtc)
    {
        var parsedEvents = events
            .Select(x => new
            {
                Event = x,
                Parsed = IPAddress.TryParse(x.RemoteIpAddress, out var address) ? address : null
            })
            .Where(x => x.Parsed is not null)
            .ToList();
        var summaries = new Dictionary<int, TrustedNetworkActivitySummary>();

        foreach (var network in networks)
        {
            if (!CidrRange.TryParse(network.Cidr, out var range) || range is null)
            {
                summaries[network.Id] = new TrustedNetworkActivitySummary(
                    network.Id,
                    null,
                    null,
                    "Invalid CIDR",
                    "status-failed",
                    "CIDR could not be parsed.");
                continue;
            }

            var matchingEvents = parsedEvents
                .Where(x => range.Contains(x.Parsed!))
                .Select(x => x.Event)
                .OrderByDescending(x => x.OccurredUtc)
                .ToList();

            var lastSeen = matchingEvents.FirstOrDefault();
            if (lastSeen is null)
            {
                summaries[network.Id] = new TrustedNetworkActivitySummary(
                    network.Id,
                    null,
                    null,
                    "Unused",
                    "status-disabled",
                    "No matching activity found.");
                continue;
            }

            var decision = matchingEvents.FirstOrDefault(IsDecisionEvent) ?? lastSeen;
            var isStale = loadedUtc - lastSeen.OccurredUtc > StaleActivityAge;
            summaries[network.Id] = new TrustedNetworkActivitySummary(
                network.Id,
                lastSeen.OccurredUtc,
                lastSeen.RemoteIpAddress,
                isStale ? "Stale" : "Active",
                isStale ? "severity-warning" : "status-enabled",
                FormatDecision(decision));
        }

        return summaries;
    }

    private static bool IsDecisionEvent(TrustedNetworkActivityEvent activityEvent)
    {
        if (activityEvent.Category == OperationalEventCategory.Security)
        {
            return true;
        }

        return activityEvent.Message.Contains("accepted", StringComparison.OrdinalIgnoreCase)
            || activityEvent.Message.Contains("denied", StringComparison.OrdinalIgnoreCase)
            || activityEvent.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDecision(TrustedNetworkActivityEvent activityEvent)
    {
        var prefix = activityEvent.Message.Contains("accepted", StringComparison.OrdinalIgnoreCase)
            ? "Accepted"
            : activityEvent.Message.Contains("denied", StringComparison.OrdinalIgnoreCase)
                || activityEvent.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase)
                    ? "Rejected"
                    : activityEvent.Category.ToString();

        return $"{prefix}: {activityEvent.Message}";
    }
}

public sealed record TrustedNetworkActivityEvent(
    DateTimeOffset OccurredUtc,
    OperationalEventCategory Category,
    string RemoteIpAddress,
    string Message);

public sealed record TrustedNetworkActivitySummary(
    int TrustedNetworkId,
    DateTimeOffset? LastSeenUtc,
    string? LastRemoteIpAddress,
    string StatusLabel,
    string BadgeClass,
    string LastDecision);
