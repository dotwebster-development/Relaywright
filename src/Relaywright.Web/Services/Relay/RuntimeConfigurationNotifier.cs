namespace Relaywright.Web.Services.Relay;

public sealed class RuntimeConfigurationNotifier : IRuntimeConfigurationNotifier
{
    private readonly object _gate = new();
    private long _version;
    private TaskCompletionSource<long> _changeSource = NewChangeSource();

    public long CurrentVersion
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    public long NotifySmtpSettingsChanged()
    {
        TaskCompletionSource<long> previous;
        long version;

        lock (_gate)
        {
            version = ++_version;
            previous = _changeSource;
            _changeSource = NewChangeSource();
        }

        previous.TrySetResult(version);
        return version;
    }

    public Task<long> WaitForSmtpSettingsChangeAsync(long knownVersion, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_version != knownVersion)
            {
                return Task.FromResult(_version);
            }

            return _changeSource.Task.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource<long> NewChangeSource()
    {
        return new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
