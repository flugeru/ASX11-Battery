using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Windows.Interop;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingIcon = System.Drawing.Icon;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using ASX11Battery.Tray;
using ASX11Battery.App.Views;
using ASX11Battery.App.ViewModels;
using ASX11Battery.App.Services;
using ASX11Battery.Core.Services;

namespace ASX11Battery.App;

/// <summary>
/// Application entry point. Owns the single-instance guard, creates the window,
/// and owns the exit sequence.
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private TrayService? _tray;
    private MainWindow? _window;
    private ApplicationLifecycle? _lifecycle;
    private readonly CancellationTokenSource _shutdownCts = new();

    private const string WakeMessageName = "ASX11Battery.Wake.";
    private static uint _wakeMessageId = 0;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(int hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const int HWND_BROADCAST = 0xFFFF;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Register wake message once
        _wakeMessageId = RegisterWindowMessage(WakeMessageName + Environment.UserName);

        // Single instance guard
        _singleInstance = new Mutex(true, @"Local\ASX11Battery.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            _singleInstance.Dispose();
            _singleInstance = null;

            // Ask the running instance to come forward
            if (_wakeMessageId != 0)
            {
                PostMessageW(HWND_BROADCAST, _wakeMessageId, IntPtr.Zero, IntPtr.Zero);
            }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Create tray first so it's ready when the window shows
        _tray = new TrayService();
        Notifications.Attach(_tray);

        // Settings. Theme is intentionally fixed to Dark.
        var settings = SettingsStore.Load();
        ApplyTheme();

        // Window
        _window = new MainWindow(_tray);
        _window.ExitRequested += (_, _) => _lifecycle?.ExitAsync();
        MainWindow = _window;

        // Load supplied multi-resolution ICO as WPF ImageSource. Pack URI keeps
        // title bar, Alt+Tab, and taskbar on same asset as executable and tray.
        _window.Icon = LoadWindowIcon();

        // Lifecycle owns the exit sequence
        _lifecycle = new ApplicationLifecycle(
            releaseResources: ReleaseResourcesAsync,
            closeWindow: () => _window?.CloseForExit(),
            shutdown: Shutdown);

        _tray.OpenRequested += (_, _) => Dispatcher.BeginInvoke(() => _window?.ShowWindow());
        _tray.SettingsRequested += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _window?.ShowWindow();
            _window?.ShowSettings();
        });
        _tray.ExitRequested += (_, _) => _lifecycle.ExitAsync();

        // Show the window initially
        _window.Show();

        // Create a hidden window to receive wake messages
        var wakeParams = new HwndSourceParameters("ASX11Battery.WakeReceiver")
        {
            PositionX = 0,
            PositionY = 0,
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = 0x80, // WS_EX_TOOLWINDOW
            ParentWindow = IntPtr.Zero,
            UsesPerPixelTransparency = false,
        };

        var wakeSource = new HwndSource(wakeParams);
        wakeSource.AddHook((hwnd, msg, wParam, lParam, ref handled) =>
        {
            if (msg == _wakeMessageId)
            {
                _window?.ShowWindow();
                handled = true;
            }
            return IntPtr.Zero;
        });

        // Start monitoring through the window, which owns the view model.
        _window.StartMonitoring();
    }

    private static ImageSource LoadWindowIcon()
    {
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "ASX11Battery.ico");
            if (!System.IO.File.Exists(path))
                path = System.IO.Path.Combine(AppContext.BaseDirectory, "ASX11Battery.ico");

            if (System.IO.File.Exists(path))
            {
                using var icon = new DrawingIcon(path);
                using var bmp = icon.ToBitmap();
                using var ms = new MemoryStream();
                bmp.Save(ms, DrawingImageFormat.Png);
                ms.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = ms;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
        catch
        {
        }
        return CreateWindowIcon();
    }

    private async Task ReleaseResourcesAsync()
    {
        if (_window is not null)
            await _window.DisposeAsync();

        if (_tray is not null)
        {
            Notifications.Detach(_tray);
            _tray.Dispose();
            _tray = null;
        }

        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Ocorreu um erro inesperado:\n\n{e.Exception.Message}\n\nA aplicação continuará em execução.",
            AppInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _tray = null;

        _singleInstance?.Dispose();
        _singleInstance = null;

        base.OnExit(e);
    }

    private void ApplyTheme()
    {
        var dict = Resources.MergedDictionaries;
        if (dict.Count >= 2)
            dict[1] = new ResourceDictionary { Source = new Uri("Themes/Dark.xaml", UriKind.Relative) };
    }
}