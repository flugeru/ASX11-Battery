using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ASX11Battery.Tray;
using ASX11Battery.App.ViewModels;
using ASX11Battery.Core.Abstractions;
using ASX11Battery.Core.Diagnostics;
using ASX11Battery.Core.Services;

namespace ASX11Battery.App.ViewModels;

/// <summary>Main window view model. Coordinates the battery monitor, settings, and UI state.</summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly BatteryMonitorService _monitor;
    private readonly BatteryNotifier _notifier;
    private readonly Dispatcher _dispatcher;
    private readonly AppSettings _settings;

    private bool _isDashboardSelected = true;
    private bool _isSettingsSelected;
    private bool _isDiagnosticsSelected;
    private bool _isAboutSelected;
    private string _pageTitle = "Bateria";
    private string _statusHeadline = "Iniciando...";
    private ObservableCollection<DeviceItemViewModel> _devices = new();
    private string? _diagnosticText;
    private string? _copyFeedback;
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

        UpdatePageTitle();
        RefreshDiagnosticText();

        _monitor.DeviceChanged += OnDeviceChanged;
        _monitor.DiagnosticsChanged += OnDiagnosticsChanged;
        _notifier.NotificationRaised += OnNotificationRaised;

        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        ShowDashboardCommand = new RelayCommand(() => IsDashboardSelected = true);
        ShowSettingsCommand = new RelayCommand(() => IsSettingsSelected = true);
        ShowDiagnosticsCommand = new RelayCommand(() => IsDiagnosticsSelected = true);
        ShowAboutCommand = new RelayCommand(() => IsAboutSelected = true);
        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnostics);

    }

    // Commands
    public RelayCommand ExitCommand { get; }
    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand ShowDiagnosticsCommand { get; }
    public RelayCommand ShowAboutCommand { get; }
    public RelayCommand CopyDiagnosticsCommand { get; }

    // Navigation
    public bool IsDashboardSelected
    {
        get => _isDashboardSelected;
        set
        {
            if (!SetProperty(ref _isDashboardSelected, value) || !value) return;
            _isSettingsSelected = _isDiagnosticsSelected = _isAboutSelected = false;
            NotifyNavigationChanged();
        }
    }
    public bool IsSettingsSelected
    {
        get => _isSettingsSelected;
        set
        {
            if (!SetProperty(ref _isSettingsSelected, value) || !value) return;
            _isDashboardSelected = _isDiagnosticsSelected = _isAboutSelected = false;
            NotifyNavigationChanged();
            RefreshDiagnosticText();
        }
    }
    public bool IsDiagnosticsSelected
    {
        get => _isDiagnosticsSelected;
        set
        {
            if (!SetProperty(ref _isDiagnosticsSelected, value) || !value) return;
            _isDashboardSelected = _isSettingsSelected = _isAboutSelected = false;
            NotifyNavigationChanged();
            RefreshDiagnosticText();
        }
    }
    public bool IsAboutSelected
    {
        get => _isAboutSelected;
        set
        {
            if (!SetProperty(ref _isAboutSelected, value) || !value) return;
            _isDashboardSelected = _isSettingsSelected = _isDiagnosticsSelected = false;
            NotifyNavigationChanged();
        }
    }

    // Page visibility for XAML binding
    public Visibility DashboardVisibility => IsDashboardSelected ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsVisibility => IsSettingsSelected ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DiagnosticsVisibility => IsDiagnosticsSelected ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AboutVisibility => IsAboutSelected ? Visibility.Visible : Visibility.Collapsed;

    private void NotifyNavigationChanged()
    {
        OnPropertyChanged(nameof(IsDashboardSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
        OnPropertyChanged(nameof(IsDiagnosticsSelected));
        OnPropertyChanged(nameof(IsAboutSelected));
        OnPropertyChanged(nameof(DashboardVisibility));
        OnPropertyChanged(nameof(SettingsVisibility));
        OnPropertyChanged(nameof(DiagnosticsVisibility));
        OnPropertyChanged(nameof(AboutVisibility));
        UpdatePageTitle();
    }

    // Header
    public string PageTitle
    {
        get => _pageTitle;
        private set => SetProperty(ref _pageTitle, value);
    }

    public string StatusHeadline
    {
        get => _statusHeadline;
        private set => SetProperty(ref _statusHeadline, value);
    }

    public string VersionText => $"v{AppInfo.Version}";

    // Devices
    public ObservableCollection<DeviceItemViewModel> Devices
    {
        get => _devices;
        private set => SetProperty(ref _devices, value);
    }

    // Settings (bound to UI)
    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set { if (_settings.StartWithWindows != value) { _settings.StartWithWindows = value; SettingsStore.Save(_settings); OnPropertyChanged(); } }
    }

    public bool StartMinimized
    {
        get => _settings.StartMinimized;
        set { if (_settings.StartMinimized != value) { _settings.StartMinimized = value; SettingsStore.Save(_settings); OnPropertyChanged(); } }
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set { if (_settings.CloseToTray != value) { _settings.CloseToTray = value; SettingsStore.Save(_settings); OnPropertyChanged(); } }
    }

    public int LowBatteryThreshold
    {
        get => _settings.LowBatteryThreshold;
        set { if (_settings.LowBatteryThreshold != value) { _settings.LowBatteryThreshold = value; SettingsStore.Save(_settings); OnPropertyChanged(); } }
    }

    public bool NotifyLowBattery
    {
        get => _settings.NotifyLowBattery;
        set { if (_settings.NotifyLowBattery != value) { _settings.NotifyLowBattery = value; SettingsStore.Save(_settings); OnPropertyChanged(); } }
    }

    public bool NotifyCharging
    {
        get => _settings.NotifyCharging;
        set { if (_settings.NotifyCharging != value) { _settings.NotifyCharging = value; SettingsStore.Save(_settings); OnPropertyChanged(); } }
    }

    public bool ShowDisconnected
    {
        get => _settings.ShowDisconnected;
        set { if (_settings.ShowDisconnected != value) { _settings.ShowDisconnected = value; SettingsStore.Save(_settings); OnPropertyChanged(); } }
    }

    // Diagnostics
    public string? DiagnosticText
    {
        get => _diagnosticText;
        private set => SetProperty(ref _diagnosticText, value);
    }

    public string? CopyFeedback
    {
        get => _copyFeedback;
        private set => SetProperty(ref _copyFeedback, value);
    }

    // Events
    public event EventHandler? ExitRequested;

    public void Start()
    {
        _monitor.Start(new ProviderOptions
        {
            ReadTimeoutMs = 700,
            ReenumerateIntervalMs = 3000,
        });
    }

    public void ShowSettings() => IsSettingsSelected = true;

    private void UpdatePageTitle()
    {
        PageTitle = IsDashboardSelected ? "Bateria"
            : IsSettingsSelected ? "Configurações"
            : IsDiagnosticsSelected ? "Diagnóstico"
            : "Sobre";

        StatusHeadline = IsDashboardSelected ? "Bateria em tempo real"
            : IsSettingsSelected ? "Preferências do aplicativo"
            : IsDiagnosticsSelected ? "Informações técnicas"
            : "Sobre o aplicativo";
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
            }
            else
            {
                vm.Update(snapshot);
            }

            if (!_settings.ShowDisconnected && snapshot.State == DeviceState.Disconnected)
                Devices.Remove(vm);

            // Update the tray icon via the shared Bridge
            Notifications.Refresh(Devices.Select(d => (d.Name, d.BatteryPercent, d.Charging)).ToList());

            StatusHeadline = Devices.Any(d => d.State == DeviceState.Connected)
                ? $"Bateria em {Devices.Where(d => d.State == DeviceState.Connected).Count()} dispositivo(s)"
                : "Nenhum dispositivo conectado";
        });
    }

    private void OnDiagnosticsChanged(object? sender, ProviderDiagnostics diag)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (IsDiagnosticsSelected) RefreshDiagnosticText();
        });
    }

    private void OnNotificationRaised(object? sender, NotificationRequest req)
    {
        _dispatcher.BeginInvoke(() =>
        {
            Notifications.OnNotification(req);
        });
    }

    private void CopyDiagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ASX11 Battery v{AppInfo.Version}");
        sb.AppendLine($"Timestamp: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        foreach (var provider in _monitor.Providers)
        {
            var diag = _monitor.GetDiagnostics(provider.Id);
            if (diag is null) continue;

            sb.AppendLine($"=== {diag.ProviderName} ({diag.ProviderId}) ===");
            sb.AppendLine($"Status: {diag.Status}");
            sb.AppendLine($"Protocol: {diag.ProtocolSummary}");
            sb.AppendLine($"Device Present: {diag.DevicePresent}");
            sb.AppendLine($"Captured: {diag.CapturedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            foreach (var iface in diag.Interfaces)
            {
                sb.AppendLine($"  Interface: {iface.Path}");
                sb.AppendLine($"    UsagePage: 0x{iface.UsagePage:X4}, Usage: 0x{iface.Usage:X4}");
                sb.AppendLine($"    Openable: {iface.IsOpenable}");
                if (!string.IsNullOrEmpty(iface.Note)) sb.AppendLine($"    Note: {iface.Note}");
            }
            sb.AppendLine();

            foreach (var frame in diag.RecentFrames.TakeLast(10))
            {
                var hex = BitConverter.ToString(frame.Data).Replace("-", " ");
                sb.AppendLine($"  [{frame.Timestamp:HH:mm:ss.fff}] {hex} -> {frame.Verdict}");
            }
            sb.AppendLine();
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            CopyFeedback = "Copiado para a área de transferência!";
        }
        catch (Exception ex)
        {
            CopyFeedback = $"Falha ao copiar: {ex.Message}";
        }
    }

    public void RefreshDiagnosticText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var provider in _monitor.Providers)
        {
            var diag = _monitor.GetDiagnostics(provider.Id);
            if (diag is null) continue;

            sb.AppendLine($"=== {diag.ProviderName} ===");
            sb.AppendLine($"Status: {diag.Status}");
            sb.AppendLine($"Protocol: {diag.ProtocolSummary}");
            sb.AppendLine();

            foreach (var iface in diag.Interfaces)
            {
                sb.AppendLine($"  {iface.Path}");
                sb.AppendLine($"    UP: 0x{iface.UsagePage:X4} U: 0x{iface.Usage:X4} Openable: {iface.IsOpenable}");
                if (!string.IsNullOrEmpty(iface.Note)) sb.AppendLine($"    {iface.Note}");
            }

            foreach (var frame in diag.RecentFrames.TakeLast(10))
            {
                var hex = BitConverter.ToString(frame.Data).Replace("-", " ");
                sb.AppendLine($"  [{frame.Timestamp:HH:mm:ss.fff}] {hex} -> {frame.Verdict}");
            }
            sb.AppendLine();
        }
        DiagnosticText = sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitor.DeviceChanged -= OnDeviceChanged;
        _monitor.DiagnosticsChanged -= OnDiagnosticsChanged;
        _notifier.NotificationRaised -= OnNotificationRaised;

        _monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}