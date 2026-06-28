using Relaywright.Web.Services.Backups;

namespace Relaywright.Web.Tests.Support;

internal sealed class ImmediateBackupCoordinator : IBackupCoordinator
{
    public Task<IAsyncDisposable> AcquireSpoolDeletionLockAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IAsyncDisposable>(new Releaser());
    }

    private sealed class Releaser : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
