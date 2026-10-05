using System;
using System.Collections.Generic;
using System.IO;

namespace ASX11Battery.Core.Diagnostics;

public static class Logger
{
    private const long MaxBytes = 1_048_576;
    private const int MaxArchives = 3;
    private static readonly string LogPath = ResolveLogPath();
    private static readonly object LogLock = new();
    private static readonly Dictionary<string, (string Message, DateTime Timestamp)> LastByKey = new(StringComparer.Ordinal);

    private static string ResolveLogPath()
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string? projectRoot = FindProjectRoot(baseDirectory);
        return Path.Combine(projectRoot ?? baseDirectory, "logs", "asx11-battery.log");
    }

    private static string? FindProjectRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ASX11Battery.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    static Logger()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!); } catch { }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Debug(string key, string message, TimeSpan interval) => Event(key, message, interval);

    public static void ClearEventCache()
    {
        lock (LogLock) LastByKey.Clear();
    }

    public static void Event(string key, string message, TimeSpan? minimumInterval = null)
    {
        lock (LogLock)
        {
            var now = DateTime.UtcNow;
            if (LastByKey.TryGetValue(key, out var last))
            {
                bool same = string.Equals(last.Message, message, StringComparison.Ordinal);
                bool throttled = minimumInterval.HasValue && now - last.Timestamp < minimumInterval.Value;
                if (same || throttled) return;
            }
            LastByKey[key] = (message, now);
            WriteLocked("INFO", message);
        }
    }

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}");

    private static void Write(string level, string message)
    {
        lock (LogLock) WriteLocked(level, message);
    }

    private static void WriteLocked(string level, string message)
    {
        try
        {
            RotateIfNeeded();
            File.AppendAllText(LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(LogPath);
        if (!file.Exists || file.Length < MaxBytes) return;

        for (int i = MaxArchives; i >= 1; i--)
        {
            string current = $"{LogPath}.{i}";
            string next = $"{LogPath}.{i + 1}";
            if (!File.Exists(current)) continue;
            if (i == MaxArchives) File.Delete(current);
            else File.Move(current, next, overwrite: true);
        }

        File.Move(LogPath, $"{LogPath}.1", overwrite: true);
    }
}
