using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spotlight.App.Services;

public record UpdateInfo(string Version, string DownloadUrl, string Notes, long Size);

public static class UpdateService
{
    private const string Repo = "Starmarine06/Spotlight";

    // Overridable so a mirror (or a test server) can stand in for GitHub.
    private static readonly string LatestReleaseUrl =
        Environment.GetEnvironmentVariable("SPOTLIGHT_UPDATE_API") ?? $"https://api.github.com/repos/{Repo}/releases/latest";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Spotlight-App");
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    public static string CurrentVersionText => CurrentVersion.ToString();

    /// <summary>Returns the newest release if (and only if) it is strictly newer than the running version.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(LatestReleaseUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            if (tag == null || !Version.TryParse(Normalize(tag), out var latest)) return null;
            if (latest <= CurrentVersion) return null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

                return new UpdateInfo(
                    latest.ToString(),
                    asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                    root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
                    asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0);
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("Update check failed: " + ex.Message);
        }
        return null;
    }

    private static string Normalize(string tag)
    {
        // "1.3" -> "1.3.0" so Version.TryParse accepts it and comparisons are consistent.
        var parts = tag.Split('-', '+')[0].Split('.');
        return parts.Length == 2 ? tag.Split('-', '+')[0] + ".0" : tag.Split('-', '+')[0];
    }

    /// <summary>
    /// Downloads the release, then hands over to the Setup tool (taken from the NEW package, so the newest
    /// replace logic always applies) which waits for this process to exit, replaces the install and relaunches.
    /// Returns an error message, or null when the hand-over succeeded and the app should shut down.
    /// </summary>
    public static async Task<string?> DownloadAndInstallAsync(UpdateInfo info, Action<int> progress)
    {
        try
        {
            var zipPath = Path.Combine(Path.GetTempPath(), "SpotlightUpdate.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);

            using (var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? info.Size;

                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920];
                long done = 0;
                int last = -1, read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    if (total > 0)
                    {
                        int percent = (int)(done * 100 / total);
                        if (percent != last) { last = percent; progress(percent); }
                    }
                }
            }

            // Prefer the updater inside the new package; fall back to the one we shipped with.
            var runnerDir = Path.Combine(Path.GetTempPath(), "SpotlightUpdater-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(runnerDir);
            var runner = Path.Combine(runnerDir, "SpotlightSetupRunner.exe");

            using (var zip = ZipFile.OpenRead(zipPath))
            {
                if (zip.GetEntry("Spotlight.App.exe") == null)
                    return "The downloaded package is incomplete.";

                var setupEntry = zip.GetEntry("Spotlight.Setup.exe");
                if (setupEntry != null) setupEntry.ExtractToFile(runner, true);
            }

            if (!File.Exists(runner))
            {
                var local = Path.Combine(AppContext.BaseDirectory, "Spotlight.Setup.exe");
                if (!File.Exists(local)) return "Spotlight.Setup.exe was not found; please reinstall manually.";
                File.Copy(local, runner, true);
            }

            var psi = new ProcessStartInfo(runner)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = runnerDir
            };
            psi.ArgumentList.Add("update");
            psi.ArgumentList.Add(zipPath);
            psi.ArgumentList.Add("--wait-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add("--silent");
            psi.ArgumentList.Add("--temp-runner");
            Process.Start(psi);
            return null;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Update failed: " + ex);
            return ex.Message;
        }
    }
}
