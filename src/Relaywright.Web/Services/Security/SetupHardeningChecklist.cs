using Microsoft.Extensions.Hosting;
using Relaywright.Web.Options;

namespace Relaywright.Web.Services.Security;

public sealed record SetupHardeningChecklist(IReadOnlyList<SetupHardeningItem> Items)
{
    public static SetupHardeningChecklist Create(
        bool adminExists,
        PasswordPolicySummary passwordPolicy,
        AdminHttpsCertificateConfiguration? certificate,
        AdminWebListenerConfiguration? listener,
        BootstrapAdminOptions bootstrapOptions,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(passwordPolicy);
        ArgumentNullException.ThrowIfNull(bootstrapOptions);
        ArgumentNullException.ThrowIfNull(environment);
        return new SetupHardeningChecklist(
        [
            new("Admin account", adminExists ? "Created" : "Required", adminExists ? "An administrator already exists." : "The first administrator will be created in this setup.", adminExists ? "status-enabled" : "severity-warning"),
            new("Password policy", "Active", passwordPolicy.CompactDescription, "status-enabled"),
            CreateBootstrapPasswordItem(bootstrapOptions, environment),
            new("HTTPS certificate", certificate is null ? "Not configured" : "Configured", certificate is null ? "Relaywright will use the current hosting certificate until one is saved." : $"{certificate.Mode} certificate saved for the admin web listener.", certificate is null ? "severity-warning" : "status-enabled"),
            new("HTTP listener", listener?.EnableHttp == true ? "HTTP enabled" : "HTTPS only", listener?.EnableHttp == true ? $"HTTP is configured on port {listener.HttpPort}; keep it limited to trusted networks." : "No Relaywright-managed HTTP listener is enabled.", listener?.EnableHttp == true ? "severity-warning" : "status-enabled")
        ]);
    }

    private static SetupHardeningItem CreateBootstrapPasswordItem(BootstrapAdminOptions options, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(options.Password))
        {
            return new("Default password guard", "First-run setup", "No bootstrap password is configured; admin creation stays in the setup flow.", "status-enabled");
        }

        if (string.Equals(options.Password, BootstrapAdminOptions.DefaultDevelopmentPassword, StringComparison.Ordinal))
        {
            return new(
                "Default password guard",
                environment.IsDevelopment() ? "Development only" : "Blocked",
                environment.IsDevelopment() ? "The default development password is accepted only in Development." : "Startup blocks the default development password outside Development.",
                environment.IsDevelopment() ? "severity-warning" : "status-failed");
        }

        return new("Default password guard", "Non-default", "Configured bootstrap credentials are not the development default.", "status-enabled");
    }
}
