using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Services.Runtime;

public sealed class DashboardReadinessService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IAdminHttpsCertificateService adminHttpsCertificateService,
    IAdminWebListenerConfigurationService adminWebListenerConfigurationService,
    ILogger<DashboardReadinessService> logger) : IDashboardReadinessService
{
    private static readonly string[] SeededLoopbackNetworks = ["127.0.0.1/32", "::1/128"];

    public async Task<DashboardReadinessSnapshot> GetSnapshotAsync(
        RelayConfigurationSnapshot configuration,
        BackupReadiness backupReadiness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(backupReadiness);

        var now = DateTimeOffset.UtcNow;
        var certificate = await adminHttpsCertificateService.GetConfigurationAsync(cancellationToken);
        var listener = await adminWebListenerConfigurationService.GetConfigurationAsync(cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var enabledNetworks = await dbContext.TrustedNetworks
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .Select(x => x.Cidr)
            .ToListAsync(cancellationToken);
        var submissionPolicyReviewed = await dbContext.ConfigurationSnapshots
            .AsNoTracking()
            .AnyAsync(x => x.Area == ConfigurationSnapshotService.SubmissionPolicyArea, cancellationToken);
        var successfulDiagnostics = await dbContext.DiagnosticRuns
            .AsNoTracking()
            .Where(x => x.Succeeded == true && x.CompletedUtc != null)
            .Select(x => new { x.Kind, x.CompletedUtc })
            .ToListAsync(cancellationToken);
        var alertEmailConfigured = await dbContext.AlertRules
            .AsNoTracking()
            .AnyAsync(
                x => x.IsEnabled && x.EmailRecipients != null && x.EmailRecipients != string.Empty,
                cancellationToken);

        var upstreamConfigured = !string.IsNullOrWhiteSpace(configuration.UpstreamHost);
        var hasNonLoopbackTrustedDevice = enabledNetworks.Any(
            cidr => !SeededLoopbackNetworks.Contains(cidr, StringComparer.OrdinalIgnoreCase));
        var connectivityVerified = upstreamConfigured && successfulDiagnostics.Any(
            x => x.Kind == DiagnosticRunKind.Connectivity
                && x.CompletedUtc >= configuration.UpdatedUtc);
        var testEmailDelivered = upstreamConfigured && successfulDiagnostics.Any(
            x => x.Kind == DiagnosticRunKind.TestEmail
                && x.CompletedUtc >= configuration.UpdatedUtc);

        var items = new List<DashboardReadinessItem>
        {
            CreateHttpsItem(certificate, listener, now),
            CreateItem(
                "upstream",
                "Upstream relay",
                upstreamConfigured,
                upstreamConfigured ? "Configured" : "Action needed",
                upstreamConfigured
                    ? $"{configuration.UpstreamHost}:{configuration.UpstreamPort} is configured as the delivery target."
                    : "Configure the upstream SMTP smart host before accepting production traffic.",
                "/Settings/Relay",
                upstreamConfigured ? "Review settings" : "Configure upstream"),
            CreateItem(
                "trusted-device",
                "Trusted device",
                hasNonLoopbackTrustedDevice,
                hasNonLoopbackTrustedDevice ? "Configured" : "Action needed",
                hasNonLoopbackTrustedDevice
                    ? $"{enabledNetworks.Count} enabled trusted network rule(s), including at least one non-loopback source."
                    : "Add the IP address or CIDR of a device that is allowed to submit mail.",
                "/Settings/TrustedNetworks",
                hasNonLoopbackTrustedDevice ? "Review devices" : "Add trusted device"),
            CreateItem(
                "submission-policy",
                "Submission policy",
                submissionPolicyReviewed,
                submissionPolicyReviewed ? "Reviewed" : "Review required",
                submissionPolicyReviewed
                    ? "The submission policy has been explicitly saved by an operator."
                    : "Review sender, recipient, message-size, and recipient-count limits, then save the policy.",
                "/Settings/SubmissionPolicy",
                submissionPolicyReviewed ? "Review policy" : "Review and save"),
            CreateItem(
                "connectivity",
                "Connectivity test",
                connectivityVerified,
                connectivityVerified ? "Passed" : "Action needed",
                connectivityVerified
                    ? "A successful upstream connectivity test was recorded after the current relay settings were saved."
                    : upstreamConfigured
                        ? "Run the upstream connectivity diagnostic against the current relay settings."
                        : "Configure the upstream relay before running connectivity diagnostics.",
                upstreamConfigured ? "/Diagnostics/Index" : "/Settings/Relay",
                connectivityVerified ? "View diagnostics" : upstreamConfigured ? "Run diagnostics" : "Configure upstream"),
            CreateItem(
                "test-email",
                "Test email",
                testEmailDelivered,
                testEmailDelivered ? "Delivered" : "Action needed",
                testEmailDelivered
                    ? "A diagnostic test email was delivered after the current relay settings were saved."
                    : upstreamConfigured
                        ? "Send a diagnostic test email and confirm successful upstream delivery."
                        : "Configure the upstream relay before sending a diagnostic test email.",
                upstreamConfigured ? "/Diagnostics/TestEmail" : "/Settings/Relay",
                testEmailDelivered ? "View test runs" : upstreamConfigured ? "Send test email" : "Configure upstream"),
            CreateItem(
                "backup",
                "Backup readiness",
                backupReadiness.IsReady,
                backupReadiness.IsReady ? "Ready" : "Action needed",
                backupReadiness.Message,
                "/Operations/Backups",
                backupReadiness.IsReady ? "Review backups" : "Create and validate"),
            CreateItem(
                "alert-email",
                "Alert email routing",
                alertEmailConfigured,
                alertEmailConfigured ? "Configured" : "Optional",
                alertEmailConfigured
                    ? "At least one enabled alert rule has email recipients configured."
                    : "Optionally route important alert rules to one or more operator mailboxes.",
                "/Operations/Alerts",
                alertEmailConfigured ? "Review alerts" : "Configure notifications",
                isRequired: false)
        };

        var snapshot = new DashboardReadinessSnapshot(items);
        logger.LogDebug(
            "Dashboard readiness evaluated. RequiredComplete={CompletedRequiredCount}; RequiredTotal={RequiredCount}; OptionalAlertEmailConfigured={AlertEmailConfigured}",
            snapshot.CompletedRequiredCount,
            snapshot.RequiredCount,
            alertEmailConfigured);
        return snapshot;
    }

    private static DashboardReadinessItem CreateHttpsItem(
        AdminHttpsCertificateConfiguration? certificate,
        AdminWebListenerConfiguration? listener,
        DateTimeOffset now)
    {
        var httpDisabled = listener?.EnableHttp != true;
        var managedCertificateValid = certificate is null
            || certificate.NotAfterUtc is not null && certificate.NotAfterUtc > now;
        var ready = httpDisabled && managedCertificateValid;

        string detail;
        if (!httpDisabled)
        {
            detail = $"The admin HTTP listener is enabled on port {listener!.HttpPort}; disable it unless a trusted-network exception is required.";
        }
        else if (certificate is null)
        {
            detail = "The admin listener is HTTPS-only; certificate ownership remains with the deployment configuration.";
        }
        else if (certificate.NotAfterUtc is null)
        {
            detail = "The managed admin certificate does not expose an expiry date and should be reviewed.";
        }
        else if (certificate.NotAfterUtc <= now)
        {
            detail = $"The managed admin certificate expired {certificate.NotAfterUtc.Value.ToLocalTime():g}.";
        }
        else
        {
            detail = $"The admin listener is HTTPS-only and the managed certificate is valid until {certificate.NotAfterUtc.Value.ToLocalTime():g}.";
        }

        return CreateItem(
            "admin-https",
            "Admin HTTPS",
            ready,
            ready ? "Ready" : "Action needed",
            detail,
            !httpDisabled || certificate is null ? "/Settings/WebHttps" : "/Settings/WebCertificate",
            ready ? "Review HTTPS" : "Fix HTTPS");
    }

    private static DashboardReadinessItem CreateItem(
        string key,
        string label,
        bool complete,
        string status,
        string detail,
        string page,
        string actionLabel,
        bool isRequired = true)
    {
        return new DashboardReadinessItem(
            key,
            label,
            status,
            detail,
            complete
                ? "status-enabled"
                : isRequired
                    ? "severity-warning"
                    : "status-unknown",
            page,
            actionLabel,
            complete,
            isRequired);
    }
}
