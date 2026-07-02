namespace Relaywright.Web.Services.Backups;

public interface IBackupCoordinator
{
    Task<IAsyncDisposable> AcquireSpoolDeletionLockAsync(CancellationToken cancellationToken);
}

public sealed class BackupCoordinator : IBackupCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IAsyncDisposable> AcquireSpoolDeletionLockAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Releaser(_gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
