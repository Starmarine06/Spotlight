import type { SystemSearchItem } from "../types";

type Extra = Pick<SystemSearchItem, "keywords" | "confirm">;

const sys = (name: string, command: string, description: string, iconName: string, extra: Extra = {}): SystemSearchItem => ({
  type: "system",
  name,
  path: "",
  command,
  description,
  iconName,
  ...extra,
});

const setting = (name: string, page: string, description: string, keywords: string[], iconName = "settings"): SystemSearchItem =>
  sys(name, `ms-settings:${page}`, description, iconName, { keywords });

export const SYSTEM_ACTIONS: SystemSearchItem[] = [
  sys("Lock Screen", "lock", "Lock the computer screen instantly", "lock", { keywords: ["lock", "secure"] }),
  sys("Sleep", "sleep", "Put the computer to sleep", "sleep", { keywords: ["suspend", "standby"] }),
  sys("Hibernate", "hibernate", "Save the session to disk and power off", "sleep", { keywords: ["hibernation"], confirm: true }),
  sys("Sign Out", "signout", "Sign out of this Windows account", "power", { keywords: ["log off", "logout", "log out", "logoff"], confirm: true }),
  sys("Empty Recycle Bin", "empty_recycle_bin", "Permanently delete everything in the Recycle Bin", "trash", { keywords: ["trash", "bin", "garbage", "delete"], confirm: true }),
  sys("Mute / Unmute Audio", "mute", "Toggle the system audio mute state", "volume_up", { keywords: ["silence", "sound", "audio"] }),
  sys("Volume Up", "volume_up", "Increase system volume", "volume_up", { keywords: ["louder", "sound", "audio"] }),
  sys("Volume Down", "volume_down", "Decrease system volume", "volume_up", { keywords: ["quieter", "sound", "audio"] }),
  sys("Shut Down", "shutdown", "Turn off the computer", "power", { keywords: ["shutdown", "power off", "turn off"], confirm: true }),
  sys("Restart", "restart", "Reboot the computer", "power", { keywords: ["reboot"], confirm: true }),
];

export const WINDOWS_SETTINGS: SystemSearchItem[] = [
  setting("Wi-Fi Settings", "network-wifi", "Manage Wi-Fi networks and connections", ["wifi", "wireless", "internet", "network"], "wifi"),
  setting("Bluetooth & Devices", "bluetooth", "Pair and manage Bluetooth devices", ["bluetooth", "pair", "devices", "mouse", "keyboard"], "bluetooth"),
  setting("Windows Update", "windowsupdate", "Check for updates and view update history", ["update", "upgrade", "patch"]),
  setting("Display Settings", "display", "Brightness, resolution, scaling, multiple screens", ["monitor", "screen", "resolution", "brightness", "scale"]),
  setting("Network & Internet", "network", "Wi-Fi, Ethernet, VPN, data usage", ["internet", "ethernet", "connection"]),
  setting("VPN", "network-vpn", "Add and manage VPN connections", ["vpn", "virtual private network"]),
  setting("Airplane Mode", "network-airplanemode", "Turn wireless communication on or off", ["flight", "wireless"]),
  setting("Mobile Hotspot", "network-mobilehotspot", "Share your internet connection", ["hotspot", "tethering"]),
  setting("Personalization", "personalization", "Background, lock screen, themes, colors", ["theme", "wallpaper", "background", "colors"]),
  setting("Dark Mode / Colors", "colors", "Switch between light and dark mode, accent color", ["dark mode", "light mode", "theme", "accent"]),
  setting("Taskbar Settings", "taskbar", "Customize the taskbar", ["taskbar", "icons"]),
  setting("Apps & Features", "appsfeatures", "Installed apps, uninstall, optional features", ["uninstall", "programs", "remove app"]),
  setting("Default Apps", "defaultapps", "Choose which app opens which file type", ["default browser", "file association", "open with"]),
  setting("Startup Apps", "startupapps", "Choose apps that start at sign-in", ["startup", "boot", "autostart"]),
  setting("Power & Battery", "powersleep", "Screen timeout, sleep, battery saver", ["battery", "power plan", "sleep timeout"]),
  setting("Sound Settings", "sound", "Output and input devices, volume mixer", ["audio", "speaker", "microphone", "volume", "headphones"]),
  setting("Notification Settings", "notifications", "Manage alerts and notification senders", ["alerts", "focus"]),
  setting("Focus", "quiethours", "Focus sessions and do not disturb", ["do not disturb", "quiet hours", "focus assist"]),
  setting("Storage", "storagesense", "Storage usage and temporary files cleanup", ["disk", "space", "cleanup", "storage sense", "free up"]),
  setting("Date & Time", "dateandtime", "Time zone, date and time", ["clock", "timezone", "time zone"]),
  setting("Language & Region", "regionlanguage", "Display language, region, keyboards", ["language", "region", "locale"]),
  setting("Keyboard Settings", "easeofaccess-keyboard", "On-screen keyboard, sticky keys", ["typing", "sticky keys", "on-screen keyboard"]),
  setting("Mouse Settings", "mousetouchpad", "Pointer speed, buttons, scrolling", ["pointer", "cursor", "touchpad", "trackpad", "scroll"]),
  setting("Printers & Scanners", "printers", "Add and manage printers", ["printer", "scanner", "print"]),
  setting("Privacy & Security", "privacy", "Permissions for camera, microphone, location", ["camera", "microphone", "location", "permissions"]),
  setting("Windows Security", "windowsdefender", "Antivirus, firewall and device protection", ["defender", "antivirus", "firewall", "virus", "malware"]),
  setting("Accounts", "yourinfo", "Your account and sign-in options", ["account", "profile", "user", "microsoft account"]),
  setting("Sign-in Options", "signinoptions", "Password, PIN, Windows Hello", ["password", "pin", "fingerprint", "hello"]),
  setting("About This PC", "about", "Device name, specs and Windows version", ["specs", "system info", "version", "computer name", "processor", "ram"]),
  setting("Optional Features", "optionalfeatures", "Add or remove optional Windows features", ["features", "windows features"]),
  setting("Recovery", "recovery", "Reset this PC and recovery options", ["reset", "restore", "reinstall"]),
  setting("Gaming / Game Mode", "gaming-gamemode", "Game Mode, captures, Xbox Game Bar", ["game mode", "xbox", "game bar"]),
  setting("Accessibility", "easeofaccess", "Make Windows easier to see, hear and use", ["narrator", "magnifier", "high contrast", "ease of access"]),
  setting("Clipboard Settings", "clipboard", "Clipboard history and sync", ["clipboard history"]),
  setting("Multitasking", "multitasking", "Snap windows, virtual desktops", ["snap", "virtual desktop"]),
  setting("Night Light", "nightlight", "Warmer display colors at night", ["blue light", "eye strain"]),
  setting("Cameras", "camera", "Manage connected cameras", ["webcam"]),
  setting("Fonts", "fonts", "Install and manage fonts", ["font", "typeface"]),
  setting("Proxy", "network-proxy", "Manual proxy setup", ["proxy server"]),
];
