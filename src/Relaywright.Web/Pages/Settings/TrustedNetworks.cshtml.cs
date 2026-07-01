using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Pages.Settings;

public sealed class TrustedNetworksModel(
    ITrustedNetworkService trustedNetworkService,
    ITrustedDevicePolicyService trustedDevicePolicyService,
    IConfigurationSnapshotService configurationSnapshotService,
    ILogger<TrustedNetworksModel> logger) : PageModel
{
    [BindProperty]
    public TrustedNetwork Input { get; set; } = new();

    public IReadOnlyList<TrustedNetwork> Networks { get; private set; } = Array.Empty<TrustedNetwork>();

    public IReadOnlyDictionary<int, TrustedDevicePolicySummary> EffectivePolicies { get; private set; } =
        new Dictionary<int, TrustedDevicePolicySummary>();

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
        Networks = await trustedNetworkService.GetAllAsync(cancellationToken);
        var policy = await trustedDevicePolicyService.GetPolicyAsync(cancellationToken);
        EffectivePolicies = Networks
            .ToDictionary(
                x => x.Id,
                x => trustedDevicePolicyService.DescribeEffectivePolicy(x, policy));
    }

    private static string FormatLimit(EffectivePolicyLimit limit)
    {
        return limit.Value is null
            ? $"not set ({limit.Source})"
            : $"{limit.Value.Value:N0} ({limit.Source})";
    }
}
