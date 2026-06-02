using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Spotlight.App.Bridge;
using Spotlight.App.Services;

namespace Spotlight.App;

public partial class MainWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private readonly HotKeyManager _hotKeyManager;
    private readonly WebViewBridge _webViewBridge;
    private HwndSource? _mainWindowHwndSource;

    public MainWindow()
    {
        InitializeComponent();
        _hotKeyManager = new HotKeyManager();
        _webViewBridge = new WebViewBridge(this);

        this.Loaded += MainWindow_Loaded;
        this.Unloaded += MainWindow_Unloaded;
        this.Deactivated += MainWindow_Deactivated;
        this.Activated += MainWindow_Activated;
        
        PositionWindow();
    }

    private void PositionWindow()
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        this.Left = (screenWidth - this.Width) / 2;
        this.Top = screenHeight * 0.15; // 15% from the top
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;

        // Register window message hook for clipboard listener
        _mainWindowHwndSource = HwndSource.FromHwnd(handle);
        _mainWindowHwndSource?.AddHook(MainWindow_HwndHook);
        AddClipboardFormatListener(handle);

        // Setup global hotkeys
        try
        {
            _hotKeyManager.Register(this);
            _hotKeyManager.HotKeyPressed += ToggleWindow;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to register hotkey: {ex.Message}", "Spotlight Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Initialize WebView2
        await WebView.EnsureCoreWebView2Async();
        
        // Register WebMessageReceived handler
        WebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

        // Set source based on build configuration
#if DEBUG
        WebView.Source = new Uri("http://localhost:5173");
#else
        var uiFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
        if (Directory.Exists(uiFolder))
        {
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping("spotlight.local", uiFolder, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            WebView.Source = new Uri("http://spotlight.local/index.html");
        }
        else
        {
            WebView.Source = new Uri("http://localhost:5173"); // Fallback
        }
#endif
    }

    private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        RemoveClipboardFormatListener(handle);
        _mainWindowHwndSource?.RemoveHook(MainWindow_HwndHook);
        _hotKeyManager.Unregister();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        HideWindow();
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        WebView.Focus();
        // Inform frontend that the window was shown
        WebView.CoreWebView2?.PostWebMessageAsString("{\"type\":\"window_shown\"}");
    }

    private IntPtr MainWindow_HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;
        if (msg == WM_CLIPBOARDUPDATE)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    ClipboardManager.Add(text);
                }
            }
            catch
            {
                // Clipboard query might fail if locked by another app
            }
        }
        return IntPtr.Zero;
    }

    private async void CoreWebView2_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        var rawMessage = e.TryGetWebMessageAsString();
        if (sender != null)
        {
            await _webViewBridge.HandleMessage(rawMessage, (Microsoft.Web.WebView2.Core.CoreWebView2)sender);
        }
    }

    public void ToggleWindow(int hotkeyId)
    {
        if (this.Visibility == Visibility.Visible && this.IsActive)
        {
            HideWindow();
        }
        else
        {
            ShowWindow(hotkeyId == HotKeyManager.HOTKEY_ID_CLIPBOARD ? "clipboard" : "search");
        }
    }

    public void ShowWindow(string mode = "search")
    {
        this.Visibility = Visibility.Visible;
        this.WindowState = WindowState.Normal;
        this.Activate();
        WebView.Focus();

        if (mode == "clipboard")
        {
            WebView.CoreWebView2?.PostWebMessageAsString("{\"type\":\"show_clipboard\"}");
        }
        else
        {
            WebView.CoreWebView2?.PostWebMessageAsString("{\"type\":\"window_shown\"}");
        }
    }

    public void HideWindow()
    {
        this.Visibility = Visibility.Collapsed;
        // Inform frontend that window is hidden
        WebView.CoreWebView2?.PostWebMessageAsString("{\"type\":\"window_hidden\"}");
    }

    public void AdjustHeight(double height)
    {
        if (height >= 50 && height <= 800)
        {
            this.Height = height;
        }
    }
}