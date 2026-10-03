using System.Collections.Concurrent;
using ASX11Battery.App.Services;
using Xunit;

namespace ASX11Battery.Tests;

/// <summary>
/// Locks in the shutdown behaviour that a tray application has to get right: the
/// exit path must await disposal rather than block the dispatcher, and must be
/// safe to run more than once.
/// </summary>
public sealed class ApplicationLifecycleTests
{
    [Fact]
    public async Task ExitReleasesResourcesThenClosesTheWindowThenShutsDown()
    {
        var log = new List<string>();

        var lifecycle = new ApplicationLifecycle(
            releaseResources: () =>
            {
                log.Add("release");
                return Task.CompletedTask;
            },
            closeWindow: () => log.Add("close"),
            shutdown: () => log.Add("shutdown"));

        await lifecycle.ExitAsync();

        // The order is the contract. Disposing the monitor last would let a
        // provider raise a device event into a tray icon that no longer exists.
        Assert.Equal(new[] { "release", "close", "shutdown" }, log);
    }

    [Fact]
    public async Task RepeatedExitRequestsStillOnlyRunTheSequenceOnce()
    {
        int releases = 0;
        int closes = 0;
        int shutdowns = 0;

        var lifecycle = new ApplicationLifecycle(
            releaseResources: () =>
            {
                Interlocked.Increment(ref releases);
                return Task.CompletedTask;
            },
            closeWindow: () => Interlocked.Increment(ref closes),
            shutdown: () => Interlocked.Increment(ref shutdowns));

        // The tray menu, the window's close button and a logoff can all arrive
        // together. Disposing a tray icon twice throws, and a throw part-way
        // through the sequence strands the process.
        await Task.WhenAll(
            lifecycle.ExitAsync(),
            lifecycle.ExitAsync(),
            lifecycle.ExitAsync());

        Assert.Equal(1, releases);
        Assert.Equal(1, closes);
        Assert.Equal(1, shutdowns);
    }

    [Fact]
    public async Task ExitStillShutsDownWhenDisposalThrows()
    {
        var log = new List<string>();

        var lifecycle = new ApplicationLifecycle(
            releaseResources: () => throw new InvalidOperationException("device handle stuck"),
            closeWindow: () => log.Add("close"),
            shutdown: () => log.Add("shutdown"));

        await lifecycle.ExitAsync();

        // A provider fault on the way down must not abort the exit before the
        // dispatcher stops; that is precisely how a process ends up invisible but
        // still running.
        Assert.Equal(new[] { "close", "shutdown" }, log);
    }

    [Fact]
    public async Task ExitDoesNotDeadlockWhenDisposalYieldsToTheThreadPool()
    {
        var shutdown = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var lifecycle = new ApplicationLifecycle(
            releaseResources: () => release.Task,
            closeWindow: () => { },
            shutdown: () => shutdown.TrySetResult());

        Task exit = lifecycle.ExitAsync();

        // The disposal is still in flight. Completing it from another thread is
        // what the real one does, because the monitor's workers are thread-pool
        // tasks. If the exit path blocked the dispatcher instead of awaiting, this
        // continuation could never run and the test would hang here.
        release.SetResult();
        await exit.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(shutdown.Task.IsCompleted, "Shutdown must run once disposal finishes.");
    }

    [Fact]
    public async Task ExitAfterCompletionIsANoOp()
    {
        int shutdowns = 0;

        var lifecycle = new ApplicationLifecycle(
            releaseResources: () => Task.CompletedTask,
            closeWindow: () => { },
            shutdown: () => Interlocked.Increment(ref shutdowns));

        await lifecycle.ExitAsync();
        await lifecycle.ExitAsync();

        Assert.True(lifecycle.IsExiting);
        Assert.Equal(1, shutdowns);
    }

    [Fact]
    public async Task ExitFinishesOnTheThreadThatAskedForIt()
    {
        // The disposal genuinely suspends, and the two steps after it are
        // UI-thread operations: WPF's Window.Close and Application.Shutdown both
        // throw on any other thread.
        //
        // A single-threaded pump stands in for the dispatcher, because without a
        // SynchronizationContext there is nothing for an await to come back to.
        using var pump = new SingleThreadedContext();
        SynchronizationContext.SetSynchronizationContext(pump);

        try
        {
            int? closeThread = null;
            int? shutdownThread = null;

            var lifecycle = new ApplicationLifecycle(
                releaseResources: () => Task.Delay(20),
                closeWindow: () => closeThread = Environment.CurrentManagedThreadId,
                shutdown: () => shutdownThread = Environment.CurrentManagedThreadId);

            await lifecycle.ExitAsync();

            Assert.Equal(pump.ThreadId, closeThread);
            Assert.Equal(pump.ThreadId, shutdownThread);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    /// <summary>
    /// A one-thread <see cref="SynchronizationContext"/> that behaves the way a
    /// WPF dispatcher does: continuations come back to a single known thread, and
    /// which thread that is has to be asserted deliberately.
    /// </summary>
    private sealed class SingleThreadedContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _pump;

        public SingleThreadedContext()
        {
            _pump = new Thread(Pump)
            {
                IsBackground = true,
                Name = "dispatcher-stand-in",
            };

            _pump.Start();
        }

        public int ThreadId { get; private set; }

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        private void Pump()
        {
            ThreadId = Environment.CurrentManagedThreadId;

            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                SetSynchronizationContext(this);
                callback(state);
            }
        }

        public void Dispose() => _queue.CompleteAdding();
    }
}
