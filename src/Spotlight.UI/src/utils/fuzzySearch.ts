import type { AppSearchItem } from "../types";

export interface ScoredApp {
  app: AppSearchItem;
  score: number;
}

/**
 * Computes a fuzzy matching score for an app against a search query.
 * Higher score = higher relevance. Returns <= 0 if there is no match.
 */
export function scoreApp(app: AppSearchItem, query: string): number {
  const q = query.trim().toLowerCase();
  if (!q) return 1;

  const name = app.name.toLowerCase();
  const path = (app.path || "").toLowerCase();

  // 1. Exact Match
  if (name === q) {
    return 10000;
  }

  // 2. Exact Prefix Match on full name
  if (name.startsWith(q)) {
    return 5000 + (1000 - Math.min(name.length, 50));
  }

  const words = name.split(/[\s\-_\.\(\)\/]+/).filter(Boolean);

  // 3. Word Prefix Match (e.g. "term" matches "Windows Terminal", "code" matches "Visual Studio Code")
  for (let i = 0; i < words.length; i++) {
    if (words[i].startsWith(q)) {
      return 4000 - i * 100 + (500 - Math.min(name.length, 50));
    }
  }

  // 4. Acronym / Initials Match (e.g. "vsc" -> "Visual Studio Code", "wt" -> "Windows Terminal")
  if (words.length > 1) {
    const acronym = words.map((w) => w[0]).join("");
    if (acronym.startsWith(q)) {
      return 3500 + (500 - Math.min(name.length, 50));
    }
    if (acronym.includes(q)) {
      return 3000 - acronym.indexOf(q) * 50;
    }
  }

  // 5. Target Executable Name Match (e.g. "calc" -> "calc.exe", "wt" -> "wt.exe")
  if (path) {
    const filename = path.split(/[\/\\]/).pop() || "";
    const baseExeName = filename.replace(/\.(exe|lnk|url|msc)$/i, "");
    if (baseExeName === q) {
      return 3200;
    }
    if (baseExeName.startsWith(q)) {
      return 2800;
    }
  }

  // 6. Substring Match anywhere in name
  const subIdx = name.indexOf(q);
  if (subIdx !== -1) {
    return 2000 - subIdx * 20 - Math.min(name.length, 50);
  }

  // 7. Subsequence / Fuzzy Character Matching
  let qIdx = 0;
  let score = 500;
  let consecutiveCount = 0;
  let prevMatchIdx = -1;

  for (let i = 0; i < name.length && qIdx < q.length; i++) {
    if (name[i] === q[qIdx]) {
      qIdx++;
      if (prevMatchIdx === i - 1) {
        consecutiveCount++;
        score += 50 * consecutiveCount; // Bonus for consecutive matched characters
      } else {
        consecutiveCount = 0;
        if (prevMatchIdx !== -1) {
          score -= (i - prevMatchIdx) * 5; // Penalty for gaps between matched chars
        }
      }
      prevMatchIdx = i;
    }
  }

  if (qIdx === q.length) {
    return Math.max(score - name.length, 10);
  }

  return 0;
}

/**
 * Filter and rank apps by relevance score.
 */
export function scoreAndRankApps(apps: AppSearchItem[], query: string): AppSearchItem[] {
  const trimmed = query.trim();
  if (!trimmed) {
    return apps;
  }

  const scored: ScoredApp[] = [];
  for (const app of apps) {
    const score = scoreApp(app, trimmed);
    if (score > 0) {
      scored.push({ app, score });
    }
  }

  // Sort descending by score
  scored.sort((a, b) => b.score - a.score);

  return scored.map((s) => s.app);
}
