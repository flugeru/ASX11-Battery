using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ASX11Battery.App.Services;

/// <summary>
/// The single owner of the application's exit sequence.
/// </summary>
public sealed class ApplicationLifecycle
{
    private readonly Func<Task> _releaseResources;
    private readonly Action _closeWindow;
    private readonly Action _shutdown;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _exiting;

    public ApplicationLifecycle(Func<Task> releaseResources, Action closeWindow, Action shutdown)
    {
        _releaseResources = releaseResources ?? throw new ArgumentNullException(nameof(releaseResources));
        _closeWindow = closeWindow ?? throw new ArgumentNullException(nameof(closeWindow));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
    }

    public Task ExitAsync()
    {
        if (Interlocked.Exchange(ref _exiting, 1) != 0)
            return Task.CompletedTask;

        return RunExitAsync();
    }

    private async Task RunExitAsync()
    {
        await _gate.WaitAsync();

        try
        {
            try
            {
                await _releaseResources();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"shutdown: disposing resources threw {ex.GetType().Name}: {ex.Message}");
            }

            _closeWindow();

            _shutdown();
        }
        finally
        {
            _gate.Release();
        }
    }
}