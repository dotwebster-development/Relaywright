namespace Relaywright.Web.Services.Relay;

public interface IRuntimeConfigurationNotifier
{
    long CurrentVersion { get; }

    long NotifySmtpSettingsChanged();

    Task<long> WaitForSmtpSettingsChangeAsync(long knownVersion, CancellationToken cancellationToken);
}

