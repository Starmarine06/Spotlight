using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Spotlight.App.Services;

namespace Spotlight.App.Bridge;

public class WebViewBridge
{
    private readonly MainWindow _mainWindow;
    private readonly AppSearcher _appSearcher;
    private readonly FileSearcher _fileSearcher;

    public WebViewBridge(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _appSearcher = new AppSearcher();
        _fileSearcher = new FileSearcher();
    }

    public async Task HandleMessage(string rawMessage, CoreWebView2 sender)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;

            var type = typeProp.GetString();
            switch (type)
            {
                case "init":
                    await Task.Run(() =>
                    {
                        var apps = _appSearcher.GetApps();
                        SendResponse(sender, "apps_loaded", apps);
                    });
                    break;

                case "refresh_apps":
                    await Task.Run(() =>
                    {
                        _appSearcher.RefreshCache();
                        var apps = _appSearcher.GetApps();
                        SendResponse(sender, "apps_loaded", apps);
                    });
                    break;

                case "search_files":
                    if (root.TryGetProperty("query", out var queryProp))
                    {
                        var query = queryProp.GetString() ?? string.Empty;
                        await Task.Run(() =>
                        {
                            var files = _fileSearcher.SearchFiles(query);
                            SendResponse(sender, "files_results", new { query, files });
                        });
                    }
                    break;

                case "get_services":
                    await Task.Run(() =>
                    {
                        var services = SystemActions.GetServices();
                        SendResponse(sender, "services_loaded", services);
                    });
                    break;

                case "control_service":
                    if (root.TryGetProperty("serviceName", out var svcNameProp) &&
                        root.TryGetProperty("action", out var actionProp))
                    {
                        var serviceName = svcNameProp.GetString();
                        var action = actionProp.GetString();
                        if (!string.IsNullOrEmpty(serviceName) && !string.IsNullOrEmpty(action))
                        {
                            await Task.Run(() => SystemActions.ControlService(serviceName, action));
                            _mainWindow.HideWindow();
                        }
                    }
                    break;

                case "open_registry":
                    if (root.TryGetProperty("path", out var regPathProp))
                    {
                        var path = regPathProp.GetString();
                        if (!string.IsNullOrEmpty(path))
                        {
                            SystemActions.OpenRegistryKey(path);
                            _mainWindow.HideWindow();
                        }
                    }
                    break;

                case "run_cmd":
                    if (root.TryGetProperty("command", out var cmdProp))
                    {
                        var command = cmdProp.GetString();
                        if (!string.IsNullOrEmpty(command))
                        {
                            SystemActions.RunCommand(command);
                            _mainWindow.HideWindow();
                        }
                    }
                    break;

                case "get_clipboard_history":
                    await Task.Run(() =>
                    {
                        var history = ClipboardManager.GetHistory();
                        SendResponse(sender, "clipboard_history_loaded", history);
                    });
                    break;

                case "delete_clipboard_item":
                    if (root.TryGetProperty("text", out var delTextProp))
                    {
                        var text = delTextProp.GetString();
                        if (text != null)
                        {
                            ClipboardManager.Delete(text);
                            var history = ClipboardManager.GetHistory();
                            SendResponse(sender, "clipboard_history_loaded", history);
                        }
                    }
                    break;

                case "clear_clipboard_history":
                    ClipboardManager.Clear();
                    SendResponse(sender, "clipboard_history_loaded", ClipboardManager.GetHistory());
                    break;

                case "paste_clip":
                    if (root.TryGetProperty("text", out var pasteTextProp))
                    {
                        var text = pasteTextProp.GetString();
                        if (text != null)
                        {
                            SystemActions.PasteClip(text, _mainWindow);
                        }
                    }
                    break;

                case "ask_gemini":
                    if (root.TryGetProperty("query", out var aiQueryProp))
                    {
                        var query = aiQueryProp.GetString();
                        if (!string.IsNullOrEmpty(query))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var (answer, error) = await GeminiService.AskGemini(query);
                                    SendResponse(sender, "gemini_response", new { query, answer, error });
                                }
                                catch (Exception ex)
                                {
                                    SendResponse(sender, "gemini_response", new { query, answer = (string?)null, error = $"Bridge Error: {ex.Message}" });
                                }
                            });
                        }
                    }
                    break;

                case "launch":
                    if (root.TryGetProperty("path", out var pathProp))
                    {
                        var path = pathProp.GetString();
                        string? arguments = null;
                        if (root.TryGetProperty("arguments", out var argsProp))
                        {
                            arguments = argsProp.GetString();
                        }

                        if (!string.IsNullOrEmpty(path))
                        {
                            SystemActions.LaunchFile(path, arguments);
                            _mainWindow.HideWindow();
                        }
                    }
                    break;

                case "show_in_explorer":
                    if (root.TryGetProperty("path", out var expPathProp))
                    {
                        var path = expPathProp.GetString();
                        if (!string.IsNullOrEmpty(path))
                        {
                            SystemActions.ShowInExplorer(path);
                            _mainWindow.HideWindow();
                        }
                    }
                    break;

                case "system":
                    if (root.TryGetProperty("command", out var sysCmdProp))
                    {
                        var command = sysCmdProp.GetString();
                        ExecuteSystemCommand(command);
                    }
                    break;

                case "copy_clipboard":
                    if (root.TryGetProperty("text", out var textProp))
                    {
                        var text = textProp.GetString();
                        if (text != null)
                        {
                            SystemActions.CopyToClipboard(text);
                            _mainWindow.HideWindow();
                        }
                    }
                    break;
                case "trigger_update":
                    if (root.TryGetProperty("downloadUrl", out var dlUrlProp))
                    {
                        var downloadUrl = dlUrlProp.GetString();
                        if (!string.IsNullOrEmpty(downloadUrl))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var tempZip = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpotlightUpdate.zip");
                                    if (System.IO.File.Exists(tempZip))
                                    {
                                        System.IO.File.Delete(tempZip);
                                    }

                                    using var client = new System.Net.Http.HttpClient();
                                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Spotlight-App");
                                    
                                    using var response = await client.GetAsync(downloadUrl);
                                    response.EnsureSuccessStatusCode();
                                    
                                    using (var fs = new System.IO.FileStream(tempZip, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                                    {
                                        await response.Content.CopyToAsync(fs);
                                    }

                                    var currentBase = AppDomain.CurrentDomain.BaseDirectory;
                                    var setupExe = System.IO.Path.Combine(currentBase, "Spotlight.Setup.exe");
                                    if (System.IO.File.Exists(setupExe))
                                    {
                                        var psi = new System.Diagnostics.ProcessStartInfo(setupExe, $"update \"{tempZip}\"")
                                        {
                                            UseShellExecute = true,
                                            WorkingDirectory = currentBase
                                        };
                                        System.Diagnostics.Process.Start(psi);
                                        
                                        _mainWindow.Dispatcher.Invoke(() =>
                                        {
                                            System.Windows.Application.Current.Shutdown();
                                        });
                                    }
                                    else
                                    {
                                        System.Windows.MessageBox.Show("Spotlight.Setup.exe updater was not found. Please install the update manually.", "Update Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Windows.MessageBox.Show($"Update download failed: {ex.Message}", "Update Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                                }
                            });
                        }
                    }
                    break;

                case "hide":
                    _mainWindow.HideWindow();
                    break;

                case "resize_window":
                    if (root.TryGetProperty("height", out var heightProp))
                    {
                        double height = heightProp.GetDouble();
                        _mainWindow.Dispatcher.Invoke(() => _mainWindow.AdjustHeight(height));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Bridge error: {ex.Message}");
        }
    }

    private void ExecuteSystemCommand(string? command)
    {
        if (string.IsNullOrEmpty(command)) return;

        switch (command.ToLower())
        {
            case "lock":
                SystemActions.Lock();
                _mainWindow.HideWindow();
                break;
            case "sleep":
                SystemActions.Sleep();
                _mainWindow.HideWindow();
                break;
            case "shutdown":
                SystemActions.Shutdown();
                break;
            case "restart":
                SystemActions.Restart();
                break;
            case "mute":
                SystemActions.Mute();
                break;
            case "volume_up":
                SystemActions.VolumeUp();
                break;
            case "volume_down":
                SystemActions.VolumeDown();
                break;
            case "empty_recycle_bin":
                SystemActions.EmptyRecycleBin();
                _mainWindow.HideWindow();
                break;
        }
    }

    private void SendResponse(CoreWebView2 sender, string type, object data)
    {
        var jsonResponse = JsonSerializer.Serialize(new
        {
            type = type,
            payload = data
        });

        _mainWindow.Dispatcher.Invoke(() =>
        {
            try
            {
                sender.PostWebMessageAsString(jsonResponse);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to send response: {ex.Message}");
            }
        });
    }
}
