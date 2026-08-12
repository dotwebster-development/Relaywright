using Relaywright.Web.Services.Runtime;

namespace Relaywright.Web.Services.Security;

public sealed record AdminWebSecuritySummary(
    bool HasManagedListener,
    int HttpsPort,
    bool HttpEnabled,
    int HttpPort,
    DateTimeOffset? ListenerUpdatedUtc,
    bool HasManagedCertificate,
    AdminHttpsCertificateMode? CertificateMode,
    IReadOnlyList<string> CertificateDnsNames,
    DateTimeOffset? CertificateExpiresUtc,
    DateTimeOffset? CertificateUpdatedUtc,
    bool RestartRequired,
    string? RestartReason,
    string? RestartRequestedBy,
    DateTimeOffset? RestartRequestedUtc)
{
    public static AdminWebSecuritySummary Create(
        AdminWebListenerConfiguration? listener,
        AdminHttpsCertificateConfiguration? certificate,
        RuntimeStatusSnapshot runtimeStatus,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(runtimeStatus);
        var effectiveListener = listener ?? new AdminWebListenerConfiguration();
        return new AdminWebSecuritySummary(
            listener is not null,
            effectiveListener.HttpsPort,
            effectiveListener.EnableHttp,
            effectiveListener.HttpPort,
            listener?.UpdatedUtc,
            certificate is not null,
            certificate?.Mode,
            certificate?.DnsNames ?? [],
            certificate?.NotAfterUtc,
            certificate?.UpdatedUtc,
            runtimeStatus.RestartRequired,
            runtimeStatus.RestartReason,
            runtimeStatus.RestartRequestedBy,
            runtimeStatus.RestartRequestedUtc);
    }

    public string ListenerSourceLabel => HasManagedListener ? "Relaywright-managed" : "Deployment default";
    public string HttpStatusLabel => HttpEnabled ? $"Enabled on port {HttpPort}" : "Disabled";
    public string HttpBadgeClass => HttpEnabled ? "severity-warning" : "status-enabled";
    public string CertificateModeLabel => HasManagedCertificate ? CertificateMode?.ToString() ?? "Configured" : "Hosting or deployment certificate";
    public string CertificateDnsLabel => CertificateDnsNames.Count == 0 ? "Not available" : string.Join(", ", CertificateDnsNames);

    public string CertificateStatusLabel(DateTimeOffset now)
    {
        if (!HasManagedCertificate) return "Not managed by Relaywright";
        if (CertificateExpiresUtc is null) return "Expiry not available";
        if (CertificateExpiresUtc <= now) return "Expired";
        return CertificateExpiresUtc <= now.AddDays(30) ? "Expiring soon" : "Valid";
    }

    public string CertificateBadgeClass(DateTimeOffset now)
    {
        if (!HasManagedCertificate || CertificateExpiresUtc is null) return "status-unknown";
        if (CertificateExpiresUtc <= now) return "status-failed";
        return CertificateExpiresUtc <= now.AddDays(30) ? "severity-warning" : "status-enabled";
    }

    public string RestartStatusLabel => RestartRequired ? "Restart required" : "Current";
    public string RestartBadgeClass => RestartRequired ? "severity-warning" : "status-enabled";
}
