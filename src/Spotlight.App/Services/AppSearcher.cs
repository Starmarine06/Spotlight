using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Win32;

namespace Spotlight.App.Services;

public class AppItem
{
    public string Name { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string IconBase64 { get; set; } = string.Empty;
}

public class AppSearcher
{
    private List<AppItem> _cachedApps = new();

    public List<AppItem> GetApps()
    {
        if (_cachedApps.Count > 0)
        {
            return _cachedApps;
        }

        RefreshCache();
        return _cachedApps;
    }

    public void RefreshCache()
    {
        var apps = new List<AppItem>();
        
        // 1. Start Menu Locations
        var userStartMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            @"Microsoft\Windows\Start Menu\Programs"
        );
        var commonStartMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), 
            @"Microsoft\Windows\Start Menu\Programs"
        );

        // 2. Desktop Locations (important for PWAs/Webapp shortcuts)
        var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        ScanDirectory(userStartMenu, apps);
        ScanDirectory(commonStartMenu, apps);
        ScanDirectory(userDesktop, apps);
        ScanDirectory(commonDesktop, apps);

        // 3. Modern / Store / UWP Apps via shell:AppsFolder
        ScanAppsFolder(apps);

        // 4. Registry App Paths
        ScanRegistryAppPaths(apps);

        // 5. Common Windows System Utilities
        AddSystemUtilities(apps);

        // De-duplicate by name, preferring entries with valid icons and direct executable paths
        var uniqueApps = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var nameKey = app.Name.Trim();
            if (string.IsNullOrEmpty(nameKey)) continue;

            if (!uniqueApps.TryGetValue(nameKey, out var existing))
            {
                uniqueApps[nameKey] = app;
            }
            else
            {
                // Prefer item with an icon
                if (string.IsNullOrEmpty(existing.IconBase64) && !string.IsNullOrEmpty(app.IconBase64))
                {
                    uniqueApps[nameKey] = app;
                }
                // Prefer direct executable file path over shell / virtual path
                else if (!File.Exists(existing.TargetPath) && File.Exists(app.TargetPath))
                {
                    uniqueApps[nameKey] = app;
                }
            }
        }

        _cachedApps = new List<AppItem>(uniqueApps.Values);
    }

    private void ScanDirectory(string dirPath, List<AppItem> apps)
    {
        if (!Directory.Exists(dirPath)) return;

        try
        {
            // Scan for .lnk shortcuts
            foreach (var file in Directory.GetFiles(dirPath, "*.lnk", SearchOption.AllDirectories))
            {
                try
                {
                    var app = ParseLnkShortcut(file);
                    if (app != null && !IsJunkOrUninstaller(app.Name, app.TargetPath))
                    {
                        apps.Add(app);
                    }
                }
                catch
                {
                    // Ignore individual shortcut parsing errors
                }
            }

            // Scan for .url shortcuts
            foreach (var file in Directory.GetFiles(dirPath, "*.url", SearchOption.AllDirectories))
            {
                try
                {
                    var app = ParseUrlShortcut(file);
                    if (app != null && !IsJunkOrUninstaller(app.Name, app.TargetPath))
                    {
                        apps.Add(app);
                    }
                }
                catch
                {
                    // Ignore individual parsing errors
                }
            }
        }
        catch
        {
            // Ignore directory scanning access errors
        }
    }

    private void ScanAppsFolder(List<AppItem> apps)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            if (folder == null) return;

            foreach (dynamic item in folder.Items())
            {
                try
                {
                    string name = item.Name;
                    string path = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;

                    if (IsJunkOrUninstaller(name, path)) continue;

                    string target = path;
                    if (!File.Exists(target) && !Directory.Exists(target) && !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    {
                        target = $"shell:AppsFolder\\{path}";
                    }

                    var app = new AppItem
                    {
                        Name = name,
                        TargetPath = target,
                        Arguments = string.Empty,
                        IconBase64 = File.Exists(path) ? ExtractIconAsBase64(path) : string.Empty
                    };
                    apps.Add(app);
                }
                catch
                {
                    // Skip individual item errors
                }
            }
        }
        catch
        {
            // Ignore COM errors
        }
    }

    private void ScanRegistryAppPaths(List<AppItem> apps)
    {
        string[] rootKeys = new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths" };
        RegistryKey[] hives = new[] { Registry.LocalMachine, Registry.CurrentUser };

        foreach (var hive in hives)
        {
            foreach (var subKeyPath in rootKeys)
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
                            if (appKey == null) continue;

                            var defaultVal = appKey.GetValue(null)?.ToString();
                            if (string.IsNullOrEmpty(defaultVal)) continue;

                            defaultVal = Environment.ExpandEnvironmentVariables(defaultVal).Trim('\"');
                            if (!File.Exists(defaultVal)) continue;

                            var friendlyName = Path.GetFileNameWithoutExtension(subName);
                            if (string.IsNullOrEmpty(friendlyName)) continue;

                            try
                            {
                                var vi = FileVersionInfo.GetVersionInfo(defaultVal);
                                if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                                {
                                    friendlyName = vi.FileDescription;
                                }
                            }
                            catch { }

                            if (IsJunkOrUninstaller(friendlyName, defaultVal)) continue;

                            apps.Add(new AppItem
                            {
                                Name = friendlyName,
                                TargetPath = defaultVal,
                                Arguments = string.Empty,
                                IconBase64 = ExtractIconAsBase64(defaultVal)
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
    }

    private void AddSystemUtilities(List<AppItem> apps)
    {
        var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var utilities = new (string Name, string Path)[]
        {
            ("Command Prompt", Path.Combine(sys32, "cmd.exe")),
            ("PowerShell", Path.Combine(sys32, @"WindowsPowerShell\v1.0\powershell.exe")),
            ("Task Manager", Path.Combine(sys32, "Taskmgr.exe")),
            ("Control Panel", Path.Combine(sys32, "control.exe")),
            ("Registry Editor", Path.Combine(winDir, "regedit.exe")),
            ("Calculator", Path.Combine(sys32, "calc.exe")),
            ("Paint", Path.Combine(sys32, "mspaint.exe")),
            ("Notepad", Path.Combine(sys32, "notepad.exe")),
            ("Remote Desktop Connection", Path.Combine(sys32, "mstsc.exe")),
            ("Snipping Tool", Path.Combine(sys32, "SnippingTool.exe")),
            ("Disk Cleanup", Path.Combine(sys32, "cleanmgr.exe")),
            ("Resource Monitor", Path.Combine(sys32, "resmon.exe")),
            ("DirectX Diagnostic Tool", Path.Combine(sys32, "dxdiag.exe")),
            ("System Information", Path.Combine(sys32, "msinfo32.exe")),
            ("Device Manager", Path.Combine(sys32, "devmgmt.msc")),
            ("Services", Path.Combine(sys32, "services.msc")),
            ("Computer Management", Path.Combine(sys32, "compmgmt.msc")),
            ("Event Viewer", Path.Combine(sys32, "eventvwr.msc"))
        };

        foreach (var util in utilities)
        {
            if (File.Exists(util.Path))
            {
                apps.Add(new AppItem
                {
                    Name = util.Name,
                    TargetPath = util.Path,
                    Arguments = string.Empty,
                    IconBase64 = ExtractIconAsBase64(util.Path)
                });
            }
        }
    }

    private static bool IsJunkOrUninstaller(string name, string targetPath)
    {
        var lowerName = name.ToLowerInvariant();
        var lowerPath = targetPath.ToLowerInvariant();

        if (lowerName.Contains("uninstall") || 
            lowerName.Contains("unins000") || 
            lowerName.Contains("remove ") || 
            lowerName.StartsWith("uninstall") ||
            lowerName.EndsWith(" uninstaller") ||
            lowerName.Contains("documentation") || 
            lowerName.Contains("release notes") || 
            lowerName.Contains("readme") || 
            lowerName.Contains("help manual") ||
            lowerName.Contains("license") ||
            lowerName.Contains("terms of service") ||
            lowerName.Contains("website") ||
            lowerName.Contains("homepage"))
        {
            return true;
        }

        if (lowerPath.Contains("unins000.exe") || 
            lowerPath.Contains("uninstall.exe") ||
            lowerPath.EndsWith(".txt") ||
            lowerPath.EndsWith(".chm") ||
            lowerPath.EndsWith(".html") ||
            lowerPath.EndsWith(".htm"))
        {
            return true;
        }

        return false;
    }

    private AppItem? ParseLnkShortcut(string shortcutPath)
    {
        var shellType = Type.GetTypeFromProgID("Wscript.Shell");
        if (shellType == null) return null;

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        string targetPath = shortcut.TargetPath;
        string arguments = shortcut.Arguments;

        if (string.IsNullOrEmpty(targetPath)) return null;

        targetPath = Environment.ExpandEnvironmentVariables(targetPath);

        // Verify target path exists
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return null;
        }

        var app = new AppItem
        {
            Name = Path.GetFileNameWithoutExtension(shortcutPath),
            TargetPath = targetPath,
            Arguments = arguments ?? string.Empty
        };

        app.IconBase64 = ExtractIconAsBase64(targetPath);
        return app;
    }

    private AppItem? ParseUrlShortcut(string shortcutPath)
    {
        var lines = File.ReadAllLines(shortcutPath);
        string url = string.Empty;
        string iconFile = string.Empty;

        foreach (var line in lines)
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                url = line.Substring(4).Trim();
            }
            else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
            {
                iconFile = line.Substring(9).Trim();
            }
        }

        if (string.IsNullOrEmpty(url)) return null;

        var app = new AppItem
        {
            Name = Path.GetFileNameWithoutExtension(shortcutPath),
            TargetPath = url,
            Arguments = string.Empty
        };

        if (!string.IsNullOrEmpty(iconFile) && File.Exists(iconFile))
        {
            app.IconBase64 = ExtractIconAsBase64(iconFile);
        }

        return app;
    }

    private string ExtractIconAsBase64(string path)
    {
        try
        {
            if (Directory.Exists(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            using (var icon = Icon.ExtractAssociatedIcon(path))
            {
                if (icon == null) return string.Empty;

                using (var bitmap = icon.ToBitmap())
                {
                    using (var ms = new MemoryStream())
                    {
                        bitmap.Save(ms, ImageFormat.Png);
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
        }
        catch
        {
            return string.Empty;
        }
    }
}
