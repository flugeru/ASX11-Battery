using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using ASX11Battery.Core.Abstractions;
using ASX11Battery.Core.Services;
using ASX11Battery.Tray;

namespace ASX11Battery.App.ViewModels;

/// <summary>Main window view model. Coordinates the battery monitor, settings, and UI state.</summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly BatteryMonitorService _monitor;
    private readonly BatteryNotifier _notifier;
    private readonly Dispatcher _dispatcher;
    private readonly AppSettings _settings;

    private ObservableCollection<DeviceItemViewModel> _devices = new();
    private bool _disposed;

    public MainViewModel(AppSettings settings, Dispatcher dispatcher)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        _monitor = new BatteryMonitorService();
        _notifier = new BatteryNotifier(_settings);

        // Render the known provider entries immediately. This keeps the dashboard
        // informative before the first HID frame arrives and shows unavailable
        // instead of an empty page when the receiver is absent.
        foreach (var snapshot in _monitor.CurrentDevices)
            Devices.Add(new DeviceItemViewModel(snapshot));

        _monitor.DeviceChanged += OnDeviceChanged;
        _notifier.NotificationRaised += OnNotificationRaised;

    }

    // Devices
    public ObservableCollection<DeviceItemViewModel> Devices
    {
        get => _devices;
        private set => SetProperty(ref _devices, value);
    }

    private readonly DeviceItemViewModel _placeholderDevice = new(new DeviceSnapshot(
        "attack-shark-x11", "Attack Shark X11", "Attack Shark", "Attack Shark X11",
        "mouse", ConnectionType.Wireless24Ghz, false, DeviceState.Disconnected,
        null, null, null, "Conecte o receptor USB", null));

    public DeviceItemViewModel PrimaryDevice => Devices.FirstOrDefault() ?? _placeholderDevice;


    public void Start()
    {
        _monitor.Start(new ProviderOptions
        {
            ReadTimeoutMs = 250,
            ReenumerateIntervalMs = 750,
            ConsensusFrames = 1,
        });
    }

    private void OnDeviceChanged(object? sender, DeviceSnapshot snapshot)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _notifier.Evaluate(snapshot);

            var vm = Devices.FirstOrDefault(d => d.Id == snapshot.Id);
            if (vm is null)
            {
                vm = new DeviceItemViewModel(snapshot);
                Devices.Add(vm);
                OnPropertyChanged(nameof(PrimaryDevice));
            }
            else
            {
                vm.Update(snapshot);
            }

            // Widget must always render a stable device surface, including while
            // receiver reconnects. Never remove its only visual data source.

            // Update the tray icon via the shared Bridge
            Notifications.Refresh(Devices.Select(d => (d.Name, d.BatteryPercent, d.Charging)).ToList());
        });
    }

    private void OnNotificationRaised(object? sender, NotificationRequest req)
    {
        _dispatcher.BeginInvoke(() =>
        {
            Notifications.OnNotification(req);
        });
    }

    public AppSettings Settings => _settings;

    public void ApplySettings(AppSettings newSettings)
    {
        _settings.DetectCharging = newSettings.DetectCharging;
        _settings.BatteryNotificationsEnabled = newSettings.BatteryNotificationsEnabled;
        _settings.LowBatteryThreshold = newSettings.LowBatteryThreshold;
        _settings.NotifyLowBattery = newSettings.NotifyLowBattery;
        _settings.NotifyCharging = newSettings.NotifyCharging;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitor.DeviceChanged -= OnDeviceChanged;
        _notifier.NotificationRaised -= OnNotificationRaised;

        _monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}