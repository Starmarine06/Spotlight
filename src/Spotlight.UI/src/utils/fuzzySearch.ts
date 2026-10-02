/**
 * Ranking engine shared by apps, system commands and Windows settings.
 *
 * A candidate is scored against every whitespace-separated query token; all tokens must match. Each token
 * takes the best of several match "tiers" (exact > prefix > word prefix > exe name > acronym > keyword >
 * substring > typo-tolerant word > fuzzy subsequence). The scores of the tokens are averaged, short names are
 * favoured, and the character ranges that matched are returned so the UI can highlight them.
 */

export type Range = [number, number];

export interface Candidate {
  name: string;
  /** Executable base name, e.g. "winword" for Microsoft Word. */
  exe?: string;
  /** Extra words that should find this item ("bluetooth" for the Devices page, ...). */
  keywords?: string[];
}

export interface Match {
  score: number;
  ranges: Range[];
}

export interface PreparedQuery {
  raw: string;
  tokens: string[];
}

const MARKS = /[̀-ͯ]/g;

export function normalize(text: string): string {
  return text.toLowerCase().normalize("NFD").replace(MARKS, "");
}

export function prepareQuery(query: string): PreparedQuery {
  const raw = normalize(query).trim();
  return { raw, tokens: raw.split(/\s+/).filter(Boolean) };
}

interface Word {
  text: string;
  start: number;
}

function splitWords(name: string): Word[] {
  const words: Word[] = [];
  const re = /[a-z0-9]+/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(name))) words.push({ text: m[0], start: m.index });
  return words;
}

/** Word starts, also treating camelCase humps in the ORIGINAL casing as boundaries ("PowerPoint" -> Power, Point). */
function boundaryStarts(original: string, normalized: string): number[] {
  const starts: number[] = [];
  for (let i = 0; i < normalized.length; i++) {
    const prev = i > 0 ? original[i - 1] : " ";
    const cur = original[i];
    const isAlnum = /[a-z0-9]/i.test(cur);
    if (!isAlnum) continue;
    const prevAlnum = /[a-z0-9]/i.test(prev);
    const hump = prevAlnum && /[a-z]/.test(prev) && /[A-Z]/.test(cur);
    const digitEdge = prevAlnum && /[a-z]/i.test(prev) !== /[a-z]/i.test(cur) && /\d/.test(cur);
    if (!prevAlnum || hump || digitEdge) starts.push(i);
  }
  return starts;
}

function damerauWithin(a: string, b: string, max: number): boolean {
  if (Math.abs(a.length - b.length) > max) return false;
  const rows = a.length + 1;
  const cols = b.length + 1;
  const d: number[][] = Array.from({ length: rows }, () => new Array(cols).fill(0));
  for (let i = 0; i < rows; i++) d[i][0] = i;
  for (let j = 0; j < cols; j++) d[0][j] = j;
  for (let i = 1; i < rows; i++) {
    for (let j = 1; j < cols; j++) {
      const cost = a[i - 1] === b[j - 1] ? 0 : 1;
      d[i][j] = Math.min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + cost);
      if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) {
        d[i][j] = Math.min(d[i][j], d[i - 2][j - 2] + 1);
      }
    }
  }
  return d[a.length][b.length] <= max;
}

/** fzf-like subsequence match: rewards word boundaries and consecutive runs. */
function fuzzySubsequence(token: string, name: string, boundaries: Set<number>): { score: number; ranges: Range[] } | null {
  const first = token[0];
  let best: { score: number; positions: number[] } | null = null;
  let starts = 0;

  for (let s = 0; s < name.length && starts < 8; s++) {
    if (name[s] !== first) continue;
    starts++;

    const positions: number[] = [s];
    let score = 16 + (boundaries.has(s) ? 24 : 0) + (s === 0 ? 30 : 0);
    let prev = s;
    let k = 1;
    for (let j = s + 1; j < name.length && k < token.length; j++) {
      if (name[j] !== token[k]) continue;
      positions.push(j);
      score += 16;
      if (j === prev + 1) score += 18;
      else score -= Math.min(j - prev - 1, 10);
      if (boundaries.has(j)) score += 24;
      prev = j;
      k++;
    }
    if (k < token.length) continue;
    if (!best || score > best.score) best = { score, positions };
  }

  if (!best) return null;
  const ranges: Range[] = best.positions.map((p) => [p, p + 1]);
  // 300..2400 : always below substring matches for a given name.
  return { score: Math.min(2400, 300 + best.score * 12), ranges };
}

function tokenMatch(
  token: string,
  nameOrig: string,
  name: string,
  words: Word[],
  boundaries: number[],
  exe: string,
  keywords: string[],
): Match | null {
  const len = name.length;
  const lengthPenalty = Math.min(len, 60) * 2;

  if (name === token) return { score: 10000, ranges: [[0, len]] };
  if (name.startsWith(token)) return { score: 8000 - lengthPenalty, ranges: [[0, token.length]] };

  // The executable name is what people actually type ("code", "winword", "chrome", "wt").
  if (exe) {
    if (exe === token) return { score: 7500 - lengthPenalty, ranges: [] };
    if (exe.startsWith(token) && token.length >= 2) return { score: 5200 - lengthPenalty, ranges: [] };
  }

  for (let i = 0; i < words.length; i++) {
    if (words[i].text.startsWith(token)) {
      return {
        score: 6000 - Math.min(i, 5) * 150 - lengthPenalty,
        ranges: [[words[i].start, words[i].start + token.length]],
      };
    }
  }

  // Acronym over word starts: "vsc" -> Visual Studio Code, "ppt" -> PowerPoint? (humps) , "wt" -> Windows Terminal.
  if (token.length >= 2 && boundaries.length >= 2) {
    const initials = boundaries.map((b) => name[b]).join("");
    const at = initials.indexOf(token);
    if (at !== -1 && (at === 0 || token.length >= 3)) {
      return {
        score: 5600 - at * 120 - lengthPenalty,
        ranges: boundaries.slice(at, at + token.length).map((b) => [b, b + 1] as Range),
      };
    }
  }

  for (const kw of keywords) {
    if (kw === token) return { score: 4600, ranges: [] };
    if (kw.startsWith(token) && token.length >= 2) return { score: 3600, ranges: [] };
  }

  const sub = name.indexOf(token);
  if (sub !== -1) return { score: 3200 - sub * 20 - lengthPenalty, ranges: [[sub, sub + token.length]] };

  // Typos ("firefx" ~ "firefox", "calcualtor" ~ "calculator") and loose abbreviations ("excl" ~ "excel").
  // Both are weak signals on their own; an item that satisfies both is clearly the intended one.
  let typo: Match | null = null;
  if (token.length >= 4) {
    const allowed = token.length >= 8 ? 2 : 1;
    for (const w of words) {
      if (w.text.length < token.length - allowed) continue;
      const slice = w.text.slice(0, token.length);
      if (damerauWithin(token, slice, allowed) || damerauWithin(token, w.text, allowed)) {
        typo = { score: 2300 - lengthPenalty, ranges: [[w.start, w.start + Math.min(token.length, w.text.length)]] };
        break;
      }
    }
  }

  const fuzzy = token.length >= 2 ? fuzzySubsequence(token, name, new Set(boundaries)) : null;
  if (typo && fuzzy) return { score: Math.max(typo.score, fuzzy.score - lengthPenalty / 2) + 250, ranges: typo.ranges };
  if (typo) return typo;
  if (fuzzy) return { score: fuzzy.score - lengthPenalty / 2, ranges: fuzzy.ranges };

  void nameOrig;
  return null;
}

export function matchCandidate(query: PreparedQuery, candidate: Candidate): Match | null {
  if (query.tokens.length === 0) return { score: 1, ranges: [] };

  const nameOrig = candidate.name;
  const name = normalize(nameOrig);
  const words = splitWords(name);
  const boundaries = boundaryStarts(nameOrig, name);
  const exe = candidate.exe ? normalize(candidate.exe) : "";
  const keywords = (candidate.keywords || []).map(normalize);

  let total = 0;
  const ranges: Range[] = [];
  for (const token of query.tokens) {
    const m = tokenMatch(token, nameOrig, name, words, boundaries, exe, keywords);
    if (!m) return null;
    total += m.score;
    ranges.push(...m.ranges);
  }

  let score = total / query.tokens.length;
  // Typing the full name (with spaces) should beat a looser multi-token match.
  if (query.tokens.length > 1) {
    if (name === query.raw) score += 3000;
    else if (name.startsWith(query.raw)) score += 1500;
  }

  return { score, ranges: mergeRanges(ranges) };
}

export function mergeRanges(ranges: Range[]): Range[] {
  if (ranges.length < 2) return ranges;
  const sorted = [...ranges].sort((a, b) => a[0] - b[0]);
  const out: Range[] = [[...sorted[0]] as Range];
  for (let i = 1; i < sorted.length; i++) {
    const last = out[out.length - 1];
    if (sorted[i][0] <= last[1]) last[1] = Math.max(last[1], sorted[i][1]);
    else out.push([...sorted[i]] as Range);
  }
  return out;
}

export interface Ranked<T> {
  item: T;
  score: number;
  ranges: Range[];
}

/** Scores every item, drops non-matches and returns the rest best-first. `boost` can add a per-item bonus. */
export function rank<T>(
  items: T[],
  query: string,
  toCandidate: (item: T) => Candidate,
  boost?: (item: T) => number,
): Ranked<T>[] {
  const prepared = prepareQuery(query);
  const out: Ranked<T>[] = [];
  for (const item of items) {
    const m = matchCandidate(prepared, toCandidate(item));
    if (!m) continue;
    out.push({ item, score: m.score + (boost ? boost(item) : 0), ranges: m.ranges });
  }
  out.sort((a, b) => b.score - a.score);
  return out;
}
