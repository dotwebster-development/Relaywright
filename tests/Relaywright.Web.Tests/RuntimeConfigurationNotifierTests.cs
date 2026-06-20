using Relaywright.Web.Services.Relay;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class RuntimeConfigurationNotifierTests
{
    [Fact]
    public async Task WaiterCompletesWhenSettingsChange()
    {
        var notifier = new RuntimeConfigurationNotifier();
        var knownVersion = notifier.CurrentVersion;

        var waitTask = notifier.WaitForSmtpSettingsChangeAsync(knownVersion, CancellationToken.None);
        var changedVersion = notifier.NotifySmtpSettingsChanged();

        Assert.Equal(changedVersion, await waitTask);
    }

    [Fact]
    public async Task OldVersionReturnsImmediately()
    {
        var notifier = new RuntimeConfigurationNotifier();
        var oldVersion = notifier.CurrentVersion;
        var changedVersion = notifier.NotifySmtpSettingsChanged();

        var observedVersion = await notifier.WaitForSmtpSettingsChangeAsync(oldVersion, CancellationToken.None);

        Assert.Equal(changedVersion, observedVersion);
    }
}
