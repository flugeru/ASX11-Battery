using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ASX11Battery.Tray;
using ASX11Battery.App.Services;
using ASX11Battery.App.ViewModels;
using ASX11Battery.Core.Services;

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

    /// <summary>
    /// Set by <see cref="CloseForExit"/> so the real close is not mistaken for
    /// the user closing the window to hide it.
    /// </summary>
    private bool _exiting;

    /// <summary>Raised when the user picks "Sair" from inside the window.</summary>
    public event EventHandler? ExitRequested;

    public MainWindow(TrayService tray)
    {
        InitializeComponent();

        _tray = tray;

        var settings = SettingsStore.Load();
        _viewModel = new MainViewModel(settings, Dispatcher);
        DataContext = _viewModel;

        // The window's own "Sair" button is bound to the view model's command;
        // re-raise it as a window event so App has one thing to listen to.
        _viewModel.ExitRequested += OnViewModelExitRequested;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>Switches the view model to the settings page. Used by the tray menu.</summary>
    public void ShowSettings() => _viewModel.ShowSettings();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NavDashboard.IsChecked = true;
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

        if (!_viewModel.CloseToTray)
        {
            e.Cancel = true;
            ExitRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        e.Cancel = true;
        Hide();
        _tray.Notify(
            AppInfo.Name + " continua na bandeja",
            "O app segue monitorando as baterias. Clique com o botão direito no ícone para sair.",
            ASX11Battery.Core.Services.NotificationKind.Charging);
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
        _viewModel.ExitRequested -= OnViewModelExitRequested;
        _viewModel.Dispose();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private void OnViewModelExitRequested(object? sender, EventArgs e) =>
        ExitRequested?.Invoke(this, e);

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }
    }

    /// <summary>
    /// The caption's close button goes through <see cref="Window.Close"/> on
    /// purpose, so it raises <see cref="Closing"/> and lands on the same
    /// close-to-tray decision as the X on a bordered window.
    /// </summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}