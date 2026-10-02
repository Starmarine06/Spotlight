using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Windows;
using Microsoft.Win32;

namespace Spotlight.App.Services;

public class ServiceItem
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class SystemActions
{
    // Native Lock Workstation
    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    // Native Volume Control (via keybd_event simulation)
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // Native Empty Recycle Bin
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    public static void Lock()
    {
        LockWorkStation();
    }

    public static void Sleep()
    {
        var psi = new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        Process.Start(psi);
    }

    public static void Hibernate()
    {
        Process.Start(new ProcessStartInfo("shutdown.exe", "/h") { CreateNoWindow = true, UseShellExecute = false });
    }

    public static void SignOut()
    {
        Process.Start(new ProcessStartInfo("shutdown.exe", "/l") { CreateNoWindow = true, UseShellExecute = false });
    }

    public static void Shutdown()
    {
        var psi = new ProcessStartInfo("shutdown.exe", "/s /t 0")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        Process.Start(psi);
    }

    public static void Restart()
    {
        var psi = new ProcessStartInfo("shutdown.exe", "/r /t 0")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        Process.Start(psi);
    }

    public static void VolumeUp()
    {
        keybd_event(VK_VOLUME_UP, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_VOLUME_UP, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void VolumeDown()
    {
        keybd_event(VK_VOLUME_DOWN, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_VOLUME_DOWN, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void Mute()
    {
        keybd_event(VK_VOLUME_MUTE, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_VOLUME_MUTE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void EmptyRecycleBin()
    {
        SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
    }

    public static void LaunchFile(string path, string? arguments = null, bool admin = false)
    {
        try
        {
            // Allow "~\Documents" and "%USERPROFILE%\x" typed into the launcher.
            if (path.StartsWith("~\\") || path.StartsWith("~/"))
                path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
            path = Environment.ExpandEnvironmentVariables(path);

            if (path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                var psi = new ProcessStartInfo("explorer.exe", path)
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
                return;
            }

            var psi2 = new ProcessStartInfo(path)
            {
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true
            };
            if (admin) psi2.Verb = "runas";

            // Many programs resolve resources relative to their own folder.
            if (System.IO.File.Exists(path))
                psi2.WorkingDirectory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
            Process.Start(psi2);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open: {ex.Message}", "Spotlight Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void RunCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/k {command}")
            {
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not run command: {ex.Message}", "Spotlight Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void OpenRegistryKey(string registryPath)
    {
        try
        {
            var formattedPath = registryPath.Trim().Replace("/", "\\");
            
            if (formattedPath.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase))
                formattedPath = "HKEY_LOCAL_MACHINE" + formattedPath.Substring(4);
            else if (formattedPath.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase))
                formattedPath = "HKEY_CURRENT_USER" + formattedPath.Substring(4);
            else if (formattedPath.StartsWith("HKCR", StringComparison.OrdinalIgnoreCase))
                formattedPath = "HKEY_CLASSES_ROOT" + formattedPath.Substring(4);
            else if (formattedPath.StartsWith("HKU", StringComparison.OrdinalIgnoreCase))
                formattedPath = "HKEY_USERS" + formattedPath.Substring(3);

            if (!formattedPath.StartsWith("Computer\\", StringComparison.OrdinalIgnoreCase) && 
                !formattedPath.StartsWith(@"\\", StringComparison.Ordinal))
            {
                formattedPath = "Computer\\" + formattedPath;
            }

            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit", true))
            {
                if (key != null)
                {
                    key.SetValue("LastKey", formattedPath);
                }
            }

            Process.Start("regedit.exe");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open registry: {ex.Message}", "Spotlight Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static List<ServiceItem> GetServices()
    {
        var servicesList = new List<ServiceItem>();
        try
        {
            foreach (var sc in ServiceController.GetServices())
            {
                servicesList.Add(new ServiceItem
                {
                    Name = sc.ServiceName,
                    DisplayName = sc.DisplayName,
                    Status = sc.Status.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get services list: {ex.Message}");
        }
        return servicesList;
    }

    public static void ControlService(string serviceName, string action)
    {
        try
        {
            string command = string.Empty;
            if (action.Equals("start", StringComparison.OrdinalIgnoreCase))
                command = $"Start-Service -Name '{serviceName}'";
            else if (action.Equals("stop", StringComparison.OrdinalIgnoreCase))
                command = $"Stop-Service -Name '{serviceName}' -Force";
            else if (action.Equals("restart", StringComparison.OrdinalIgnoreCase))
                command = $"Restart-Service -Name '{serviceName}' -Force";

            if (string.IsNullOrEmpty(command)) return;

            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{command}\"",
                Verb = "runas",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to execute service control action '{action}': {ex.Message}", "Spotlight Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static async void PasteClip(string text, MainWindow window)
    {
        try
        {
            // Set text to clipboard
            CopyToClipboard(text);

            // Hide the window
            window.Dispatcher.Invoke(() => window.HideWindow());

            // Wait brief moment (100ms) for target window to reclaim active focus
            await System.Threading.Tasks.Task.Delay(180);

            // Simulate Ctrl + V keypresses
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to simulate auto-paste: {ex.Message}");
        }
    }

    public static void ShowInExplorer(string path)
    {
        try
        {
            if (System.IO.File.Exists(path) || System.IO.Directory.Exists(path))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    public static void CopyToClipboard(string text)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        });
    }
}
