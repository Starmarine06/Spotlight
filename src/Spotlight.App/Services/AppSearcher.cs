using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Spotlight.App.Services;

public class AppItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    /// <summary>PNG file name inside the icon cache folder (served as https://icons.spotlight.local/{Icon}).</summary>
    public string Icon { get; set; } = string.Empty;
    /// <summary>Executable base name ("winword", "chrome") - lets "word" or "chrome" match apps with fancy names.</summary>
    public string Exe { get; set; } = string.Empty;
    /// <summary>Files an icon can be extracted from, best first (the .lnk itself, then its target).</summary>
    [JsonIgnore] public List<string> IconSources { get; set; } = new();
}

/// <summary>
/// Discovers launchable apps (Start Menu, Desktop, Store/UWP apps, registry App Paths, system tools).
/// The result is persisted so the launcher has apps the instant it starts; a background rescan refreshes it.
/// </summary>
public class AppSearcher
{
    private static readonly string CachePath = Path.Combine(AppPaths.DataDir, "apps.json");

    private readonly object _lock = new();
    private List<AppItem> _apps = new();
    private DateTime _lastScan = DateTime.MinValue;
    private int _scanning;

    public event Action<List<AppItem>>? AppsUpdated;

    public DateTime LastScan => _lastScan;

    public List<AppItem> GetCached()
    {
        lock (_lock)
        {
            if (_apps.Count == 0) LoadFromDisk();
            return _apps;
        }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(CachePath)) return;
            var loaded = JsonSerializer.Deserialize<List<AppItem>>(File.ReadAllText(CachePath));
            if (loaded != null) _apps = loaded;
        }
        catch { }
    }

    /// <summary>Rescans in the background on an STA thread (shell COM objects need one) and raises <see cref="AppsUpdated"/>.</summary>
    public Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1) return Task.CompletedTask;

        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                ScanAndPublish();
            }
            catch (Exception ex)
            {
                AppPaths.Log("App scan failed: " + ex);
            }
            finally
            {
                Interlocked.Exchange(ref _scanning, 0);
                tcs.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Name = "Spotlight app scan",
            Priority = ThreadPriority.BelowNormal
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    /// <summary>
    /// Scans in phases and publishes after each, so apps are searchable within a couple of seconds on the very
    /// first run instead of after the slow shell/icon work has finished.
    /// </summary>
    private void ScanAndPublish()
    {
        var sw = Stopwatch.StartNew();
        var apps = new List<AppItem>();

        dynamic? wsh = null;
        try
        {
            var wshType = Type.GetTypeFromProgID("WScript.Shell");
            if (wshType != null) wsh = Activator.CreateInstance(wshType);
        }
        catch { }

        var userStartMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
        var commonStartMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs");
        var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        // Phase 1: shortcuts and system tools (fast)
        foreach (var dir in new[] { userStartMenu, commonStartMenu, userDesktop, commonDesktop })
            ScanDirectory(dir, apps, wsh);
        AddSystemUtilities(apps);
        Publish(apps, final: false);
        AppPaths.Log($"App scan phase 1: {apps.Count} items, {sw.ElapsedMilliseconds} ms");

        if (wsh != null)
        {
            try { System.Runtime.InteropServices.Marshal.ReleaseComObject(wsh); } catch { }
        }

        // Phase 2: Store/UWP apps and registered executables (slower shell enumeration)
        ScanAppsFolder(apps);
        ScanRegistryAppPaths(apps);
        Publish(apps, final: false);
        AppPaths.Log($"App scan phase 2: {apps.Count} items, {sw.ElapsedMilliseconds} ms");

        // Phase 3: icons that are not cached yet, extracted in parallel
        var unique = Dedupe(apps);
        ExtractMissingIcons(unique);
        Publish(unique, final: true);
        AppPaths.Log($"App scan finished: {unique.Count} apps in {sw.ElapsedMilliseconds} ms");
    }

    private void Publish(List<AppItem> source, bool final)
    {
        var list = final ? source : Dedupe(source);
        if (!final)
        {
            // Partial results must not make previously known apps vanish while the slower phases still run.
            List<AppItem> known;
            lock (_lock) known = _apps;
            var names = new HashSet<string>(list.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
            list = list.Concat(known.Where(k => !names.Contains(k.Name))).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        foreach (var app in list)
        {
            if (string.IsNullOrEmpty(app.Icon))
                app.Icon = app.IconSources.Select(IconExtractor.CachedFile).FirstOrDefault(f => !string.IsNullOrEmpty(f)) ?? string.Empty;
        }

        lock (_lock)
        {
            _apps = list;
            if (final) _lastScan = DateTime.UtcNow;
        }
        if (final)
        {
            try { File.WriteAllText(CachePath, JsonSerializer.Serialize(list)); } catch { }
        }
        AppsUpdated?.Invoke(list);
    }

    private static List<AppItem> Dedupe(List<AppItem> apps)
    {
        // De-duplicate by name, preferring entries that point at a real file.
        var unique = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var key = app.Name.Trim();
            if (key.Length == 0) continue;

            if (!unique.TryGetValue(key, out var existing))
                unique[key] = app;
            else if (!File.Exists(existing.TargetPath) && File.Exists(app.TargetPath))
                unique[key] = app;
        }

        foreach (var app in unique.Values)
        {
            app.Id = IconExtractor.Hash(app.Name + "|" + app.TargetPath + "|" + app.Arguments);
            if (string.IsNullOrEmpty(app.Exe) && !app.TargetPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                app.Exe = Path.GetFileNameWithoutExtension(app.TargetPath);
        }

        return unique.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ExtractMissingIcons(List<AppItem> apps)
    {
        var queue = new System.Collections.Concurrent.ConcurrentQueue<AppItem>(
            apps.Where(a => string.IsNullOrEmpty(a.Icon) && a.IconSources.Count > 0));
        if (queue.IsEmpty) return;

        var workers = Enumerable.Range(0, 4).Select(_ =>
        {
            var t = new Thread(() =>
            {
                while (queue.TryDequeue(out var app))
                {
                    foreach (var source in app.IconSources)
                    {
                        var file = IconExtractor.GetIconFile(source);
                        if (file.Length > 0) { app.Icon = file; break; }
                    }
                }
            })
            { IsBackground = true, Name = "Spotlight icon worker" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return t;
        }).ToList();

        foreach (var t in workers) t.Join();
    }

    private static void ScanDirectory(string dirPath, List<AppItem> apps, dynamic? wsh)
    {
        if (!Directory.Exists(dirPath)) return;

        foreach (var file in EnumerateSafe(dirPath, "*.lnk"))
        {
            try
            {
                var app = ParseLnk(file, wsh);
                if (app != null && !IsJunk(app.Name, app.TargetPath)) apps.Add(app);
            }
            catch { }
        }

        foreach (var file in EnumerateSafe(dirPath, "*.url"))
        {
            try
            {
                var app = ParseUrl(file);
                if (app != null && !IsJunk(app.Name, app.TargetPath)) apps.Add(app);
            }
            catch { }
        }
    }

    private static IEnumerable<string> EnumerateSafe(string dir, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            }).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void ScanAppsFolder(List<AppItem> apps)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic? folder = shell.NameSpace("shell:AppsFolder");
            if (folder == null) return;

            foreach (dynamic item in folder!.Items())
            {
                try
                {
                    string name = item.Name;
                    string path = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                    if (IsJunk(name, path)) continue;

                    string target = path;
                    bool isFile = File.Exists(target) || Directory.Exists(target);
                    if (!isFile && !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                        target = "shell:AppsFolder\\" + path;

                    apps.Add(new AppItem
                    {
                        Name = name,
                        TargetPath = target,
                        IconSources = new List<string> { target }
                    });
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ScanRegistryAppPaths(List<AppItem> apps)
    {
        string[] subKeys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"
        };

        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var subKeyPath in subKeys)
            {
                try
                {
                    using var key = hive.OpenSubKey(subKeyPath);
                    if (key == null) continue;

                    foreach (var subName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var appKey = key.OpenSubKey(subName);
                            var defaultVal = appKey?.GetValue(null)?.ToString();
                            if (string.IsNullOrEmpty(defaultVal)) continue;

                            defaultVal = Environment.ExpandEnvironmentVariables(defaultVal).Trim('"');
                            if (!File.Exists(defaultVal)) continue;

                            var friendlyName = Path.GetFileNameWithoutExtension(subName);
                            if (string.IsNullOrEmpty(friendlyName)) continue;

                            try
                            {
                                var vi = FileVersionInfo.GetVersionInfo(defaultVal);
                                if (!string.IsNullOrWhiteSpace(vi.FileDescription)) friendlyName = vi.FileDescription.Trim();
                            }
                            catch { }

                            if (IsJunk(friendlyName, defaultVal)) continue;

                            apps.Add(new AppItem
                            {
                                Name = friendlyName,
                                TargetPath = defaultVal,
                                IconSources = new List<string> { defaultVal }
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
    }

    private static void AddSystemUtilities(List<AppItem> apps)
    {
        var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var utilities = new (string Name, string Path)[]
        {
            ("Command Prompt", Path.Combine(sys32, "cmd.exe")),
            ("Windows PowerShell", Path.Combine(sys32, @"WindowsPowerShell\v1.0\powershell.exe")),
            ("Task Manager", Path.Combine(sys32, "Taskmgr.exe")),
            ("Control Panel", Path.Combine(sys32, "control.exe")),
            ("Registry Editor", Path.Combine(winDir, "regedit.exe")),
            ("Calculator", Path.Combine(sys32, "calc.exe")),
            ("Paint", Path.Combine(sys32, "mspaint.exe")),
            ("Notepad", Path.Combine(sys32, "notepad.exe")),
            ("File Explorer", Path.Combine(winDir, "explorer.exe")),
            ("Remote Desktop Connection", Path.Combine(sys32, "mstsc.exe")),
            ("Snipping Tool", Path.Combine(sys32, "SnippingTool.exe")),
            ("Disk Cleanup", Path.Combine(sys32, "cleanmgr.exe")),
            ("Resource Monitor", Path.Combine(sys32, "resmon.exe")),
            ("Performance Monitor", Path.Combine(sys32, "perfmon.exe")),
            ("DirectX Diagnostic Tool", Path.Combine(sys32, "dxdiag.exe")),
            ("System Information", Path.Combine(sys32, "msinfo32.exe")),
            ("Device Manager", Path.Combine(sys32, "devmgmt.msc")),
            ("Disk Management", Path.Combine(sys32, "diskmgmt.msc")),
            ("Services", Path.Combine(sys32, "services.msc")),
            ("Computer Management", Path.Combine(sys32, "compmgmt.msc")),
            ("Event Viewer", Path.Combine(sys32, "eventvwr.msc")),
            ("Character Map", Path.Combine(sys32, "charmap.exe")),
            ("Windows Security", "windowsdefender:")
        };

        foreach (var (name, path) in utilities)
        {
            bool isUri = path.EndsWith(":", StringComparison.Ordinal);
            if (!isUri && !File.Exists(path)) continue;
            apps.Add(new AppItem
            {
                Name = name,
                TargetPath = path,
                IconSources = isUri ? new List<string>() : new List<string> { path }
            });
        }
    }

    private static bool IsJunk(string name, string targetPath)
    {
        var n = name.ToLowerInvariant();
        var p = targetPath.ToLowerInvariant();

        if (n.Contains("uninstall") || n.Contains("unins000") || n.StartsWith("remove ") ||
            n.EndsWith(" uninstaller") || n.Contains("documentation") || n.Contains("release notes") ||
            n.Contains("readme") || n.Contains("help manual") || n.Contains("license") ||
            n.Contains("terms of service") || n == "website" || n == "homepage")
            return true;

        return p.Contains("unins000.exe") || p.Contains("uninstall.exe") || p.EndsWith(".txt") ||
               p.EndsWith(".chm") || p.EndsWith(".html") || p.EndsWith(".htm") || p.EndsWith(".pdf");
    }

    private static AppItem? ParseLnk(string shortcutPath, dynamic? wsh)
    {
        if (wsh == null) return null;

        dynamic shortcut = wsh.CreateShortcut(shortcutPath);
        string targetPath = shortcut.TargetPath;
        string arguments = shortcut.Arguments;
        if (string.IsNullOrEmpty(targetPath)) return null;

        targetPath = Environment.ExpandEnvironmentVariables(targetPath);
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath)) return null;

        return new AppItem
        {
            Name = Path.GetFileNameWithoutExtension(shortcutPath),
            TargetPath = targetPath,
            Arguments = arguments ?? string.Empty,
            // The .lnk itself yields the icon the user sees in the Start Menu (custom icon locations included).
            IconSources = new List<string> { shortcutPath, targetPath }
        };
    }

    private static AppItem? ParseUrl(string shortcutPath)
    {
        string url = string.Empty, iconFile = string.Empty;
        foreach (var line in File.ReadAllLines(shortcutPath))
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) url = line[4..].Trim();
            else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) iconFile = line[9..].Trim();
        }
        if (string.IsNullOrEmpty(url)) return null;

        return new AppItem
        {
            Name = Path.GetFileNameWithoutExtension(shortcutPath),
            TargetPath = url,
            IconSources = !string.IsNullOrEmpty(iconFile) && File.Exists(iconFile) ? new List<string> { iconFile } : new List<string>()
        };
    }

}
