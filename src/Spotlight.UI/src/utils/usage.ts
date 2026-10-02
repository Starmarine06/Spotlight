/**
 * Learns from what you launch. Two signals feed the ranking:
 *  - frecency: how often and how recently an item was opened,
 *  - query affinity: which item you picked after typing a given query ("ch" -> Chrome).
 * Stored in localStorage, which lives in the WebView2 profile under %LOCALAPPDATA%\Spotlight\data.
 */

const KEY = "spotlight_usage_v2";
const MAX_ITEMS = 400;
const MAX_QUERIES = 300;

interface UsageData {
  items: Record<string, { n: number; t: number }>;
  picks: Record<string, Record<string, number>>;
}

let cache: UsageData | null = null;

function load(): UsageData {
  if (cache) return cache;
  try {
    const raw = localStorage.getItem(KEY);
    cache = raw ? (JSON.parse(raw) as UsageData) : { items: {}, picks: {} };
  } catch {
    cache = { items: {}, picks: {} };
  }
  cache.items ??= {};
  cache.picks ??= {};
  return cache;
}

function save() {
  try {
    localStorage.setItem(KEY, JSON.stringify(cache));
  } catch {
    /* storage unavailable */
  }
}

export function recordUsage(key: string, query: string) {
  const data = load();
  const now = Date.now();
  const entry = data.items[key] ?? { n: 0, t: now };
  entry.n += 1;
  entry.t = now;
  data.items[key] = entry;

  const q = query.trim().toLowerCase();
  if (q) {
    const picks = (data.picks[q] ??= {});
    picks[key] = (picks[key] ?? 0) + 1;
  }

  const keys = Object.keys(data.items);
  if (keys.length > MAX_ITEMS) {
    keys.sort((a, b) => data.items[a].t - data.items[b].t);
    for (const k of keys.slice(0, keys.length - MAX_ITEMS)) delete data.items[k];
  }
  const queries = Object.keys(data.picks);
  if (queries.length > MAX_QUERIES) {
    for (const k of queries.slice(0, queries.length - MAX_QUERIES)) delete data.picks[k];
  }
  save();
}

/** Bonus (0..~900) added on top of the text-match score. */
export function usageBoost(key: string, query: string): number {
  const data = load();
  let boost = 0;

  const entry = data.items[key];
  if (entry) {
    const ageDays = (Date.now() - entry.t) / 86_400_000;
    const recency = ageDays < 1 ? 1 : ageDays < 7 ? 0.7 : ageDays < 30 ? 0.4 : 0.15;
    boost += Math.min(350, Math.log2(1 + entry.n) * 90) * (0.4 + 0.6 * recency);
  }

  const q = query.trim().toLowerCase();
  if (q) {
    const exact = data.picks[q]?.[key];
    if (exact) boost += Math.min(600, 300 + exact * 60);
    else if (q.length >= 2) {
      // The user has typed a prefix of something they previously completed ("chr" after "chrome").
      for (const past of Object.keys(data.picks)) {
        if (past.length > q.length && past.startsWith(q) && data.picks[past][key]) {
          boost += 220;
          break;
        }
      }
    }
  }
  return boost;
}

/** Most used / most recent keys, best first - used for the empty-query suggestions. */
export function topKeys(prefix: string, limit: number): string[] {
  const data = load();
  return Object.keys(data.items)
    .filter((k) => k.startsWith(prefix))
    .sort((a, b) => score(data.items[b]) - score(data.items[a]))
    .slice(0, limit);
}

function score(e: { n: number; t: number }): number {
  const ageDays = (Date.now() - e.t) / 86_400_000;
  return Math.log2(1 + e.n) * 100 - Math.min(ageDays, 90);
}
