using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Spotlight.App.Services;

public class HotKeyManager
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int HOTKEY_ID_SEARCH = 9000;
    public const int HOTKEY_ID_CLIPBOARD = 9001;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint VK_SPACE = 0x20;
    public const uint VK_C = 0x43;

    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;

    public event Action<int>? HotKeyPressed;

    public void Register(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;

        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(HwndHook);

        // Register Search Hotkey (Alt + Space, fallbacks to Ctrl + Space)
        if (!RegisterHotKey(_windowHandle, HOTKEY_ID_SEARCH, MOD_ALT | MOD_NOREPEAT, VK_SPACE))
        {
            RegisterHotKey(_windowHandle, HOTKEY_ID_SEARCH, MOD_CONTROL | MOD_NOREPEAT, VK_SPACE);
        }

        // Register Clipboard Hotkey (Alt + C, fallbacks to Ctrl + Alt + C)
        if (!RegisterHotKey(_windowHandle, HOTKEY_ID_CLIPBOARD, MOD_ALT | MOD_NOREPEAT, VK_C))
        {
            RegisterHotKey(_windowHandle, HOTKEY_ID_CLIPBOARD, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_C);
        }
    }

    public void Unregister()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HOTKEY_ID_SEARCH);
            UnregisterHotKey(_windowHandle, HOTKEY_ID_CLIPBOARD);
        }
        _hwndSource?.RemoveHook(HwndHook);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ID_SEARCH || id == HOTKEY_ID_CLIPBOARD)
            {
                HotKeyPressed?.Invoke(id);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }
}
