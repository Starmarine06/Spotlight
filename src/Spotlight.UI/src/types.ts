export interface AppSearchItem {
  type: "app";
  name: string;
  path: string;
  arguments?: string;
  icon?: string; // base64
}

export interface FileSearchItem {
  type: "file";
  name: string;
  path: string;
  size: number;
  dateModified: string;
  extension: string;
}

export interface SystemSearchItem {
  type: "system";
  name: string;
  path: string;
  command: string;
  description: string;
  iconName: string;
}

export interface CalcSearchItem {
  type: "calc";
  name: string;
  path: string;
  expression: string;
  result: string;
}

export interface WebSearchItem {
  type: "web";
  name: string;
  path: string;
  query: string;
  engine: "google" | "duckduckgo";
}

export interface ServiceSearchItem {
  type: "service";
  name: string;
  path: string;
  serviceName: string;
  displayName: string;
  status: string; // "Running", "Stopped", etc.
}

export interface RegistrySearchItem {
  type: "registry";
  name: string;
  path: string;
  registryPath: string;
}

export interface CommandSearchItem {
  type: "cmd";
  name: string;
  path: string;
  command: string;
}

export interface ConversionSearchItem {
  type: "conversion";
  name: string;
  path: string;
  result: string;
}

export interface ClipSearchItem {
  type: "clip";
  name: string;
  path: string;
  fullText: string;
  timestamp: string;
}

export interface AISearchItem {
  type: "ai";
  name: string;
  path: string;
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
