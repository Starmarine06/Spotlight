using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

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
        
        // Start Menu Locations
        var userStartMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            @"Microsoft\Windows\Start Menu\Programs"
        );
        var commonStartMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), 
            @"Microsoft\Windows\Start Menu\Programs"
        );

        // Desktop Locations (important for PWAs/Webapp shortcuts)
        var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        ScanDirectory(userStartMenu, apps);
        ScanDirectory(commonStartMenu, apps);
        ScanDirectory(userDesktop, apps);
        ScanDirectory(commonDesktop, apps);

        // De-duplicate by name and executable path + arguments
        var uniqueApps = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var key = app.Name + "|" + app.TargetPath + "|" + app.Arguments;
            if (!uniqueApps.ContainsKey(key))
            {
                uniqueApps[key] = app;
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
                    if (app != null) apps.Add(app);
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
                    if (app != null) apps.Add(app);
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
            if (Directory.Exists(path))
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
