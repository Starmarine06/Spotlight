using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Spotlight.App.Services;

namespace Spotlight.App.Bridge;

/// <summary>Message contract between the React UI and the native host. Every message is JSON with a "type".</summary>
public class WebViewBridge
{
    private readonly MainWindow _window;
    private readonly AppSearcher _apps;
    private readonly FileIndex _files;

    public WebViewBridge(MainWindow window, AppSearcher apps, FileIndex files)
    {
        _window = window;
        _apps = apps;
        _files = files;
        _files.IndexChanged += () => Send("index_status", new { ready = _files.Ready, count = _files.Count });
    }

    public void PushApps(List<AppItem> apps) => Send("apps_loaded", apps);

    public async Task HandleMessage(string rawMessage, CoreWebView2 sender)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;
            var type = Str(root, "type");
            if (type == null) return;

            switch (type)
            {
                case "init":
                    Send("app_info", new
                    {
                        version = UpdateService.CurrentVersionText,
                        hotkeys = Settings.Current.SearchHotkeys,
                        indexReady = _files.Ready,
                        indexCount = _files.Count
                    });
                    var cached = _apps.GetCached();
                    if (cached.Count > 0) PushApps(cached);
                    _ = _apps.RefreshAsync();
                    break;

                case "refresh_apps":
                    _ = _apps.RefreshAsync();
                    break;

                case "search_files":
                {
                    var query = Str(root, "query") ?? string.Empty;
                    var id = Int(root, "id");
                    var limit = Math.Clamp(Int(root, "limit", 60), 1, 300);
                    var foldersOnly = Bool(root, "foldersOnly");
                    await Task.Run(() =>
                    {
                        var results = _files.Search(query, limit);
                        if (foldersOnly) results = results.FindAll(f => f.IsFolder);
                        Send("files_results", new { id, query, ready = _files.Ready, files = results });
                    });
                    break;
                }

                case "recent_files":
                    await Task.Run(() => Send("files_results", new { id = Int(root, "id"), query = string.Empty, ready = _files.Ready, files = _files.Recent() }));
                    break;

                case "get_services":
                    await Task.Run(() => Send("services_loaded", SystemActions.GetServices()));
                    break;

                case "control_service":
                {
                    var name = Str(root, "serviceName");
                    var action = Str(root, "action");
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(action))
                    {
                        await Task.Run(() => SystemActions.ControlService(name, action));
                        _window.HideWindow();
                    }
                    break;
                }

                case "open_registry":
                    if (Str(root, "path") is { Length: > 0 } regPath)
                    {
                        SystemActions.OpenRegistryKey(regPath);
                        _window.HideWindow();
                    }
                    break;

                case "run_cmd":
                    if (Str(root, "command") is { Length: > 0 } command)
                    {
                        SystemActions.RunCommand(command);
                        _window.HideWindow();
                    }
                    break;

                case "get_clipboard_history":
                    await Task.Run(() => Send("clipboard_history_loaded", ClipboardManager.GetHistory()));
                    break;

                case "delete_clipboard_item":
                    if (Str(root, "text") is { } delText)
                    {
                        ClipboardManager.Delete(delText);
                        Send("clipboard_history_loaded", ClipboardManager.GetHistory());
                    }
                    break;

                case "clear_clipboard_history":
                    ClipboardManager.Clear();
                    Send("clipboard_history_loaded", ClipboardManager.GetHistory());
                    break;

                case "paste_clip":
                    if (Str(root, "text") is { } pasteText) SystemActions.PasteClip(pasteText, _window);
                    break;

                case "ask_gemini":
                    if (Str(root, "query") is { Length: > 0 } aiQuery)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var (answer, error) = await GeminiService.AskGemini(aiQuery);
                                Send("gemini_response", new { query = aiQuery, answer, error });
                            }
                            catch (Exception ex)
                            {
                                Send("gemini_response", new { query = aiQuery, answer = (string?)null, error = $"Bridge Error: {ex.Message}" });
                            }
                        });
                    }
                    break;

                case "launch":
                    if (Str(root, "path") is { Length: > 0 } path)
                    {
                        SystemActions.LaunchFile(path, Str(root, "arguments"), Bool(root, "admin"));
                        _window.HideWindow();
                    }
                    break;

                case "show_in_explorer":
                    if (Str(root, "path") is { Length: > 0 } expPath)
                    {
                        SystemActions.ShowInExplorer(expPath);
                        _window.HideWindow();
                    }
                    break;

                case "system":
                    ExecuteSystemCommand(Str(root, "command"));
                    break;

                case "copy_clipboard":
                    if (Str(root, "text") is { } text)
                    {
                        SystemActions.CopyToClipboard(text);
                        _window.HideWindow();
                    }
                    break;

                case "check_update":
                    await _window.CheckForUpdatesAsync(true);
                    break;

                case "install_update":
                    await _window.InstallUpdateAsync();
                    break;

                case "open_settings":
                    SystemActions.LaunchFile(Settings.FilePath);
                    _window.HideWindow();
                    break;

                case "hide":
                    _window.HideWindow();
                    break;

                case "resize_window":
                    _window.Dispatcher.Invoke(() => _window.AdjustHeight(Int(root, "height")));
                    break;
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("Bridge error: " + ex);
        }
    }

    private void ExecuteSystemCommand(string? command)
    {
        switch (command?.ToLowerInvariant())
        {
            case "lock": SystemActions.Lock(); _window.HideWindow(); break;
            case "sleep": SystemActions.Sleep(); _window.HideWindow(); break;
            case "hibernate": SystemActions.Hibernate(); _window.HideWindow(); break;
            case "signout": SystemActions.SignOut(); break;
            case "shutdown": SystemActions.Shutdown(); break;
            case "restart": SystemActions.Restart(); break;
            case "mute": SystemActions.Mute(); break;
            case "volume_up": SystemActions.VolumeUp(); break;
            case "volume_down": SystemActions.VolumeDown(); break;
            case "empty_recycle_bin": SystemActions.EmptyRecycleBin(); _window.HideWindow(); break;
        }
    }

    private void Send(string type, object payload)
    {
        _window.Post(JsonSerializer.Serialize(new { type, payload }));
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int Int(JsonElement root, string name, int fallback = 0) =>
        root.TryGetProperty(name, out var p) && p.TryGetInt32(out var v) ? v : fallback;

    private static bool Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
