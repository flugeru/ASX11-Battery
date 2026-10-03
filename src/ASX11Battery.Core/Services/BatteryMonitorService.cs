using System.Collections.Concurrent;
using ASX11Battery.Core.Abstractions;
using ASX11Battery.Core.Diagnostics;
using ASX11Battery.Core.Providers;

namespace ASX11Battery.Core.Services;

/// <summary>
/// Runs one watch loop per provider and republishes results as plain events.
/// This is the only object the UI layer talks to; it knows nothing about WPF
/// and raises its events from thread-pool threads.
/// </summary>
public sealed class BatteryMonitorService : IAsyncDisposable
{
    private readonly IReadOnlyList<IBatteryProvider> _providers;
    private readonly ConcurrentDictionary<string, DeviceSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<string, ProviderDiagnostics> _diagnostics = new();
    private readonly CancellationTokenSource _shutdown = new();

    private readonly object _optionsGate = new();
    private ProviderOptions _options = new();
    private CancellationTokenSource? _generation;
    private readonly List<Task> _workers = new();
    private int _disposed;

    public BatteryMonitorService(IEnumerable<IBatteryProvider>? providers = null)
    {
        _providers = providers?.ToList() ?? ProviderRegistry.CreateDefault();
    }

    public IReadOnlyList<IBatteryProvider> Providers => _providers;

    public event EventHandler<DeviceSnapshot>? DeviceChanged;
    public event EventHandler<ProviderDiagnostics>? DiagnosticsChanged;

    public IReadOnlyList<DeviceSnapshot> CurrentDevices =>
        _providers.Select(p => _snapshots.GetValueOrDefault(p.Id) ?? Disconnected(p)).ToList();

    private static DeviceSnapshot Disconnected(IBatteryProvider p) => new(
        p.Id, p.DisplayName, p.Manufacturer, p.DisplayName, "mouse",
        p.DefaultConnection, false, DeviceState.Disconnected, null, null, null,
        "Desconectado", null);

    public void Start(ProviderOptions options)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        lock (_optionsGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            _options = options;
            CancelGeneration();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _generation = cts;

            foreach (var provider in _providers)
            {
                _workers.Add(Task.Run(() => RunProviderAsync(provider, _options, cts.Token), cts.Token));
            }
        }
    }

    public void Restart(ProviderOptions options)
    {
        lock (_optionsGate) _options = options;
        Start(options);
    }

    private void CancelGeneration()
    {
        var old = _generation;
        _generation = null;
        if (old is null) return;
        try { old.Cancel(); } catch { }
        old.Dispose();
    }

    private async Task RunProviderAsync(IBatteryProvider provider, ProviderOptions options, CancellationToken ct)
    {
        await foreach (var snapshot in provider.WatchAsync(options, ct).ConfigureAwait(false))
        {
            _snapshots[snapshot.Id] = snapshot;
            DeviceChanged?.Invoke(this, snapshot);

            if (provider.Diagnostics is not null)
            {
                _diagnostics[provider.Id] = provider.Diagnostics;
                DiagnosticsChanged?.Invoke(this, provider.Diagnostics);
            }
        }
    }

    public ProviderDiagnostics? GetDiagnostics(string providerId)
    {
        if (_diagnostics.TryGetValue(providerId, out var fault)) return fault;
        return _providers.FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase))?.Diagnostics;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try { _shutdown.Cancel(); } catch { }

        lock (_optionsGate) CancelGeneration();

        try
        {
            await Task.WhenAll(_workers).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch
        {
            // A stuck HID read is cleaned up when its handle is disposed; nothing to do.
        }

        _shutdown.Dispose();
    }
}