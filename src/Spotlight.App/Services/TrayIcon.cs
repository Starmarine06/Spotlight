using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using WinForms = System.Windows.Forms;

namespace Spotlight.App.Services;

/// <summary>System tray presence: Spotlight has no taskbar button, so this is the way to open settings, update or quit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _updateItem;

    public TrayIcon(MainWindow window, HotKeyManager hotKeys)
    {
        var menu = new WinForms.ContextMenuStrip();

        var hotkeyText = hotKeys.ActiveHotkeys.FirstOrDefault()?.Replace(" (hook)", "") ?? "no hotkey";
        menu.Items.Add($"Open Spotlight\t{hotkeyText}", null, (_, _) => window.Dispatcher.Invoke(() => window.ShowWindow()));
        menu.Items.Add("Clipboard history", null, (_, _) => window.Dispatcher.Invoke(() => window.ShowWindow("clipboard")));
        menu.Items.Add(new WinForms.ToolStripSeparator());

        _updateItem = new WinForms.ToolStripMenuItem($"Version {UpdateService.CurrentVersionText} - check for updates");
        _updateItem.Click += (_, _) => window.Dispatcher.Invoke(window.CheckForUpdatesFromTray);
        menu.Items.Add(_updateItem);

        menu.Items.Add("Edit settings", null, (_, _) => OpenInShell(Settings.FilePath));
        menu.Items.Add("Open log", null, (_, _) => OpenInShell(AppPaths.LogFile));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quit Spotlight", null, (_, _) => window.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown()));

        System.Drawing.Icon icon;
        try
        {
            icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        }
        catch
        {
            icon = System.Drawing.SystemIcons.Application;
        }

        _icon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = $"Spotlight - {hotkeyText}",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) window.Dispatcher.Invoke(() => window.ShowWindow());
        };
    }

    public void SetUpdateAvailable(string version) => _updateItem.Text = $"Update to {version}...";

    public void Balloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(4000);
    }

    private static void OpenInShell(string path)
    {
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, string.Empty);
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
