# <img src="./spotlight_logo.png" width="48" height="48" align="center" /> Spotlight for Windows

Spotlight is a keyboard-driven search and command launcher for Windows, inspired by macOS Spotlight and Raycast. A native **WPF (.NET 9)** host renders a **React + Vite + TypeScript** UI inside WebView2.

---

## What it does

* **Instant app launcher.** Start Menu, Desktop, Store (UWP) apps and registered executables, with real icons. Type the name, the executable (`code`, `winword`), initials (`vsc`, `wt`), or even a typo (`firefx`).
* **Instant file and folder search.** Files are indexed in memory (no external tools), so results appear as you type, ranked by name match, recency and location. Operators: `ext:pdf invoice`, `type:folder`.
* **Learns from you.** Frequently used items rise, and what you pick for a query is remembered (`ch` → Chrome).
* **Direct answers.** Math (`(5+3)*2`, `sqrt(16)`, `20% of 150`), unit conversion (`10 ft to m`, `72 f to c`, `2.5 gb to mb`), links and paths (`github.com`, `C:\Users`).
* **Clipboard history.** Last 50 text items; Enter pastes into the app you were using.
* **System actions and Windows Settings.** Lock, sleep, restart (asks to confirm), 40 Settings pages with synonyms (`bluetooth`, `vpn`, `dark mode`).
* **Tray icon** with Open / Clipboard / Check for updates / Edit settings / Quit.

## Keyboard

| Shortcut | Action |
| --- | --- |
| `Ctrl + Alt + Space` | Show / hide Spotlight |
| `Ctrl + Alt + C` | Open directly in clipboard history |
| `↑ ↓`, `PgUp PgDn`, `Ctrl+N / Ctrl+P` | Move selection |
| `Enter` | Open / run / paste |
| `Ctrl + Enter` | Reveal in Explorer (files, apps) / copy path |
| `Ctrl + K` | Actions menu (run as administrator, copy path, ...) |
| `Esc` | Clear the query, then close |

> **Why not Alt+Space / Ctrl+Space?** PowerToys Run claims `Alt + Space`, and input methods often swallow `Ctrl + Space`. Spotlight listens with a keyboard hook so its shortcut wins; change it in `settings.json` (`SearchHotkeys`).

## Prefix modes

| Prefix | Mode | Example |
| :---: | --- | --- |
| `<` | Files and folders only (empty = recent files) | `< budget ext:xlsx` |
| `.` | Apps only | `. term` |
| `*` | Clipboard history | `* npm install` |
| `=` | Calculator | `= 2^10` |
| `%%` | Unit converter | `%% 5 km to miles` |
| `$` or `%` | Windows Settings | `$ bluetooth` |
| `>` | Run in Command Prompt | `> ping 8.8.8.8` |
| `:` | Open a registry key | `: HKCU\Software\Microsoft` |
| `!` | Windows Services (Enter toggles) | `! spooler` |
| `!!` | Recently used items | `!!` |
| `#` | SHA-1 / SHA-256 of text | `# hello` |
| `?` | Quick web answer (starts when you pause typing) | `? speed of light` |

## Install and update

Download **`SpotlightSetup.exe`** and run it. It needs no administrator rights: it installs to `%LOCALAPPDATA%\Spotlight`, adds a Start Menu shortcut and a startup entry, and launches Spotlight. (Requires the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) and the Edge WebView2 Runtime, which Windows 11 includes.)

**Updates are a real replace, not an overlay.** Spotlight checks GitHub Releases at startup and every 6 hours; an *Update available* card appears at the top of the launcher. Pressing Enter:

1. downloads the new package (progress shown on the card),
2. waits for Spotlight to exit,
3. extracts the package to a staging folder and verifies it,
4. moves the **entire old version** out of the way and installs the new one (if anything fails, the old version is put back),
5. relaunches Spotlight.

Your data lives in `%LOCALAPPDATA%\Spotlight\data` (settings, clipboard history, icon cache, WebView profile) and is never touched by an update.

## Settings

`%LOCALAPPDATA%\Spotlight\data\settings.json` (tray icon → *Edit settings*; restart Spotlight to apply):

```json
{
  "SearchHotkeys": ["Ctrl+Alt+Space"],
  "ClipboardHotkeys": ["Ctrl+Alt+C"],
  "ExtraIndexRoots": ["D:\\Projects"],
  "ExcludedFolders": ["node_modules", ".git"],
  "IndexDotFolders": false,
  "CheckForUpdates": true
}
```

A log is kept in `data\spotlight.log` (tray icon → *Open log*).

## Building

Prerequisites: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and [Node.js 18+](https://nodejs.org/).

```powershell
.\build.ps1
```

Produces in `dist\`:

* `Spotlight_<version>.zip`: the app package (~1 MB). **Attach this to the GitHub release** (tag `v<version>`); the in-app updater and the installer both download it.
* `SpotlightSetup.exe`: the first-time installer. It downloads the latest release, or installs a `Spotlight_*.zip` placed next to it (offline install).

To release: bump `<Version>` in `src/Spotlight.App/Spotlight.App.csproj` and `src/Spotlight.Setup/Spotlight.Setup.csproj`, run `.\build.ps1`, then publish a GitHub release `v<version>` with the zip attached.

For UI development run `npm run dev` in `src/Spotlight.UI` (a mock host serves sample data in a plain browser) and build the host in Debug, which loads `http://localhost:5173`.

---

> Spotlight keeps clipboard history and settings only on your machine. The only network traffic is the update check against GitHub and the optional `?` web lookup.
