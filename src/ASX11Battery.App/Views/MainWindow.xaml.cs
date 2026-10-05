using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ASX11Battery.App.Services;
using ASX11Battery.App.ViewModels;
using ASX11Battery.Core.Services;
using ASX11Battery.Tray;

namespace ASX11Battery.App.Views;

/// <summary>
/// The application window. Owns the view model and decides what closing the
/// window means; the process-wide exit sequence belongs to
/// <see cref="ApplicationLifecycle"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TrayService _tray;
    private bool _started;

    /// <summary>Set during application shutdown to allow final window close.</summary>
    private bool _exiting;

    public MainWindow(TrayService tray)
    {
        InitializeComponent();
        _tray = tray ?? throw new ArgumentNullException(nameof(tray));

        var settings = SettingsStore.Load();
        _viewModel = new MainViewModel(settings, Dispatcher);
        DataContext = _viewModel;

        Closing += OnClosing;
    }

    public void StartMonitoring()
    {
        if (_started) return;
        _started = true;
        _viewModel.Start();
    }

    public void ShowWindow()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting) return;

        e.Cancel = true;
        Hide();
        _tray.Notify(
            "ASX11 Battery continua na bandeja",
            "Monitoramento ativo. Clique no ícone da bandeja para reabrir ou sair.",
            NotificationKind.Charging);
    }

    /// <summary>
    /// Closes the window for real, as part of the exit sequence. Bypasses
    /// close-to-tray and never cancels.
    /// </summary>
    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    public System.Threading.Tasks.Task DisposeAsync()
    {
        _viewModel.Dispose();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is DependencyObject source &&
            FindVisualParent<Button>(source) is not null)
            return;

        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }

        return null;
    }



    /// <summary>Closes the widget through the normal application exit path.</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settings = _viewModel.Settings;
        var win = new SettingsWindow(settings) { Owner = this };
        win.SettingsChanged += (s, ev) =>
        {
            SettingsStore.Save(settings);
            _viewModel.ApplySettings(settings);
        };
        win.ShowDialog();
    }
}