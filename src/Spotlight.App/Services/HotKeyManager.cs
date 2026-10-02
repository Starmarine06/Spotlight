using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Spotlight.App.Services;

public enum HotKeyKind { Search, Clipboard }

/// <summary>
/// Registers every configured global hotkey independently (the old code only registered Ctrl+Space when
/// Alt+Space was taken, so Ctrl+Space usually did nothing). If Windows refuses a combination because another
/// app or an IME owns it, a low-level keyboard hook takes over for that combination.
/// </summary>
public class HotKeyManager
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private sealed record Binding(string Text, uint Modifiers, uint Vk, HotKeyKind Kind);

    private readonly List<Binding> _hookBindings = new();
    private readonly HashSet<uint> _swallowedKeyUps = new();
    private readonly List<int> _registeredIds = new();
    private readonly Dictionary<int, HotKeyKind> _idToKind = new();
    private LowLevelKeyboardProc? _hookProc; // kept as a field so the GC never collects the delegate
    private IntPtr _hookHandle = IntPtr.Zero;
    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private int _nextId = 9000;

    public event Action<HotKeyKind>? HotKeyPressed;

    /// <summary>Combinations that are live (shown in the tray tooltip).</summary>
    public List<string> ActiveHotkeys { get; } = new();
    public List<string> FailedHotkeys { get; } = new();

    public void Register(Window window)
    {
        _windowHandle = new WindowInteropHelper(window).Handle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(HwndHook);

        foreach (var text in Settings.Current.SearchHotkeys) Bind(text, HotKeyKind.Search);
        foreach (var text in Settings.Current.ClipboardHotkeys) Bind(text, HotKeyKind.Clipboard);

        if (_hookBindings.Count > 0) InstallHook();

        AppPaths.Log($"Hotkeys active: [{string.Join(", ", ActiveHotkeys)}] failed: [{string.Join(", ", FailedHotkeys)}]");
    }

    private void Bind(string text, HotKeyKind kind)
    {
        if (!TryParse(text, out var mods, out var vk))
        {
            FailedHotkeys.Add(text);
            AppPaths.Log($"Could not parse hotkey '{text}'");
            return;
        }

        int id = _nextId++;
        if (RegisterHotKey(_windowHandle, id, mods | MOD_NOREPEAT, vk))
        {
            _registeredIds.Add(id);
            _idToKind[id] = kind;
            ActiveHotkeys.Add(text);
            // Also watch it with the keyboard hook: the hook sees keys before other apps' registrations and
            // input methods do, and swallows the press so the two paths never both fire.
            _hookBindings.Add(new Binding(text, mods, vk, kind));
            return;
        }

        // Someone else (an IME, PowerToys, ...) owns it. Fall back to the keyboard hook, which sees keys first.
        _hookBindings.Add(new Binding(text, mods, vk, kind));
        ActiveHotkeys.Add(text + " (hook)");
        AppPaths.Log($"RegisterHotKey failed for '{text}' (error {Marshal.GetLastWin32Error()}); using keyboard hook");
    }

    public void Unregister()
    {
        foreach (var id in _registeredIds) UnregisterHotKey(_windowHandle, id);
        _registeredIds.Clear();
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _hwndSource?.RemoveHook(HwndHook);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _idToKind.TryGetValue(wParam.ToInt32(), out var kind))
        {
            HotKeyPressed?.Invoke(kind);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void InstallHook()
    {
        _hookProc = HookCallback;
        using var module = Process.GetCurrentProcess().MainModule;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(module?.ModuleName), 0);
        if (_hookHandle == IntPtr.Zero)
            AppPaths.Log("Keyboard hook installation failed: " + Marshal.GetLastWin32Error());
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            uint vk = (uint)Marshal.ReadInt32(lParam);

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                uint mods = CurrentModifiers();
                foreach (var b in _hookBindings)
                {
                    if (b.Vk == vk && b.Modifiers == mods)
                    {
                        _swallowedKeyUps.Add(vk);
                        // Dispatch asynchronously so the hook returns immediately (Windows drops slow hooks).
                        Application.Current.Dispatcher.BeginInvoke(() => HotKeyPressed?.Invoke(b.Kind));
                        return (IntPtr)1;
                    }
                }
            }
            else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && _swallowedKeyUps.Remove(vk))
            {
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static uint CurrentModifiers()
    {
        uint mods = 0;
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) mods |= MOD_CONTROL;
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0) mods |= MOD_ALT;
        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) mods |= MOD_SHIFT;
        if ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0) mods |= MOD_WIN;
        return mods;
    }

    /// <summary>Parses strings like "Ctrl+Space", "Alt+Shift+K", "Win+Alt+F1".</summary>
    public static bool TryParse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (var part in parts.Take(parts.Length - 1))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win": case "windows": case "super": modifiers |= MOD_WIN; break;
                default: return false;
            }
        }

        var keyText = parts[^1];
        if (keyText.Equals("Space", StringComparison.OrdinalIgnoreCase)) { vk = 0x20; return modifiers != 0; }
        if (Enum.TryParse<Key>(keyText, true, out var key) && key != Key.None)
        {
            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return vk != 0 && modifiers != 0;
        }
        return false;
    }
}
