using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Spotlight.Setup;

/// <summary>
/// Installer and updater. An update is a real replace, not an overlay:
///   1. wait for the running app to exit (kill it only if it will not),
///   2. extract the new version to a staging folder and verify it,
///   3. move the old version out of the way (into .backup), keeping only user data,
///   4. move the new version in; if anything fails, restore the backup,
///   5. relaunch.
/// User data lives in the "data" sub-folder and is never touched.
/// </summary>
class Program
{
    private const string RepoOwner = "Starmarine06";
    private const string RepoName = "Spotlight";
    private const string AppExeName = "Spotlight.App.exe";

    // Top-level entries inside the install folder that must survive an update.
    private static readonly string[] Preserved =
    {
        "data", ".staging", ".backup", "clipboard_history.json", "gemini_key.txt"
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly string InstallDir = Environment.GetEnvironmentVariable("SPOTLIGHT_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spotlight");
    private static readonly string StagingDir = Path.Combine(InstallDir, ".staging");
    private static readonly string BackupDir = Path.Combine(InstallDir, ".backup");
    private static readonly string LogPath = Path.Combine(InstallDir, "data", "setup.log");

    static async Task<int> Main(string[] args)
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Spotlight-Setup");

        bool silent = HasFlag(args, "--silent");
        bool isTempRunner = HasFlag(args, "--temp-runner");
        int waitPid = ArgValue(args, "--wait-pid") is { } p && int.TryParse(p, out var pid) ? pid : 0;
        string? zipArg = args.Length >= 2 && args[0].Equals("update", StringComparison.OrdinalIgnoreCase) ? args[1] : null;

        // Running from inside the install folder would lock our own exe while replacing it: hop to a temp copy.
        string currentExe = Environment.ProcessPath ?? string.Empty;
        if (!isTempRunner && currentExe.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var runnerDir = Path.Combine(Path.GetTempPath(), "SpotlightSetup-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(runnerDir);
                var runner = Path.Combine(runnerDir, "SpotlightSetupRunner.exe");
                File.Copy(currentExe, runner, true);
                var psi = new ProcessStartInfo(runner)
                {
                    UseShellExecute = false,
                    CreateNoWindow = silent
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                psi.ArgumentList.Add("--temp-runner");
                Process.Start(psi);
                return 0;
            }
            catch (Exception ex)
            {
                Log($"Could not spawn temp runner, continuing in place: {ex.Message}");
            }
        }

        Log("==================================================");
        Log("  Spotlight Setup");
        Log("==================================================");

        try
        {
            var zipPath = await ResolvePackageAsync(zipArg);
            if (zipPath == null) return Fail("Could not find or download a Spotlight package.", silent);

            ValidatePackage(zipPath);

            Directory.CreateDirectory(InstallDir);
            bool firstInstall = !File.Exists(Path.Combine(InstallDir, AppExeName));

            StopRunningApp(waitPid);

            Stage(zipPath);
            ReplaceInstall();

            // Packages we downloaded into %TEMP% are single-use; never delete a zip the user handed us.
            if (zipPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) TryDelete(zipPath);

            var targetExe = Path.Combine(InstallDir, AppExeName);
            // A relocated install (SPOTLIGHT_HOME, used for testing) must not rewire the user's real shortcuts.
            if (Environment.GetEnvironmentVariable("SPOTLIGHT_HOME") == null)
            {
                CreateShortcut(targetExe);
                RegisterStartup(targetExe, firstInstall);
            }

            Log("Launching Spotlight...");
            Process.Start(new ProcessStartInfo(targetExe) { WorkingDirectory = InstallDir, UseShellExecute = true });

            Log("Spotlight was installed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail($"Setup failed: {ex.Message}", silent, ex);
        }
    }

    // ------------------------------------------------------------------ package

    private static async Task<string?> ResolvePackageAsync(string? zipArg)
    {
        if (!string.IsNullOrEmpty(zipArg) && File.Exists(zipArg))
        {
            Log($"Using provided package: {zipArg}");
            return zipArg;
        }

        // Offline install: a Spotlight_*.zip sitting next to the setup exe.
        var sibling = Directory.EnumerateFiles(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "Spotlight_*.zip")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (sibling != null)
        {
            Log($"Using local package: {sibling}");
            return sibling;
        }

        Log("Looking up the latest release on GitHub...");
        var (tag, url) = await GetLatestReleaseAsync();
        if (url == null) return null;

        Log($"Downloading {tag}...");
        var dest = Path.Combine(Path.GetTempPath(), $"Spotlight_{tag}.zip");
        TryDelete(dest);
        await DownloadAsync(url, dest);
        return dest;
    }

    private static async Task<(string Tag, string? Url)> GetLatestReleaseAsync()
    {
        try
        {
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return (tag, asset.GetProperty("browser_download_url").GetString());
            }
        }
        catch (Exception ex)
        {
            Log($"Could not query GitHub: {ex.Message}");
        }
        return (string.Empty, null);
    }

    private static async Task DownloadAsync(string url, string destination)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? -1;

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long done = 0;
        int lastPercent = -1;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            if (total > 0)
            {
                int percent = (int)(done * 100 / total);
                if (percent / 10 != lastPercent / 10) { Log($"  {percent}%"); lastPercent = percent; }
            }
        }
    }

    private static void ValidatePackage(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (!zip.Entries.Any(e => e.FullName.Equals(AppExeName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"The package does not contain {AppExeName}.");
        Log("Package verified.");
    }

    // ------------------------------------------------------------------ replace

    private static void StopRunningApp(int waitPid)
    {
        if (waitPid > 0)
        {
            try
            {
                Log($"Waiting for Spotlight (PID {waitPid}) to exit...");
                using var proc = Process.GetProcessById(waitPid);
                if (!proc.WaitForExit(15000))
                {
                    Log("It did not exit in time; terminating it.");
                    proc.Kill(true);
                    proc.WaitForExit(5000);
                }
            }
            catch (ArgumentException) { /* already gone */ }
            catch (Exception ex) { Log($"Wait failed: {ex.Message}"); }
        }

        // Anything else still running from THIS install folder (e.g. a second instance) would block deletion.
        // Instances that live elsewhere (a dev build, another install) are none of our business.
        foreach (var p in Process.GetProcessesByName("Spotlight.App"))
        {
            try
            {
                var path = p.MainModule?.FileName ?? string.Empty;
                if (!path.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase)) continue;

                Log($"Stopping Spotlight process {p.Id}...");
                p.Kill(true);
                p.WaitForExit(5000);
            }
            catch (Exception ex) { Log($"Could not stop {p.Id}: {ex.Message}"); }
            finally { p.Dispose(); }
        }
        Thread.Sleep(400);
    }

    private static void Stage(string zipPath)
    {
        Log("Extracting new version...");
        DeleteDirectory(StagingDir);
        Directory.CreateDirectory(StagingDir);
        ZipFile.ExtractToDirectory(zipPath, StagingDir, true);

        if (!File.Exists(Path.Combine(StagingDir, AppExeName)))
            throw new InvalidDataException("Extraction produced an incomplete package.");
    }

    private static void ReplaceInstall()
    {
        Log("Removing the old version...");
        DeleteDirectory(BackupDir);
        Directory.CreateDirectory(BackupDir);

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(InstallDir).ToList())
            {
                var name = Path.GetFileName(entry);
                if (Preserved.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                // Leftovers from old releases (fd.exe, logs, WebView2 profile) are not worth restoring.
                if (IsLegacyCruft(name))
                {
                    TryDelete(entry);
                    continue;
                }

                MoveWithRetry(entry, Path.Combine(BackupDir, name));
            }

            Log("Installing the new version...");
            foreach (var entry in Directory.EnumerateFileSystemEntries(StagingDir).ToList())
                MoveWithRetry(entry, Path.Combine(InstallDir, Path.GetFileName(entry)));
        }
        catch
        {
            Log("Install failed - restoring the previous version.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(InstallDir).ToList())
            {
                if (!Preserved.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase)) TryDelete(entry);
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(BackupDir).ToList())
                MoveWithRetry(entry, Path.Combine(InstallDir, Path.GetFileName(entry)));
            throw;
        }

        DeleteDirectory(StagingDir);
        DeleteDirectory(BackupDir);
    }

    private static bool IsLegacyCruft(string name) =>
        name.Equals("fd.exe", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("ai_debug.log", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".WebView2", StringComparison.OrdinalIgnoreCase);

    private static void MoveWithRetry(string source, string destination)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(source)) Directory.Move(source, destination);
                else File.Move(source, destination, true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) when (attempt < 10)
            {
                Thread.Sleep(300);
            }
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { Directory.Delete(path, true); return; }
            catch { Thread.Sleep(250); }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) DeleteDirectory(path);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    // ------------------------------------------------------------------ shell integration

    private static void CreateShortcut(string targetExe)
    {
        try
        {
            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
            var shortcutPath = Path.Combine(startMenu, "Spotlight.lnk");
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return;

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetExe;
            shortcut.WorkingDirectory = InstallDir;
            shortcut.Description = "Fast keyboard-driven search and launcher for Windows";
            shortcut.IconLocation = targetExe + ",0";
            shortcut.Save();
        }
        catch (Exception ex)
        {
            Log($"Could not create Start Menu shortcut: {ex.Message}");
        }
    }

    private static void RegisterStartup(string targetExe, bool firstInstall)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            // Updates must not silently re-enable a startup entry the user removed.
            if (firstInstall || key.GetValue("Spotlight") != null)
                key.SetValue("Spotlight", $"\"{targetExe}\"");
        }
        catch (Exception ex)
        {
            Log($"Could not register startup entry: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ helpers

    private static bool HasFlag(string[] args, string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string? ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static int Fail(string message, bool silent, Exception? ex = null)
    {
        Log("[ERROR] " + message);
        if (ex != null) Log(ex.ToString());
        if (!silent)
        {
            Console.WriteLine("Press Enter to close...");
            try { Console.ReadLine(); } catch { }
        }
        return 1;
    }

    private static void Log(string message)
    {
        Console.WriteLine(message);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
