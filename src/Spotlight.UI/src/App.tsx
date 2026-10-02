import { useState, useEffect, useMemo, useRef, useCallback } from "react";
import { SearchBar } from "./components/SearchBar";
import { ResultList } from "./components/ResultList";
import { PreviewPane } from "./components/PreviewPane";
import { ActionMenu } from "./components/ActionMenu";
import type { SearchItem, AppSearchItem, FileSearchItem, ServiceSearchItem, ClipSearchItem, UpdateState } from "./types";
import { useKeyboard } from "./hooks/useKeyboard";
import { bridge } from "./bridge";
import { calculateSHA256, calculateSHA1 } from "./utils/prefixHelpers";
import { buildResults, usageKey } from "./utils/results";
import { recordUsage } from "./utils/usage";

const ICON_BASE = "https://icons.spotlight.local/";
const HISTORY_KEY = "spotlight_history";

/** Strip the mode prefix so the host only searches for what the user actually wants to find. */
function fileQueryFor(query: string): { text: string; mode: "files" | "none" | "recent" } {
  const q = query.trim();
  if (q.startsWith("<")) {
    const text = q.slice(1).trim();
    return text ? { text, mode: "files" } : { text: "", mode: "recent" };
  }
  if (/^[.!>:#%$=*?]/.test(q)) return { text: "", mode: "none" };
  return q.length >= 2 ? { text: q, mode: "files" } : { text: "", mode: "none" };
}

export default function App() {
  const [searchQuery, setSearchQuery] = useState("");
  const [apps, setApps] = useState<AppSearchItem[]>([]);
  const [fileResults, setFileResults] = useState<FileSearchItem[]>([]);
  const [services, setServices] = useState<ServiceSearchItem[]>([]);
  const [hashes, setHashes] = useState({ sha256: "", sha1: "" });
  const [clipboardHistory, setClipboardHistory] = useState<ClipSearchItem[]>([]);
  const [historyList, setHistoryList] = useState<SearchItem[]>([]);
  const [isActionsOpen, setIsActionsOpen] = useState(false);
  const [version, setVersion] = useState("");
  const [indexReady, setIndexReady] = useState(false);
  const [update, setUpdate] = useState<UpdateState | null>(null);
  const [confirmKey, setConfirmKey] = useState<string | null>(null);

  const [aiAnswer, setAiAnswer] = useState<string | undefined>();
  const [aiLoading, setAiLoading] = useState(false);
  const [aiError, setAiError] = useState<string | undefined>();

  // Latest values for bridge callbacks, which are registered once.
  const queryRef = useRef(searchQuery);
  useEffect(() => {
    queryRef.current = searchQuery;
  }, [searchQuery]);
  const fileRequestId = useRef(0);
  const aiRequested = useRef("");

  const refreshHistory = () => {
    try {
      setHistoryList(JSON.parse(localStorage.getItem(HISTORY_KEY) || "[]"));
    } catch {
      setHistoryList([]);
    }
  };

  const requestFiles = useCallback((text: string, mode: "files" | "recent") => {
    const id = ++fileRequestId.current;
    if (mode === "recent") bridge.send("recent_files", { id });
    else bridge.send("search_files", { id, query: text, limit: queryRef.current.trim().startsWith("<") ? 150 : 60 });
  }, []);

  // ---------------------------------------------------------------- bridge listeners (mount once)
  useEffect(() => {
    const onApps = (payload: any[]) => {
      setApps(
        payload.map((app) => ({
          type: "app" as const,
          id: app.Id,
          name: app.Name,
          path: app.TargetPath,
          arguments: app.Arguments || undefined,
          icon: app.Icon ? ICON_BASE + app.Icon : undefined,
          exe: app.Exe || undefined,
        })),
      );
    };

    const onAppInfo = (info: any) => {
      setVersion(info.version ?? "");
      setIndexReady(!!info.indexReady);
    };

    const onIndexStatus = (info: any) => {
      setIndexReady(!!info.ready);
      // The index finished building while a search was open - repeat it so results appear.
      const { text, mode } = fileQueryFor(queryRef.current);
      if (mode !== "none") requestFiles(text, mode);
    };

    const onFiles = (payload: any) => {
      if (payload.id !== fileRequestId.current) return; // a newer search superseded this one
      setIndexReady(!!payload.ready);
      setFileResults(
        payload.files.map((f: any) => ({
          type: "file" as const,
          name: f.Name,
          path: f.Path,
          size: f.Size,
          dateModified: f.DateModified,
          extension: f.Extension,
          isFolder: !!f.IsFolder,
          score: f.Score ?? 0,
        })),
      );
    };

    const onServices = (payload: any[]) =>
      setServices(
        payload.map((s) => ({
          type: "service" as const,
          name: s.DisplayName,
          path: s.Status,
          serviceName: s.Name,
          displayName: s.DisplayName,
          status: s.Status,
        })),
      );

    const onClipboard = (payload: any[]) =>
      setClipboardHistory(
        payload.map((c) => {
          const flat = String(c.FullText).replace(/\s+/g, " ");
          return {
            type: "clip" as const,
            name: flat.length > 70 ? flat.slice(0, 70) + "..." : flat,
            path: c.Timestamp,
            fullText: c.FullText,
            timestamp: c.Timestamp,
          };
        }),
      );

    const onAi = (payload: any) => {
      const q = queryRef.current.trim();
      if (!q.startsWith("?") || q.slice(1).trim().toLowerCase() !== String(payload.query).toLowerCase()) return;
      setAiLoading(false);
      if (payload.error) setAiError(payload.error);
      else setAiAnswer(payload.answer);
    };

    const resetForShow = (query: string) => {
      setSearchQuery(query);
      setFileResults([]);
      setIsActionsOpen(false);
      setConfirmKey(null);
      refreshHistory();
      window.dispatchEvent(new CustomEvent("window-shown"));
    };

    const onShown = (p: any) => {
      if (p?.version) setVersion(p.version);
      resetForShow("");
    };
    const onShowClipboard = () => resetForShow("*");

    const onUpdateAvailable = (p: any) => setUpdate((prev) => (prev?.progress !== undefined ? prev : { version: p.version, notes: p.notes }));
    const onUpdateProgress = (p: any) => setUpdate((prev) => (prev ? { ...prev, progress: p.percent, error: undefined } : prev));
    const onUpdateError = (p: any) => setUpdate((prev) => (prev ? { ...prev, progress: undefined, error: p.message } : prev));

    const subscriptions: [string, (p: any) => void][] = [
      ["apps_loaded", onApps],
      ["app_info", onAppInfo],
      ["index_status", onIndexStatus],
      ["files_results", onFiles],
      ["services_loaded", onServices],
      ["clipboard_history_loaded", onClipboard],
      ["gemini_response", onAi],
      ["window_shown", onShown],
      ["show_clipboard", onShowClipboard],
      ["update_available", onUpdateAvailable],
      ["update_progress", onUpdateProgress],
      ["update_error", onUpdateError],
    ];
    subscriptions.forEach(([type, cb]) => bridge.on(type, cb));

    bridge.send("init");
    refreshHistory();

    return () => subscriptions.forEach(([type, cb]) => bridge.off(type, cb));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ---------------------------------------------------------------- query-driven effects
  useEffect(() => {
    const { text, mode } = fileQueryFor(searchQuery);
    if (mode === "none") {
      fileRequestId.current++;
      setFileResults([]);
      return;
    }
    const handle = setTimeout(() => requestFiles(text, mode), 25);
    return () => clearTimeout(handle);
  }, [searchQuery, requestFiles]);

  useEffect(() => {
    setConfirmKey(null);
    setAiAnswer(undefined);
    setAiError(undefined);
    setAiLoading(false);
    aiRequested.current = "";
  }, [searchQuery]);

  // "?" web search starts by itself once typing pauses (Enter still works to force it).
  useEffect(() => {
    const q = searchQuery.trim();
    if (!q.startsWith("?")) return;
    const text = q.slice(1).trim();
    if (text.length < 3) return;
    const handle = setTimeout(() => {
      if (aiRequested.current === text) return;
      aiRequested.current = text;
      setAiLoading(true);
      bridge.send("ask_gemini", { query: text });
    }, 600);
    return () => clearTimeout(handle);
  }, [searchQuery]);

  useEffect(() => {
    if (searchQuery.startsWith("!") && !searchQuery.startsWith("!!")) bridge.send("get_services");
  }, [searchQuery.startsWith("!") && !searchQuery.startsWith("!!")]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (searchQuery.startsWith("*")) bridge.send("get_clipboard_history");
  }, [searchQuery.startsWith("*")]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!searchQuery.startsWith("#")) return;
    const text = searchQuery.slice(1).trim();
    if (!text) {
      setHashes({ sha256: "", sha1: "" });
      return;
    }
    Promise.all([calculateSHA256(text), calculateSHA1(text)]).then(([sha256, sha1]) => setHashes({ sha256, sha1 }));
  }, [searchQuery]);

  // ---------------------------------------------------------------- results
  const results = useMemo(
    () =>
      buildResults({
        query: searchQuery,
        apps,
        fileResults,
        services,
        clipboard: clipboardHistory,
        history: historyList,
        hashes,
        update,
        ai: { answer: aiAnswer, loading: aiLoading, error: aiError },
      }),
    [searchQuery, apps, fileResults, services, clipboardHistory, historyList, hashes, update, aiAnswer, aiLoading, aiError],
  );

  const saveToHistory = (item: SearchItem) => {
    if (!["app", "file", "system", "registry", "cmd", "service"].includes(item.type)) return;
    if (item.type === "system" && item.command.startsWith("update-spotlight")) return;
    try {
      const { ranges: _ranges, ...clean } = item as SearchItem & { ranges?: unknown };
      void _ranges;
      let list: SearchItem[] = JSON.parse(localStorage.getItem(HISTORY_KEY) || "[]");
      list = list.filter((i) => !(i.type === clean.type && i.name === clean.name));
      list.unshift(clean as SearchItem);
      localStorage.setItem(HISTORY_KEY, JSON.stringify(list.slice(0, 15)));
    } catch (e) {
      console.error(e);
    }
  };

  const executeItem = (item: SearchItem, action: string = "open") => {
    if (!item) return;

    // Destructive system actions need a second Enter.
    if (item.type === "system" && item.confirm && action === "open") {
      const key = `sys:${item.command}`;
      if (confirmKey !== key) {
        setConfirmKey(key);
        return;
      }
    }

    const key = usageKey(item);
    if (key && action === "open") recordUsage(key, queryRef.current);
    if (action === "open") saveToHistory(item);

    if (action === "copy_path") {
      const text = item.type === "cmd" ? item.command : item.type === "registry" ? item.registryPath : item.path;
      if (text) bridge.send("copy_clipboard", { text });
      return;
    }
    if (action === "copy_name") {
      bridge.send("copy_clipboard", { text: item.name });
      return;
    }

    switch (item.type) {
      case "app":
        if (action === "show_explorer") bridge.send("show_in_explorer", { path: item.path });
        else if (action === "run_admin") bridge.send("launch", { path: item.path, arguments: item.arguments, admin: true });
        else bridge.send("launch", { path: item.path, arguments: item.arguments });
        break;

      case "file":
        if (action === "show_explorer") bridge.send("show_in_explorer", { path: item.path });
        else bridge.send("launch", { path: item.path });
        break;

      case "system":
        if (item.command === "update-spotlight") {
          setUpdate((prev) => (prev ? { ...prev, progress: 0, error: undefined } : prev));
          bridge.send("install_update");
        } else if (item.command.startsWith("path:")) {
          bridge.send("launch", { path: item.command.slice(5) });
        } else if (item.command.startsWith("ms-settings:")) {
          bridge.send("launch", { path: item.command });
        } else {
          bridge.send("system", { command: item.command });
        }
        break;

      case "calc":
        bridge.send("copy_clipboard", { text: item.result });
        break;

      case "conversion":
        bridge.send("copy_clipboard", { text: item.copy ?? item.result });
        break;

      case "web": {
        const encoded = encodeURIComponent(item.query);
        const url =
          item.engine === "url" ? item.url! : item.engine === "google" ? `https://www.google.com/search?q=${encoded}` : `https://duckduckgo.com/?q=${encoded}`;
        bridge.send("launch", { path: url });
        break;
      }

      case "cmd":
        bridge.send("run_cmd", { command: item.command || "cmd.exe" });
        break;

      case "registry":
        bridge.send("open_registry", { path: item.registryPath || "HKCU\\Software" });
        break;

      case "service":
        bridge.send("control_service", { serviceName: item.serviceName, action: action === "open" ? (item.status === "Running" ? "stop" : "start") : action });
        break;

      case "clip":
        if (action === "delete") bridge.send("delete_clipboard_item", { text: item.fullText });
        else if (action === "clear_history") bridge.send("clear_clipboard_history");
        else if (action === "copy_text") bridge.send("copy_clipboard", { text: item.fullText });
        else bridge.send("paste_clip", { text: item.fullText });
        break;

      case "ai":
        if (item.answer) {
          bridge.send("copy_clipboard", { text: item.answer });
        } else if (!item.loading && item.query.trim().length >= 2) {
          aiRequested.current = item.query.trim();
          setAiLoading(true);
          setAiError(undefined);
          bridge.send("ask_gemini", { query: item.query });
        }
        break;
    }
  };

  const { activeIndex, setActiveIndex } = useKeyboard({
    itemCount: results.length,
    resetKey: searchQuery,
    onExecute: (index, secondary) => {
      const item = results[index];
      if (!item) return;
      if (secondary) executeItem(item, item.type === "file" || item.type === "app" ? "show_explorer" : "copy_path");
      else executeItem(item);
    },
    onToggleActions: () => results.length > 0 && setIsActionsOpen((open) => !open),
    onEscape: () => {
      if (isActionsOpen) setIsActionsOpen(false);
      else if (searchQuery) setSearchQuery("");
      else bridge.send("hide");
    },
  });

  const activeItem = results[activeIndex] || null;
  const activeKey = activeItem && activeItem.type === "system" && activeItem.confirm ? `sys:${activeItem.command}` : null;
  // Moving the selection cancels a pending confirmation.
  useEffect(() => {
    if (confirmKey && confirmKey !== activeKey) setConfirmKey(null);
  }, [activeKey, confirmKey]);

  const showFileHint = !indexReady && fileQueryFor(searchQuery).mode !== "none";

  return (
    <div className="app-container">
      <SearchBar value={searchQuery} onChange={setSearchQuery} onFocus={() => setIsActionsOpen(false)} />

      <div className="app-body">
        <ResultList
          items={results}
          activeIndex={activeIndex}
          confirmKey={confirmKey}
          query={searchQuery}
          onItemClick={(index) => {
            setActiveIndex(index);
            executeItem(results[index]);
          }}
          onItemHover={setActiveIndex}
        />

        <PreviewPane item={activeItem} />
      </div>

      <footer className="app-footer">
        <div className="footer-shortcuts">
          <div className="shortcut-item">
            <kbd>Esc</kbd> <span>{searchQuery ? "Clear" : "Close"}</span>
          </div>
          <div className="shortcut-item">
            <kbd>↑↓</kbd> <span>Navigate</span>
          </div>
          <div className="shortcut-item">
            <kbd>↵</kbd> <span>Open</span>
          </div>
          {showFileHint && <div className="shortcut-item index-hint">Indexing files...</div>}
        </div>
        <div className="footer-right">
          {version && <span className="version-tag">v{version}</span>}
          <div className="action-trigger" onClick={() => setIsActionsOpen(true)}>
            <span>Actions</span>
            <kbd>Ctrl+K</kbd>
          </div>
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
