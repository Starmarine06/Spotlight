import type { Range } from "./utils/fuzzySearch";

interface Base {
  name: string;
  path: string;
  /** Character ranges of `name` that matched the query (for highlighting). */
  ranges?: Range[];
}

export interface AppSearchItem extends Base {
  type: "app";
  id?: string;
  arguments?: string;
  /** URL of the cached PNG icon (served by the host). */
  icon?: string;
  /** Executable base name, used by search ("word" -> winword). */
  exe?: string;
}

export interface FileSearchItem extends Base {
  type: "file";
  size: number;
  dateModified: string;
  extension: string;
  isFolder: boolean;
  score: number;
}

export interface SystemSearchItem extends Base {
  type: "system";
  command: string;
  description: string;
  iconName: string;
  keywords?: string[];
  /** Needs a second Enter press before running (shutdown, restart, ...). */
  confirm?: boolean;
}

export interface CalcSearchItem extends Base {
  type: "calc";
  expression: string;
  result: string;
}

export interface WebSearchItem extends Base {
  type: "web";
  query: string;
  engine: "google" | "duckduckgo" | "url";
  url?: string;
}

export interface ServiceSearchItem extends Base {
  type: "service";
  serviceName: string;
  displayName: string;
  status: string; // "Running", "Stopped", etc.
}

export interface RegistrySearchItem extends Base {
  type: "registry";
  registryPath: string;
}

export interface CommandSearchItem extends Base {
  type: "cmd";
  command: string;
}

export interface ConversionSearchItem extends Base {
  type: "conversion";
  result: string;
  copy?: string;
}

export interface ClipSearchItem extends Base {
  type: "clip";
  fullText: string;
  timestamp: string;
}

export interface AISearchItem extends Base {
  type: "ai";
  query: string;
  answer?: string;
  error?: string;
  loading?: boolean;
}

export type SearchItem =
  | AppSearchItem
  | FileSearchItem
  | SystemSearchItem
  | CalcSearchItem
  | WebSearchItem
  | ServiceSearchItem
  | RegistrySearchItem
  | CommandSearchItem
  | ConversionSearchItem
  | ClipSearchItem
  | AISearchItem;

export interface UpdateState {
  version: string;
  notes: string;
  /** 0-100 while downloading, undefined when idle. */
  progress?: number;
  error?: string;
}
