import type {
  AppSearchItem,
  CalcSearchItem,
  ClipSearchItem,
  CommandSearchItem,
  ConversionSearchItem,
  FileSearchItem,
  RegistrySearchItem,
  SearchItem,
  ServiceSearchItem,
  SystemSearchItem,
  UpdateState,
  WebSearchItem,
} from "../types";
import { SYSTEM_ACTIONS, WINDOWS_SETTINGS } from "../data/commands";
import { evaluate, formatNumber, looksLikeMath } from "./calc";
import { matchCandidate, prepareQuery, rank } from "./fuzzySearch";
import { convertUnits } from "./units";
import { topKeys, usageBoost } from "./usage";

export const appKey = (a: AppSearchItem) => `app:${a.path}|${a.arguments || ""}`;
export const fileKey = (f: FileSearchItem) => `file:${f.path}`;
export const systemKey = (s: SystemSearchItem) => `sys:${s.command}`;

export function usageKey(item: SearchItem): string | null {
  switch (item.type) {
    case "app": return appKey(item);
    case "file": return fileKey(item);
    case "system": return item.command.startsWith("update-spotlight") ? null : systemKey(item);
    default: return null;
  }
}

export interface Section {
  key: string;
  label: string;
}

/** Which visual group an item belongs to. Consecutive items with the same key share one header. */
export function sectionOf(item: SearchItem): Section {
  switch (item.type) {
    case "ai": return { key: "ai", label: "Web Answer" };
    case "calc": return { key: "calc", label: "Calculator" };
    case "conversion": return { key: "conversion", label: "Unit Conversion" };
    case "cmd": return { key: "cmd", label: "Command Prompt" };
    case "registry": return { key: "registry", label: "Registry Editor" };
    case "service": return { key: "service", label: "Windows Services" };
    case "clip": return { key: "clip", label: "Clipboard History" };
    case "app": return { key: "app", label: "Applications" };
    case "file": return { key: "file", label: "Files & Folders" };
    case "web": return { key: "web", label: item.engine === "url" ? "Open Link" : "Search the Web" };
    case "system":
      if (item.command.startsWith("update-spotlight")) return { key: "update", label: "Update" };
      if (item.command.startsWith("path:")) return { key: "path", label: "Open Path" };
      if (item.command.startsWith("ms-settings:")) return { key: "setting", label: "Windows Settings" };
      return { key: "system", label: "System Actions" };
  }
}

export interface ResultsContext {
  query: string;
  apps: AppSearchItem[];
  fileResults: FileSearchItem[];
  services: ServiceSearchItem[];
  clipboard: ClipSearchItem[];
  history: SearchItem[];
  hashes: { sha256: string; sha1: string };
  update: UpdateState | null;
  ai: { answer?: string; loading: boolean; error?: string };
}

const URL_RE = /^(https?:\/\/|www\.)\S+$/i;
const DOMAIN_RE = /^(?:[a-z0-9-]+\.)+(?:com|org|net|io|dev|app|co|ai|edu|gov|me|info|tv|gg|xyz|uk|de|in|fr|ca|us|tech|cloud|so|sh)(?:[/:?#]\S*)?$/i;
const PATH_RE = /^(?:[a-z]:[\\/]|\\\\|~[\\/]|%[a-z_]+%)/i;

function updateCard(update: UpdateState): SystemSearchItem {
  const busy = update.progress !== undefined;
  return {
    type: "system",
    name: busy ? `Updating to ${update.version}... ${update.progress}%` : `Update available: ${update.version}`,
    path: "update-spotlight",
    command: "update-spotlight",
    description: update.error
      ? `Update failed: ${update.error}. Press Enter to retry.`
      : busy
        ? "Downloading. Spotlight restarts by itself when it is ready."
        : "Press Enter to install. The old version is removed and Spotlight restarts.",
    iconName: "update",
  };
}

function ranges(query: string, name: string) {
  return matchCandidate(prepareQuery(query), { name })?.ranges ?? [];
}

function webItems(query: string): WebSearchItem[] {
  return [
    { type: "web", name: `Search Google for "${query}"`, path: "", query, engine: "google" },
    { type: "web", name: `Search DuckDuckGo for "${query}"`, path: "", query, engine: "duckduckgo" },
  ];
}

export function buildResults(ctx: ResultsContext): SearchItem[] {
  const { apps, update } = ctx;
  const query = ctx.query.trim();
  const out: SearchItem[] = [];
  const lower = query.toLowerCase();

  if (update && (!query || "update".startsWith(lower) || "version".startsWith(lower) || "spotlight".startsWith(lower))) {
    out.push(updateCard(update));
  }

  // ---- empty query: suggestions ---------------------------------------------------------------
  if (!query) {
    const byKey = new Map(apps.map((a) => [appKey(a), a]));
    const suggested: AppSearchItem[] = [];
    for (const key of topKeys("app:", 6)) {
      const app = byKey.get(key);
      if (app) suggested.push(app);
    }
    for (const app of apps) {
      if (suggested.length >= 6) break;
      if (!suggested.includes(app)) suggested.push(app);
    }
    out.push(...suggested, ...SYSTEM_ACTIONS.slice(0, 3));
    return out;
  }

  // ---- prefix modes ---------------------------------------------------------------------------
  if (query.startsWith("?")) {
    const aiQuery = query.slice(1).trim();
    out.push({
      type: "ai",
      name: aiQuery ? `Search the web: "${aiQuery}"` : "Search the web...",
      path: "",
      query: aiQuery,
      answer: ctx.ai.answer,
      loading: ctx.ai.loading,
      error: ctx.ai.error,
    });
    return out;
  }

  if (query.startsWith("!!")) return ctx.history;

  if (query.startsWith("*")) {
    const tokens = query.slice(1).trim().toLowerCase().split(/\s+/).filter(Boolean);
    return tokens.length === 0
      ? ctx.clipboard
      : ctx.clipboard.filter((c) => tokens.every((t) => c.fullText.toLowerCase().includes(t)));
  }

  if (query.startsWith(">")) {
    const command = query.slice(1).trim();
    const item: CommandSearchItem = {
      type: "cmd",
      name: `Run in Command Prompt: "${command || "ipconfig"}"`,
      path: "cmd.exe",
      command,
    };
    return [item];
  }

  if (query.startsWith(":")) {
    const regPath = query.slice(1).trim();
    const item: RegistrySearchItem = {
      type: "registry",
      name: `Open Registry Key: "${regPath || "HKCU\\Software"}"`,
      path: "Registry Editor (regedit)",
      registryPath: regPath,
    };
    return [item];
  }

  if (query.startsWith("=")) {
    const expr = query.slice(1).trim();
    if (!expr) {
      return [{ type: "calc", name: "Type an expression", path: "", expression: "e.g. (5 + 3) * 2, sqrt(16), 20% of 150", result: "0" }];
    }
    const value = evaluate(expr);
    return [
      value === null
        ? { type: "calc", name: "Keep typing...", path: "", expression: expr, result: "Error" }
        : { type: "calc", name: formatNumber(value), path: "", expression: expr, result: formatNumber(value) },
    ];
  }

  if (query.startsWith("!")) {
    const q = query.slice(1).trim();
    if (!q) return ctx.services.slice(0, 80);
    return rank(ctx.services, q, (s) => ({ name: s.displayName, keywords: [s.serviceName] }))
      .slice(0, 80)
      .map((r) => ({ ...r.item, ranges: r.ranges }));
  }

  if (query.startsWith("$") || (query.startsWith("%") && !query.startsWith("%%"))) {
    const q = query.slice(1).trim();
    if (!q) return WINDOWS_SETTINGS;
    return rank(WINDOWS_SETTINGS, q, (s) => ({ name: s.name, keywords: s.keywords })).map((r) => ({ ...r.item, ranges: r.ranges }));
  }

  if (query.startsWith("#")) {
    const text = query.slice(1).trim();
    const sha256: CalcSearchItem = { type: "calc", name: ctx.hashes.sha256, path: "", expression: `SHA-256 of "${text}"`, result: ctx.hashes.sha256 };
    const sha1: CalcSearchItem = { type: "calc", name: ctx.hashes.sha1, path: "", expression: `SHA-1 of "${text}"`, result: ctx.hashes.sha1 };
    return [sha256, sha1];
  }

  if (query.startsWith("%%")) {
    const conv = convertUnits(query.slice(2).trim());
    const item: ConversionSearchItem = conv
      ? { type: "conversion", name: conv.result, path: "", result: conv.result, copy: conv.copy }
      : { type: "conversion", name: "Try: 10 ft to m, 72 f to c, 2.5 gb to mb", path: "", result: "No conversion found" };
    return [item];
  }

  if (query.startsWith("<")) return ctx.fileResults;

  if (query.startsWith(".")) {
    const q = query.slice(1).trim();
    if (!q) return apps.slice(0, 100);
    return rank(apps, q, (a) => ({ name: a.name, exe: a.exe }), (a) => usageBoost(appKey(a), q))
      .slice(0, 50)
      .map((r) => ({ ...r.item, ranges: r.ranges }));
  }

  // ---- normal mode: one ranked search across everything --------------------------------------
  // Direct answers go first: arithmetic, unit conversions, links and paths.
  if (looksLikeMath(query)) {
    const value = evaluate(query);
    if (value !== null) {
      out.push({ type: "calc", name: formatNumber(value), path: "", expression: query, result: formatNumber(value) });
    }
  }

  const conv = convertUnits(query);
  if (conv) out.push({ type: "conversion", name: conv.result, path: "", result: conv.result, copy: conv.copy });

  if (URL_RE.test(query) || DOMAIN_RE.test(query)) {
    const url = /^https?:\/\//i.test(query) ? query : `https://${query}`;
    out.push({ type: "web", name: `Open ${query}`, path: url, query, engine: "url", url });
  }

  if (PATH_RE.test(query)) {
    out.push({ type: "system", name: `Open ${query}`, path: query, command: `path:${query}`, description: "Open this file or folder", iconName: "folder" });
  }

  const sections: { best: number; items: SearchItem[] }[] = [];
  const addSection = (items: { item: SearchItem; score: number }[], cap: number) => {
    if (items.length === 0) return;
    items.sort((a, b) => b.score - a.score);
    sections.push({ best: items[0].score, items: items.slice(0, cap).map((i) => i.item) });
  };

  addSection(
    rank(apps, query, (a) => ({ name: a.name, exe: a.exe }), (a) => usageBoost(appKey(a), query)).map((r) => ({
      item: { ...r.item, ranges: r.ranges } as SearchItem,
      score: r.score,
    })),
    8,
  );

  addSection(
    rank(
      [...SYSTEM_ACTIONS, ...WINDOWS_SETTINGS],
      query,
      (s) => ({ name: s.name, keywords: s.keywords }),
      (s) => usageBoost(systemKey(s), query),
    ).map((r) => ({ item: { ...r.item, ranges: r.ranges } as SearchItem, score: r.score * 0.85 })),
    5,
  );

  if (query.length >= 2) {
    addSection(
      ctx.fileResults.map((f) => ({
        item: { ...f, ranges: ranges(query, f.name) } as SearchItem,
        score: f.score * 0.7 + usageBoost(fileKey(f), query),
      })),
      10,
    );
  }

  sections.sort((a, b) => b.best - a.best);
  for (const s of sections) out.push(...s.items);

  out.push(...webItems(query));
  return out;
}
