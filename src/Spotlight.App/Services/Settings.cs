using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Spotlight.App.Services;

public class AppSettings
{
    /// <summary>Hotkeys that toggle the launcher. All of them are registered (those that are free).</summary>
    public List<string> SearchHotkeys { get; set; } = new() { "Ctrl+Alt+Space" };

    /// <summary>Hotkeys that open the launcher straight in clipboard-history mode.</summary>
    public List<string> ClipboardHotkeys { get; set; } = new() { "Ctrl+Alt+C" };

    /// <summary>Extra folders (e.g. "D:\Projects") to include in the file index besides the user profile.</summary>
    public List<string> ExtraIndexRoots { get; set; } = new();

    /// <summary>Folder names never descended into by the file indexer.</summary>
    public List<string> ExcludedFolders { get; set; } = new()
    {
        "AppData", "node_modules", ".git", ".svn", ".hg", "bin", "obj", "__pycache__", ".venv", "venv",
        ".gradle", ".nuget", ".cache", ".npm", ".vscode", ".idea", "$Recycle.Bin", "System Volume Information",
        "target", "dist", ".next", "Library"
    };

    /// <summary>Folders such as .claude, .config or .cache are tooling noise for most people; off by default.</summary>
    public bool IndexDotFolders { get; set; } = false;

    public bool CheckForUpdates { get; set; } = true;
    public bool RunAtStartup { get; set; } = true;
}

public static class Settings
{
    private static readonly string _path = Path.Combine(AppPaths.DataDir, "settings.json");
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = Load();
    public static string FilePath => _path;

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
                if (loaded != null)
                {
                    // 1.3.0 shipped Ctrl+Space / Alt+Space as defaults; both are claimed by input methods and
                    // PowerToys on many PCs. Move people who never customised them to the new default.
                    if (loaded.SearchHotkeys.SequenceEqual(new[] { "Ctrl+Space", "Alt+Space" }))
                    {
                        loaded.SearchHotkeys = new AppSettings().SearchHotkeys;
                        try { File.WriteAllText(_path, JsonSerializer.Serialize(loaded, _json)); } catch { }
                    }
                    return loaded;
                }
            }
        }
        catch (Exception ex) { AppPaths.Log("Settings load failed: " + ex.Message); }

        var fresh = new AppSettings();
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(_path, JsonSerializer.Serialize(fresh, _json));
        }
        catch { }
        return fresh;
    }

    public static void Reload() => Current = Load();
}
