using System;
using System.IO;

namespace Spotlight.App.Services;

/// <summary>
/// Central place for on-disk locations. Everything the user owns (settings, clipboard history, icon cache,
/// WebView2 profile) lives under <c>data\</c> so an update can wipe the program files without touching it.
/// </summary>
public static class AppPaths
{
    // SPOTLIGHT_HOME lets tests and portable setups relocate everything; normally it is %LOCALAPPDATA%\Spotlight.
    public static readonly string InstallDir = Environment.GetEnvironmentVariable("SPOTLIGHT_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spotlight");

    public static readonly string DataDir = Path.Combine(InstallDir, "data");
    public static readonly string IconDir = Path.Combine(DataDir, "icons");
    public static readonly string WebViewDir = Path.Combine(DataDir, "WebView2");
    public static readonly string LogFile = Path.Combine(DataDir, "spotlight.log");

    static AppPaths()
    {
        try { Directory.CreateDirectory(IconDir); } catch { }
    }

    public static void Log(string message)
    {
        try
        {
            var info = new FileInfo(LogFile);
            if (info.Exists && info.Length > 512 * 1024) info.Delete();
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
