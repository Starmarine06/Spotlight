using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Spotlight.App.Bridge;
using Spotlight.App.Services;
using WinForms = System.Windows.Forms;

namespace Spotlight.App;

public partial class MainWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;

    private readonly HotKeyManager _hotKeys = new();
    private readonly AppSearcher _appSearcher = new();
    private readonly FileIndex _fileIndex = new();
    private readonly WebViewBridge _bridge;
    private readonly DispatcherTimer _maintenanceTimer = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };

    private HwndSource? _hwndSource;
    private TrayIcon? _tray;
    private bool _uiReady;
    private bool _isShown;
    private DateTime _shownAt = DateTime.MinValue;
    private readonly System.Collections.Generic.List<string> _pending = new();
    private UpdateInfo? _pendingUpdate;
    private bool _updating;

    public AppSearcher Apps => _appSearcher;
    public FileIndex Files => _fileIndex;

    public MainWindow()
    {
        InitializeComponent();
        _bridge = new WebViewBridge(this, _appSearcher, _fileIndex);

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        Deactivated += MainWindow_Deactivated;
    }

    // ------------------------------------------------------------------ startup

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
        AddClipboardFormatListener(handle);

        try
        {
            _hotKeys.Register(this);
            _hotKeys.HotKeyPressed += OnHotKey;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Hotkey registration failed: " + ex);
        }

        _tray = new TrayIcon(this, _hotKeys);
        if (_hotKeys.ActiveHotkeys.Count == 0)
            _tray.Balloon("Spotlight", "No global hotkey could be registered. Click the tray icon to open Spotlight, or edit your hotkeys in settings.");

        _fileIndex.Start();
        _appSearcher.AppsUpdated += apps => _bridge.PushApps(apps);
        _ = _appSearcher.RefreshAsync();

        _maintenanceTimer.Tick += (_, _) => _ = _appSearcher.RefreshAsync();
        _maintenanceTimer.Start();

        if (Settings.Current.CheckForUpdates)
        {
            _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(false);
            _updateTimer.Start();
            _ = Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ => Dispatcher.InvokeAsync(() => CheckForUpdatesAsync(false)));
        }

        await InitWebViewAsync();
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.WebViewDir);
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDir);
            await WebView.EnsureCoreWebView2Async(env);

            var core = WebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
#endif
            core.WebMessageReceived += CoreWebView2_WebMessageReceived;
            core.NavigationCompleted += CoreWebView2_NavigationCompleted;
            core.ProcessFailed += CoreWebView2_ProcessFailed;

            core.SetVirtualHostNameToFolderMapping("icons.spotlight.local", AppPaths.IconDir, CoreWebView2HostResourceAccessKind.Allow);

#if DEBUG
            WebView.Source = new Uri("http://localhost:5173");
#else
            var uiFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
            if (Directory.Exists(uiFolder))
            {
                core.SetVirtualHostNameToFolderMapping("spotlight.local", uiFolder, CoreWebView2HostResourceAccessKind.Allow);
                WebView.Source = new Uri("http://spotlight.local/index.html");
            }
            else
            {
                WebView.Source = new Uri("http://localhost:5173");
            }
#endif
        }
        catch (Exception ex)
        {
            AppPaths.Log("WebView2 initialisation failed: " + ex);
            MessageBox.Show(
                "Spotlight could not start its user interface.\n\nThe Microsoft Edge WebView2 Runtime may be missing or damaged.\n\n" + ex.Message,
                "Spotlight", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            AppPaths.Log($"Navigation failed: {e.WebErrorStatus}");
            return;
        }

        if (!_uiReady)
        {
            _uiReady = true;
            // The window was created off-screen so WebView2 could initialise; now it can disappear until summoned.
            if (!_isShown) HideWindow();
            var queued = _pending.ToArray();
            _pending.Clear();
            foreach (var message in queued) Post(message);
        }
    }

    private void CoreWebView2_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        AppPaths.Log($"WebView2 process failed: {e.ProcessFailedKind}");
        Dispatcher.InvokeAsync(() =>
        {
            _uiReady = false;
            try { WebView.Reload(); } catch { _ = InitWebViewAsync(); }
        });
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        RemoveClipboardFormatListener(handle);
        _hwndSource?.RemoveHook(WndProc);
        _hotKeys.Unregister();
        _fileIndex.Dispose();
        _tray?.Dispose();
    }

    // ------------------------------------------------------------------ messages

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;
        if (msg == WM_CLIPBOARDUPDATE)
        {
            try
            {
                if (Clipboard.ContainsText()) ClipboardManager.Add(Clipboard.GetText());
            }
            catch
            {
                // The clipboard may be locked by another process; the next update will retry.
            }
        }
        return IntPtr.Zero;
    }

    private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (sender is CoreWebView2 core)
            await _bridge.HandleMessage(e.TryGetWebMessageAsString(), core);
    }

    /// <summary>Posts a JSON message to the UI; safe to call from any thread.</summary>
    public void Post(string json)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Post(json));
            return;
        }

        if (!_uiReady || WebView.CoreWebView2 == null)
        {
            if (_pending.Count < 50) _pending.Add(json);
            return;
        }

        try { WebView.CoreWebView2.PostWebMessageAsString(json); }
        catch (Exception ex) { AppPaths.Log("PostWebMessage failed: " + ex.Message); }
    }

    // ------------------------------------------------------------------ show / hide

    private void OnHotKey(HotKeyKind kind)
    {
        if (kind == HotKeyKind.Clipboard)
        {
            ShowWindow("clipboard");
            return;
        }

        if (_isShown && IsActive) HideWindow();
        else ShowWindow();
    }

    public void ShowWindow(string mode = "search")
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        _isShown = true;
        _shownAt = DateTime.UtcNow;

        PositionOnActiveMonitor(hwnd);
        Visibility = Visibility.Visible;
        WindowState = WindowState.Normal;
        WindowFocus.ForceForeground(hwnd);
        Activate();
        Focus();
        WebView.Focus();

        var type = mode == "clipboard" ? "show_clipboard" : "window_shown";
        Post(JsonSerializer.Serialize(new { type, payload = new { version = UpdateService.CurrentVersionText } }));

        // Focus can lag a frame behind on first show; make sure the input really has it.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_isShown && !IsActive) WindowFocus.ForceForeground(hwnd);
        });
    }

    public void HideWindow()
    {
        _isShown = false;
        Visibility = Visibility.Collapsed;
        Post("{\"type\":\"window_hidden\"}");
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        // Ignore the focus churn that happens in the first instants after the window is summoned.
        if (_isShown && (DateTime.UtcNow - _shownAt).TotalMilliseconds > 250) HideWindow();
    }

    /// <summary>Centres the launcher near the top of the monitor that currently holds the mouse cursor.</summary>
    private void PositionOnActiveMonitor(IntPtr hwnd)
    {
        try
        {
            var screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position);
            var area = screen.WorkingArea;
            GetWindowRect(hwnd, out var rect);
            int width = rect.Right - rect.Left;
            if (width <= 0)
            {
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                width = (int)(Width * dpi.DpiScaleX);
            }

            int x = area.X + (area.Width - width) / 2;
            int y = area.Y + (int)(area.Height * 0.14);
            SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Positioning failed: " + ex.Message);
        }
    }

    public void AdjustHeight(double height)
    {
        if (height >= 50 && height <= 900) Height = height;
    }

    // ------------------------------------------------------------------ updates

    public void CheckForUpdatesFromTray()
    {
        if (_pendingUpdate != null) _ = InstallUpdateAsync();
        else _ = CheckForUpdatesAsync(true);
    }

    public async Task CheckForUpdatesAsync(bool userInitiated)
    {
        var info = await UpdateService.CheckAsync();
        if (info != null)
        {
            _pendingUpdate = info;
            _tray?.SetUpdateAvailable(info.Version);
            Post(JsonSerializer.Serialize(new
            {
                type = "update_available",
                payload = new { version = info.Version, notes = info.Notes, current = UpdateService.CurrentVersionText }
            }));
            if (userInitiated) _tray?.Balloon("Spotlight update", $"Version {info.Version} is available. Open Spotlight and press Enter on the update card, or click this tray entry again to install.");
        }
        else if (userInitiated)
        {
            _tray?.Balloon("Spotlight", $"You're on the latest version ({UpdateService.CurrentVersionText}).");
        }
    }

    public async Task InstallUpdateAsync()
    {
        if (_pendingUpdate == null || _updating) return;
        _updating = true;

        var info = _pendingUpdate;
        var error = await UpdateService.DownloadAndInstallAsync(info, percent =>
            Post(JsonSerializer.Serialize(new { type = "update_progress", payload = new { percent } })));

        if (error == null)
        {
            // The updater waits for this process to exit, replaces the install folder and relaunches.
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            return;
        }

        _updating = false;
        Post(JsonSerializer.Serialize(new { type = "update_error", payload = new { message = error } }));
    }
}
