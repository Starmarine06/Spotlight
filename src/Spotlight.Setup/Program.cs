using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Spotlight.Setup;

class Program
{
    private static readonly HttpClient HttpClient = new HttpClient();
    private const string RepoOwner = "Starmarine06";
    private const string RepoName = "Spotlight";

    static async Task<int> Main(string[] args)
    {
        // Add User-Agent header required by GitHub API
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Spotlight-Setup-Utility");

        string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spotlight");
        string targetExe = Path.Combine(installDir, "Spotlight.App.exe");

        Console.WriteLine("==================================================");
        Console.WriteLine("          Spotlight Setup & Auto-Updater          ");
        Console.WriteLine("==================================================");

        try
        {
            // 1. Check arguments for local update path
            string? localZipPath = null;
            if (args.Length >= 2 && args[0].Equals("update", StringComparison.OrdinalIgnoreCase))
            {
                localZipPath = args[1];
                Console.WriteLine($"[Info] Local update path provided: {localZipPath}");
            }

            // 2. Stop any active running instances of Spotlight
            Console.WriteLine("[*] Stopping any running Spotlight instances...");
            KillRunningInstances();
            Thread.Sleep(1000); // Give processes time to release file locks

            // 3. Ensure installation folder exists
            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
                Console.WriteLine($"[Info] Created installation directory: {installDir}");
            }

            // 4. Download latest package if local path not provided
            string zipPath;
            if (!string.IsNullOrEmpty(localZipPath) && File.Exists(localZipPath))
            {
                zipPath = localZipPath;
            }
            else
            {
                Console.WriteLine("[*] Fetching latest release info from GitHub...");
                var releaseInfo = await GetLatestReleaseInfoAsync();
                if (releaseInfo == null || string.IsNullOrEmpty(releaseInfo.DownloadUrl))
                {
                    Console.WriteLine("[Error] Could not retrieve download URL for the latest version.");
                    return 1;
                }

                Console.WriteLine($"[Info] Latest version: {releaseInfo.TagName}");
                zipPath = Path.Combine(Path.GetTempPath(), $"Spotlight_{releaseInfo.TagName}.zip");

                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                Console.WriteLine($"[*] Downloading update from: {releaseInfo.DownloadUrl}...");
                await DownloadFileAsync(releaseInfo.DownloadUrl, zipPath);
                Console.WriteLine("[Success] Download complete.");
            }

            // 5. Extract files
            Console.WriteLine($"[*] Extracting update payload to: {installDir}...");
            ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);
            Console.WriteLine("[Success] Files extracted.");

            // Cleanup temp downloaded file if it was online mode
            if (string.IsNullOrEmpty(localZipPath))
            {
                try { File.Delete(zipPath); } catch { /* Ignore */ }
            }

            // 6. Setup Shortcuts and Registry Run Keys
            Console.WriteLine("[*] Configuring Start Menu shortcut...");
            string startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
            string shortcutPath = Path.Combine(startMenuPath, "Spotlight.lnk");
            CreateShortcut(targetExe, installDir, shortcutPath);

            Console.WriteLine("[*] Registering run-at-startup Registry keys...");
            SetStartupRegistryKey(targetExe);

            // 7. Restart application
            if (File.Exists(targetExe))
            {
                Console.WriteLine("[*] Launching Spotlight...");
                Process.Start(new ProcessStartInfo(targetExe)
                {
                    WorkingDirectory = installDir,
                    UseShellExecute = true
                });
                Console.WriteLine("[Success] Spotlight successfully launched.");
            }
            else
            {
                Console.WriteLine($"[Error] Could not find executable to launch at: {targetExe}");
                return 1;
            }

            Console.WriteLine("==================================================");
            Console.WriteLine("      Spotlight has been successfully updated!    ");
            Console.WriteLine("==================================================");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Setup/Update failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ReadLine();
            return 1;
        }
    }

    private static void KillRunningInstances()
    {
        var processes = Process.GetProcessesByName("Spotlight.App");
        foreach (var p in processes)
        {
            try
            {
                Console.WriteLine($"[Info] Terminating process PID {p.Id}...");
                p.Kill();
                p.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Failed to stop PID {p.Id}: {ex.Message}");
            }
        }
    }

    private static async Task<GitHubRelease?> GetLatestReleaseInfoAsync()
    {
        string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        try
        {
            string response = await HttpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            string tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
            string downloadUrl = string.Empty;

            if (root.TryGetProperty("assets", out var assetsProp))
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? string.Empty;
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                        break;
                    }
                }
            }

            return new GitHubRelease
            {
                TagName = tagName,
                DownloadUrl = downloadUrl
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Warning] Failed to fetch release details: {ex.Message}");
            return null;
        }
    }

    private static async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        await response.Content.CopyToAsync(fileStream);
    }

    private static void CreateShortcut(string targetExe, string workingDir, string shortcutPath)
    {
        try
        {
            // Execute hidden PowerShell instance to create COM-based shortcut
            string escapedShortcutPath = shortcutPath.Replace("'", "''");
            string escapedTargetExe = targetExe.Replace("'", "''");
            string escapedWorkingDir = workingDir.Replace("'", "''");

            string psCommand = $"$WshShell = New-Object -ComObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('{escapedShortcutPath}'); $Shortcut.TargetPath = '{escapedTargetExe}'; $Shortcut.WorkingDirectory = '{escapedWorkingDir}'; $Shortcut.Description = 'Fast keyboard-driven search and launcher for Windows'; $Shortcut.Save()";
            
            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{psCommand}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Warning] Failed to create Start Menu shortcut: {ex.Message}");
        }
    }

    private static void SetStartupRegistryKey(string targetExe)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                key.SetValue("Spotlight", $"\"{targetExe}\"");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Warning] Failed to set auto-run Registry key: {ex.Message}");
        }
    }

    private class GitHubRelease
    {
        public string TagName { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
    }
}
