import { useState, useEffect, useMemo, useRef } from "react";
import { SearchBar } from "./components/SearchBar";
import { ResultList } from "./components/ResultList";
import { PreviewPane } from "./components/PreviewPane";
import { ActionMenu } from "./components/ActionMenu";
import type { SearchItem, AppSearchItem, FileSearchItem, SystemSearchItem, CalcSearchItem, ServiceSearchItem, RegistrySearchItem, CommandSearchItem, ClipSearchItem } from "./types";
import { useKeyboard } from "./hooks/useKeyboard";
import { bridge } from "./bridge";
import { convertUnits, calculateSHA256, calculateSHA1 } from "./utils/prefixHelpers";
import { scoreAndRankApps } from "./utils/fuzzySearch";

const DEFAULT_SYSTEM_ACTIONS: SystemSearchItem[] = [
  { type: "system", name: "Lock Screen", path: "", command: "lock", description: "Lock the computer screen instantly", iconName: "lock" },
  { type: "system", name: "Sleep", path: "", command: "sleep", description: "Put the computer to sleep mode", iconName: "sleep" },
  { type: "system", name: "Empty Recycle Bin", path: "", command: "empty_recycle_bin", description: "Empty Windows Recycle Bin", iconName: "trash" },
  { type: "system", name: "Mute/Unmute Audio", path: "", command: "mute", description: "Toggle mute state of system audio", iconName: "power" },
  { type: "system", name: "Volume Up", path: "", command: "volume_up", description: "Increase system volume", iconName: "volume_up" },
  { type: "system", name: "Volume Down", path: "", command: "volume_down", description: "Decrease system volume", iconName: "volume_up" },
  { type: "system", name: "Shutdown", path: "", command: "shutdown", description: "Safely turn off the computer", iconName: "power" },
  { type: "system", name: "Restart", path: "", command: "restart", description: "Reboot the computer", iconName: "power" }
];

const WINDOWS_SETTINGS: SystemSearchItem[] = [
  { type: "system", name: "Wi-Fi Settings", path: "ms-settings:network-wifi", command: "ms-settings:network-wifi", description: "Manage Wi-Fi networks and connection settings", iconName: "wifi" },
  { type: "system", name: "Bluetooth Settings", path: "ms-settings:bluetooth", command: "ms-settings:bluetooth", description: "Manage Bluetooth devices, pairing, and discovery", iconName: "bluetooth" },
  { type: "system", name: "Windows Update", path: "ms-settings:windowsupdate", command: "ms-settings:windowsupdate", description: "Check for updates, view update history", iconName: "settings" },
  { type: "system", name: "Display Settings", path: "ms-settings:display", command: "ms-settings:display", description: "Brightness, resolution, multiple screens", iconName: "settings" },
  { type: "system", name: "Network & Internet", path: "ms-settings:network", command: "ms-settings:network", description: "Wi-Fi, Ethernet, VPN, data usage", iconName: "settings" },
  { type: "system", name: "Personalization", path: "ms-settings:personalization", command: "ms-settings:personalization", description: "Background, lock screen, themes, colors", iconName: "settings" },
  { type: "system", name: "Apps & Features", path: "ms-settings:appsfeatures", command: "ms-settings:appsfeatures", description: "Uninstall apps, default apps, optional features", iconName: "settings" },
  { type: "system", name: "Power & Sleep", path: "ms-settings:power", command: "ms-settings:power", description: "Screen timeout, sleep settings, battery", iconName: "settings" },
  { type: "system", name: "Sound Settings", path: "ms-settings:sound", command: "ms-settings:sound", description: "Output devices, input devices, volume mixer", iconName: "settings" },
  { type: "system", name: "Notification Settings", path: "ms-settings:notifications", command: "ms-settings:notifications", description: "Manage alerts, notifications, and sender options", iconName: "settings" },
  { type: "system", name: "Storage Settings", path: "ms-settings:storagesense", command: "ms-settings:storagesense", description: "Check storage usage, clean temporary files", iconName: "settings" },
  { type: "system", name: "Date & Time Settings", path: "ms-settings:dateandtime", command: "ms-settings:dateandtime", description: "Adjust date, time zone, and language settings", iconName: "settings" }
];

const CURRENT_VERSION = "1.2.0";

export default function App() {
  const [searchQuery, setSearchQuery] = useState("");
  const [updateInfo, setUpdateInfo] = useState<{ version: string; downloadUrl: string; notes: string } | null>(null);
  const [apps, setApps] = useState<AppSearchItem[]>([]);
  const [fileResults, setFileResults] = useState<FileSearchItem[]>([]);
  const [services, setServices] = useState<ServiceSearchItem[]>([]);
  const [hashes, setHashes] = useState<{ sha256: string; sha1: string }>({ sha256: "", sha1: "" });
  const [clipboardHistory, setClipboardHistory] = useState<ClipSearchItem[]>([]);
  const [isActionsOpen, setIsActionsOpen] = useState(false);
  const [historyList, setHistoryList] = useState<SearchItem[]>([]);
  
  const [hasLoadedServices, setHasLoadedServices] = useState(false);
  const [hasLoadedClipboard, setHasLoadedClipboard] = useState(false);

  // Google AI state
  const [aiAnswer, setAiAnswer] = useState<string | undefined>(undefined);
  const [aiLoading, setAiLoading] = useState(false);
  const [aiError, setAiError] = useState<string | undefined>(undefined);

  // Keep a mutable ref of the search query for event listener callbacks to prevent state-dependency re-runs
  const queryRef = useRef(searchQuery);
  useEffect(() => {
    queryRef.current = searchQuery;
  }, [searchQuery]);

  // Load history from local storage
  const refreshHistory = () => {
    try {
      const historyJson = localStorage.getItem("spotlight_history") || "[]";
      setHistoryList(JSON.parse(historyJson));
    } catch {
      setHistoryList([]);
    }
  };

  // Setup mount-only event listeners to prevent duplicate listener accumulation and performance bottlenecks
  useEffect(() => {
    const handleAppsLoaded = (payload: any) => {
      const formattedApps = payload.map((app: any) => ({
        type: "app",
        name: app.Name,
        path: app.TargetPath,
        arguments: app.Arguments,
        icon: app.IconBase64,
      }));
      setApps(formattedApps);
    };

    const handleFilesResults = (payload: any) => {
      const { query, files } = payload;
      const currentQuery = queryRef.current.trim();
      const actualFileQuery = currentQuery.startsWith("<") ? currentQuery.slice(1).trim() : currentQuery;
      
      // Verify query relevance to ignore stale async file search results
      if (query.toLowerCase().trim() === actualFileQuery.toLowerCase().trim()) {
        const formattedFiles = files.map((file: any) => ({
          type: "file",
          name: file.Name,
          path: file.Path,
          size: file.Size,
          dateModified: file.DateModified,
          extension: file.Extension,
        }));
        setFileResults(formattedFiles);
      }
    };

    const handleServicesLoaded = (payload: any) => {
      const formattedServices = payload.map((svc: any) => ({
        type: "service",
        name: svc.DisplayName,
        path: svc.Status,
        serviceName: svc.Name,
        displayName: svc.DisplayName,
        status: svc.Status,
      }));
      setServices(formattedServices);
    };

    const handleClipboardLoaded = (payload: any) => {
      const formattedClips = payload.map((c: any) => ({
        type: "clip",
        name: c.FullText.length > 55 ? c.FullText.slice(0, 55).replace(/\r?\n/g, " ") + "..." : c.FullText.replace(/\r?\n/g, " "),
        path: c.Timestamp,
        fullText: c.FullText,
        timestamp: c.Timestamp
      }));
      setClipboardHistory(formattedClips);
    };

    const handleGeminiResponse = (payload: any) => {
      const { query: respQuery, answer: respAnswer, error: respError } = payload;
      const currentQuery = queryRef.current.trim();
      if (currentQuery.startsWith("?")) {
        const actualAIQuery = currentQuery.slice(1).trim();
        if (actualAIQuery.toLowerCase() === respQuery.toLowerCase()) {
          setAiLoading(false);
          if (respError) {
            setAiError(respError);
          } else {
            setAiAnswer(respAnswer);
          }
        }
      }
    };

    const handleWindowShown = () => {
      setSearchQuery("");
      setFileResults([]);
      setIsActionsOpen(false);
      refreshHistory();
      window.dispatchEvent(new CustomEvent("window-shown"));
    };

    const handleShowClipboard = () => {
      setSearchQuery("*");
      setFileResults([]);
      setIsActionsOpen(false);
      window.dispatchEvent(new CustomEvent("window-shown"));
    };

    bridge.on("apps_loaded", handleAppsLoaded);
    bridge.on("files_results", handleFilesResults);
    bridge.on("services_loaded", handleServicesLoaded);
    bridge.on("clipboard_history_loaded", handleClipboardLoaded);
    bridge.on("gemini_response", handleGeminiResponse);
    bridge.on("window_shown", handleWindowShown);
    bridge.on("show_clipboard", handleShowClipboard);

    // Run initial Start Menu app scan exactly once on startup
    bridge.send("init");
    refreshHistory();

    // Check for latest updates on GitHub Releases
    fetch("https://api.github.com/repos/Starmarine06/Spotlight/releases/latest")
      .then((res) => {
        if (!res.ok) throw new Error("Failed to fetch release");
        return res.json();
      })
      .then((data) => {
        const latestTag = data.tag_name;
        if (latestTag) {
          const cleanLatest = latestTag.replace(/^v/, "");
          const cleanCurrent = CURRENT_VERSION.replace(/^v/, "");
          if (cleanLatest !== cleanCurrent) {
            const zipAsset = data.assets?.find((asset: any) =>
              asset.name.endsWith(".zip")
            );
            if (zipAsset) {
              setUpdateInfo({
                version: latestTag,
                downloadUrl: zipAsset.browser_download_url,
                notes: data.body || "",
              });
            }
          }
        }
      })
      .catch((err) => console.error("Update check failed:", err));

    return () => {
      bridge.off("apps_loaded", handleAppsLoaded);
      bridge.off("files_results", handleFilesResults);
      bridge.off("services_loaded", handleServicesLoaded);
      bridge.off("clipboard_history_loaded", handleClipboardLoaded);
      bridge.off("gemini_response", handleGeminiResponse);
      bridge.off("window_shown", handleWindowShown);
      bridge.off("show_clipboard", handleShowClipboard);
    };
  }, []);

  // Debounced file search
  useEffect(() => {
    const trimmed = searchQuery.trim();
    
    // Determine the actual file query based on prefix
    let fileSearchQuery = "";
    if (trimmed.startsWith("<")) {
      fileSearchQuery = trimmed.slice(1).trim();
    } else if (
      !trimmed.startsWith(".") &&
      !trimmed.startsWith("!") &&
      !trimmed.startsWith(">") &&
      !trimmed.startsWith(":") &&
      !trimmed.startsWith("#") &&
      !trimmed.startsWith("%") &&
      !trimmed.startsWith("=") &&
      !trimmed.startsWith("*") &&
      !trimmed.startsWith("?")
    ) {
      fileSearchQuery = trimmed;
    }

    if (fileSearchQuery.length >= 2) {
      const handler = setTimeout(() => {
        bridge.send("search_files", { query: fileSearchQuery });
      }, 150);
      return () => clearTimeout(handler);
    } else {
      setFileResults([]);
    }
  }, [searchQuery]);

  // Reset Gemini AI question search state on query input change
  useEffect(() => {
    setAiAnswer(undefined);
    setAiLoading(false);
    setAiError(undefined);
  }, [searchQuery]);

  // Query Windows Services list exactly once when entering service mode ("!")
  useEffect(() => {
    if (searchQuery.startsWith("!")) {
      if (!hasLoadedServices) {
        bridge.send("get_services");
        setHasLoadedServices(true);
      }
    } else {
      setHasLoadedServices(false);
    }
  }, [searchQuery, hasLoadedServices]);

  // Fetch persisted Clipboard list exactly once when entering clipboard mode ("*")
  useEffect(() => {
    if (searchQuery.startsWith("*")) {
      if (!hasLoadedClipboard) {
        bridge.send("get_clipboard_history");
        setHasLoadedClipboard(true);
      }
    } else {
      setHasLoadedClipboard(false);
    }
  }, [searchQuery, hasLoadedClipboard]);

  // Compute text hashes asynchronously
  useEffect(() => {
    if (searchQuery.startsWith("#")) {
      const text = searchQuery.slice(1).trim();
      if (text) {
        Promise.all([calculateSHA256(text), calculateSHA1(text)]).then(([s256, s1]) => {
          setHashes({ sha256: s256, sha1: s1 });
        });
      } else {
        setHashes({ sha256: "", sha1: "" });
      }
    }
  }, [searchQuery]);

  // Filter and construct result set
  const results = useMemo(() => {
    const query = searchQuery.trim();
    const list: SearchItem[] = [];
    const queryLower = query.toLowerCase();

    // Prepend update card if an update is available and query is empty or relates to updating
    if (updateInfo && (!query || "update".includes(queryLower) || "version".includes(queryLower))) {
      list.push({
        type: "system",
        name: `✨ Update Available: ${updateInfo.version}`,
        path: "update-spotlight",
        command: `update-spotlight:${updateInfo.downloadUrl}`,
        description: `Install version ${updateInfo.version}. Press Enter to update now.`,
        iconName: "settings"
      });
    }

    if (!query) {
      list.push(...apps.slice(0, 5));
      list.push(...DEFAULT_SYSTEM_ACTIONS.slice(0, 4));
      return list;
    }

    // 0. AI Mode ("?")
    if (query.startsWith("?")) {
      const aiQuery = query.slice(1).trim();
      return [
        {
          type: "ai" as const,
          name: aiQuery ? `Search Web: "${aiQuery}"` : "Search the web...",
          path: "",
          query: aiQuery,
          answer: aiAnswer,
          loading: aiLoading,
          error: aiError,
        }
      ];
    }

    // 1. History mode ("!!")
    if (query === "!!" || query.startsWith("!!")) {
      return historyList;
    }

    // 2. Clipboard mode ("*")
    if (query.startsWith("*")) {
      const clipQuery = query.slice(1).trim().toLowerCase();
      if (!clipQuery) return clipboardHistory;
      return clipboardHistory.filter((c) =>
        c.fullText.toLowerCase().includes(clipQuery)
      );
    }

    // 3. Command runner mode (">")
    if (query.startsWith(">")) {
      const commandText = query.slice(1).trim();
      const cmdItem: CommandSearchItem = {
        type: "cmd",
        name: `Execute command: "${commandText || "ipconfig"}"`,
        path: "cmd.exe",
        command: commandText,
      };
      return [cmdItem];
    }

    // 4. Registry mode (":")
    if (query.startsWith(":")) {
      const regPath = query.slice(1).trim();
      const regItem: RegistrySearchItem = {
        type: "registry",
        name: `Open Registry Key: "${regPath || "HKCU\\Software"}"`,
        path: "Registry Editor (regedit)",
        registryPath: regPath,
      };
      return [regItem];
    }

    // 5. Calculator / Math mode ("=")
    if (query.startsWith("=")) {
      const expr = query.slice(1).trim();
      if (!expr) {
        return [{
          type: "calc" as const,
          name: "Type a mathematical equation",
          path: "",
          expression: "e.g. 5*3-2",
          result: "0",
        }];
      }
      try {
        const sanitized = expr.replace(/\^/g, "**");
        const res = new Function(`return (${sanitized})`)();
        if (typeof res === "number" && !isNaN(res) && isFinite(res)) {
          return [{
            type: "calc" as const,
            name: res.toString(),
            path: "",
            expression: expr,
            result: res.toString(),
          }];
        }
      } catch {
        // Ignore math exceptions while typing
      }
      return [{
        type: "calc" as const,
        name: "Evaluating...",
        path: "",
        expression: expr,
        result: "Error",
      }];
    }

    // 6. Services mode ("!")
    if (query.startsWith("!")) {
      const svcQuery = query.slice(1).trim().toLowerCase();
      return services.filter((svc) =>
        svc.displayName.toLowerCase().includes(svcQuery) || svc.serviceName.toLowerCase().includes(svcQuery)
      );
    }

    // 7. Settings mode ("%")
    if (query.startsWith("%") && !query.startsWith("%%")) {
      const settingsQuery = query.slice(1).trim().toLowerCase();
      return WINDOWS_SETTINGS.filter((set) =>
        set.name.toLowerCase().includes(settingsQuery) || set.description.toLowerCase().includes(settingsQuery)
      );
    }

    // 8. Hashes mode ("#")
    if (query.startsWith("#")) {
      const text = query.slice(1).trim();
      const sha256Item: CalcSearchItem = {
        type: "calc",
        name: hashes.sha256,
        path: "",
        expression: `SHA-256 Hash of "${text}"`,
        result: hashes.sha256,
      };
      const sha1Item: CalcSearchItem = {
        type: "calc",
        name: hashes.sha1,
        path: "",
        expression: `SHA-1 Hash of "${text}"`,
        result: hashes.sha1,
      };
      return [sha256Item, sha1Item];
    }

    // 9. Unit conversion mode ("%%")
    if (query.startsWith("%%")) {
      const convTerm = query.slice(2).trim();
      const convResult = convertUnits(convTerm);
      if (convResult) {
        return [{
          type: "conversion" as const,
          name: convResult,
          path: "",
          result: convResult,
        }];
      } else {
        return [{
          type: "conversion" as const,
          name: `Example: 10 ft to m, 32 f to c`,
          path: "",
          result: "No conversion match found",
        }];
      }
    }

    // 10. Files filter ("<")
    if (query.startsWith("<")) {
      return fileResults;
    }

    // 11. Apps filter (".")
    if (query.startsWith(".")) {
      const appQuery = query.slice(1).trim();
      return scoreAndRankApps(apps, appQuery);
    }

    // NORMAL MODE: Combined search

    // Math evaluation
    const isMath = /^[0-9+\-*/().\s^%]+$/.test(query) && /[0-9]/.test(query);
    if (isMath) {
      try {
        const sanitized = query.replace(/\^/g, "**");
        const res = new Function(`return (${sanitized})`)();
        if (typeof res === "number" && !isNaN(res) && isFinite(res)) {
          list.push({
            type: "calc",
            name: res.toString(),
            path: "",
            expression: query,
            result: res.toString(),
          });
        }
      } catch {
        // Skip
      }
    }

    // Apps filter with fuzzy ranking
    const rankedApps = scoreAndRankApps(apps, query);
    list.push(...rankedApps);

    // Files
    list.push(...fileResults);

    // System commands & Windows Settings
    const filteredSys = DEFAULT_SYSTEM_ACTIONS.filter((sys) =>
      sys.name.toLowerCase().includes(queryLower) || sys.description.toLowerCase().includes(queryLower)
    );
    list.push(...filteredSys);

    const filteredSettings = WINDOWS_SETTINGS.filter((set) =>
      set.name.toLowerCase().includes(queryLower) || set.description.toLowerCase().includes(queryLower)
    );
    list.push(...filteredSettings);

    // Web Search Options
    list.push(
      {
        type: "web" as const,
        name: `Search Google for "${query}"`,
        path: "",
        query: query,
        engine: "google" as const,
      },
      {
        type: "web" as const,
        name: `Search DuckDuckGo for "${query}"`,
        path: "",
        query: query,
        engine: "duckduckgo" as const,
      }
    );

    return list;
  }, [searchQuery, apps, fileResults, services, hashes, clipboardHistory, historyList, aiAnswer, aiLoading, aiError, updateInfo]);

  // Save selection history to localStorage
  const saveToHistory = (item: SearchItem) => {
    if (["app", "file", "system", "registry", "cmd", "service"].includes(item.type)) {
      try {
        const historyJson = localStorage.getItem("spotlight_history") || "[]";
        let list: SearchItem[] = JSON.parse(historyJson);
        list = list.filter((i) => i.name !== item.name);
        list.unshift(item);
        localStorage.setItem("spotlight_history", JSON.stringify(list.slice(0, 10)));
      } catch (e) {
        console.error(e);
      }
    }
  };

  // Execute selected item actions
  const executeItem = (item: SearchItem, actionOverride?: string) => {
    if (!item) return;

    const action = actionOverride || "open";

    saveToHistory(item);

    if (action === "copy_path" && item.path) {
      bridge.send("copy_clipboard", { text: item.path });
      return;
    }

    if (item.type === "app") {
      bridge.send("launch", { path: item.path, arguments: item.arguments });
    } else if (item.type === "file") {
      if (action === "show_explorer") {
        bridge.send("show_in_explorer", { path: item.path });
      } else {
        bridge.send("launch", { path: item.path });
      }
    } else if (item.type === "system") {
      if (item.command.startsWith("update-spotlight:")) {
        const downloadUrl = item.command.replace("update-spotlight:", "");
        bridge.send("trigger_update", { downloadUrl });
      } else if (item.command.startsWith("ms-settings:")) {
        bridge.send("launch", { path: item.command });
      } else {
        bridge.send("system", { command: item.command });
      }
    } else if (item.type === "calc") {
      bridge.send("copy_clipboard", { text: item.result });
    } else if (item.type === "web") {
      const encoded = encodeURIComponent(item.query);
      const url =
        item.engine === "google"
          ? `https://www.google.com/search?q=${encoded}`
          : `https://duckduckgo.com/?q=${encoded}`;
      bridge.send("launch", { path: url });
    } else if (item.type === "cmd") {
      bridge.send("run_cmd", { command: item.command || "cmd.exe" });
    } else if (item.type === "registry") {
      bridge.send("open_registry", { path: item.registryPath || "HKCU\\Software" });
    } else if (item.type === "service") {
      const serviceAction = actionOverride || (item.status === "Running" ? "stop" : "start");
      bridge.send("control_service", { serviceName: item.serviceName, action: serviceAction });
    } else if (item.type === "conversion") {
      bridge.send("copy_clipboard", { text: item.result });
    } else if (item.type === "clip") {
      if (action === "delete") {
        bridge.send("delete_clipboard_item", { text: item.fullText });
      } else if (action === "clear_history") {
        bridge.send("clear_clipboard_history");
      } else if (action === "copy_text") {
        bridge.send("copy_clipboard", { text: item.fullText });
      } else {
        // Default action is to close and paste
        bridge.send("paste_clip", { text: item.fullText });
      }
    } else if (item.type === "ai") {
      if (item.answer) {
        bridge.send("copy_clipboard", { text: item.answer });
      } else if (!item.loading && item.query && item.query.trim().length >= 3) {
        setAiLoading(true);
        setAiError(undefined);
        bridge.send("ask_gemini", { query: item.query });
      }
    }
  };

  // Keyboard navigation
  const { activeIndex, setActiveIndex } = useKeyboard(
    results.length,
    (index) => executeItem(results[index]),
    () => results.length > 0 && setIsActionsOpen(!isActionsOpen),
    () => bridge.send("hide")
  );

  const activeItem = results[activeIndex] || null;

  return (
    <div className="app-container">
      <SearchBar
        value={searchQuery}
        onChange={setSearchQuery}
        onFocus={() => setIsActionsOpen(false)}
      />

      <div className="app-body">
        <ResultList
          items={results}
          activeIndex={activeIndex}
          onItemClick={(index) => {
            setActiveIndex(index);
            executeItem(results[index]);
          }}
        />

        <PreviewPane item={activeItem} />
      </div>

      <footer className="app-footer">
        <div className="footer-shortcuts">
          <div className="shortcut-item">
            <kbd>Esc</kbd> <span>Close</span>
          </div>
          <div className="shortcut-item">
            <kbd>↑↓</kbd> <span>Navigate</span>
          </div>
          <div className="shortcut-item">
            <kbd>↵</kbd> <span>Open / Paste</span>
          </div>
        </div>
        <div className="action-trigger" onClick={() => setIsActionsOpen(true)}>
          <span>Actions</span>
          <kbd>Ctrl+K</kbd>
        </div>
      </footer>

      {isActionsOpen && activeItem && (
        <ActionMenu
          item={activeItem}
          onClose={() => setIsActionsOpen(false)}
          onExecuteAction={(actionType) => {
            setIsActionsOpen(false);
            executeItem(activeItem, actionType);
          }}
        />
      )}
    </div>
  );
}
