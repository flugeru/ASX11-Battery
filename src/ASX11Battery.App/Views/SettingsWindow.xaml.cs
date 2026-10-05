using System;
using System.Windows;
using System.Windows.Input;
using ASX11Battery.Core.Services;

namespace ASX11Battery.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _loading;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _loading = true;
        DetectChargingCheckBox.IsChecked = _settings.DetectCharging;
        BatteryNotificationsCheckBox.IsChecked = _settings.BatteryNotificationsEnabled;
        ThresholdTextBox.Text = Math.Clamp(_settings.LowBatteryThreshold, 1, 99).ToString();
        _loading = false;
        UpdateThresholdState();
    }

    public event EventHandler? SettingsChanged;

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.DetectCharging = DetectChargingCheckBox.IsChecked == true;
        _settings.BatteryNotificationsEnabled = BatteryNotificationsCheckBox.IsChecked == true;
        UpdateThresholdState();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnThresholdChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading) return;
        if (int.TryParse(ThresholdTextBox.Text, out int value))
            _settings.LowBatteryThreshold = Math.Clamp(value, 1, 99);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateThresholdState() => ThresholdTextBox.IsEnabled = BatteryNotificationsCheckBox.IsChecked == true;
}
