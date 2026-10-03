using System;
using System.IO;

namespace ASX11Battery.Core.Diagnostics;

public static class Logger
{
    private static readonly string LogPath = ResolveLogPath();
    private static readonly object LogLock = new();

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

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}");
    }

    private static void Write(string level, string message)
    {
        lock (LogLock)
        {
            try
            {
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
