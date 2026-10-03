using System.Reflection;

namespace ASX11Battery.App;

/// <summary>Build and identity information shown in the UI and in diagnostics.</summary>
public static class AppInfo
{
    public static string Name => "ASX11 Battery";

    public static string Version
    {
        get
        {
            var informational = typeof(AppInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                int plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        }
    }

    /// <summary>Full path of the running executable, used for the autostart Run key.</summary>
    public static string ExecutablePath
    {
        get
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path)) return path;
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            return current.MainModule?.FileName ?? "ASX11Battery.App.exe";
        }
    }
}