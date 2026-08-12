using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Updates;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class UpdateCheckWorkerTests
{
    [Fact]
    public async Task TransientFailureDoesNotStopWorker()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var service = new FailOnceUpdateCheckService();
        var worker = new UpdateCheckWorker(
            service,
            Microsoft.Extensions.Options.Options.Create(new UpdateCheckOptions
            {
                Enabled = true,
                StartupDelaySeconds = 0,
                IntervalHours = 1
            }),
            NullLogger<UpdateCheckWorker>.Instance,
            new ImmediateTimerTimeProvider());

        await worker.StartAsync(CancellationToken.None);
        await service.SecondCallObserved.Task.WaitAsync(timeout.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(service.CallCount >= 2);
    }

    private sealed class FailOnceUpdateCheckService : IUpdateCheckService
    {
        public int CallCount { get; private set; }

        public TaskCompletionSource SecondCallObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<UpdateCheckStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateCheckStatus.Initial());
        }

        public async Task<UpdateCheckStatus> RefreshAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                throw new HttpRequestException("Simulated transient failure.");
            }

            SecondCallObserved.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return UpdateCheckStatus.Initial();
        }
    }

    private sealed class ImmediateTimerTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            return new ImmediateTimer(callback, state, dueTime);
        }
    }

    private sealed class ImmediateTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private int _disposed;

        public ImmediateTimer(TimerCallback callback, object? state, TimeSpan dueTime)
        {
            _callback = callback;
            _state = state;
            QueueIfEnabled(dueTime);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            QueueIfEnabled(dueTime);
            return Volatile.Read(ref _disposed) == 0;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private void QueueIfEnabled(TimeSpan dueTime)
        {
            if (dueTime == Timeout.InfiniteTimeSpan || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    _callback(_state);
                }
            });
        }
    }
}
