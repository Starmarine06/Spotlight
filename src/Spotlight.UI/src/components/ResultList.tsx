import React, { useEffect, useRef } from "react";
import type { SearchItem } from "../types";
import type { Range } from "../utils/fuzzySearch";
import { sectionOf } from "../utils/results";

interface ResultListProps {
  items: SearchItem[];
  activeIndex: number;
  confirmKey: string | null;
  query: string;
  onItemClick: (index: number) => void;
  onItemHover: (index: number) => void;
}

/** Renders `text` with the matched character ranges emphasised. */
const Highlight: React.FC<{ text: string; ranges?: Range[] }> = ({ text, ranges }) => {
  if (!ranges || ranges.length === 0) return <>{text}</>;
  const parts: React.ReactNode[] = [];
  let cursor = 0;
  ranges.forEach(([start, end], i) => {
    if (start >= text.length) return;
    if (start > cursor) parts.push(text.slice(cursor, start));
    parts.push(<mark key={i} className="hl">{text.slice(start, Math.min(end, text.length))}</mark>);
    cursor = Math.min(end, text.length);
  });
  if (cursor < text.length) parts.push(text.slice(cursor));
  return <>{parts}</>;
};

export const ResultList: React.FC<ResultListProps> = ({ items, activeIndex, confirmKey, query, onItemClick, onItemHover }) => {
  const containerRef = useRef<HTMLDivElement>(null);
  // Hovering moves the selection too; scrolling in response would make the list jump under the cursor.
  const fromMouse = useRef(false);

  useEffect(() => {
    if (fromMouse.current) {
      fromMouse.current = false;
      return;
    }
    containerRef.current?.querySelector(".result-item.active")?.scrollIntoView({ block: "nearest" });
  }, [activeIndex]);

  useEffect(() => {
    if (containerRef.current) containerRef.current.scrollTop = 0;
  }, [query]);

  if (items.length === 0) {
    return (
      <div className="results-panel" style={{ justifyContent: "center", alignItems: "center" }}>
        <p style={{ color: "var(--text-muted)", fontSize: "14px" }}>No results found</p>
      </div>
    );
  }

  const getSystemIcon = (iconName: string) => {
    switch (iconName) {
      case "lock":
        return (
          <svg className="item-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
          </svg>
        );
      case "sleep":
        return (
          <svg className="item-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />
          </svg>
        );
      case "power":
        return (
          <svg className="item-icon" style={{ color: "#f87171" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5.636 5.636a9 9 0 1012.728 0M12 3v9" />
          </svg>
        );
      case "volume_up":
        return (
          <svg className="item-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.536 8.464a5 5 0 010 7.072m2.828-9.9a9 9 0 010 12.728M5.586 15H4a1 1 0 01-1-1v-4a1 1 0 011-1h1.586l4.707-4.707C10.923 3.663 12 4.109 12 5v14c0 .891-1.077 1.337-1.707.707L5.586 15z" />
          </svg>
        );
      case "trash":
        return (
          <svg className="item-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
          </svg>
        );
      case "wifi":
        return (
          <svg className="item-icon" style={{ color: "#60a5fa" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 19h.01M5.978 13A8.995 8.995 0 0112 10c2.51 0 4.778 1.026 6.022 2.684M3.078 8A13.978 13.978 0 0112 5c3.897 0 7.42 1.59 9.922 4.15" />
          </svg>
        );
      case "bluetooth":
        return (
          <svg className="item-icon" style={{ color: "#3b82f6" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 7l10 10-5 5V2l5 5L7 17" />
          </svg>
        );
      case "update":
        return (
          <svg className="item-icon" style={{ color: "#34d399" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
          </svg>
        );
      case "settings":
        return (
          <svg className="item-icon" style={{ color: "#a8a29e" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
          </svg>
        );
      default:
        return (
          <svg className="item-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
          </svg>
        );
    }
  };

  const getFileIcon = (ext: string) => {
    const e = ext.toLowerCase();
    
    if ([".exe", ".msi", ".bat", ".cmd", ".lnk"].includes(e)) {
      return (
        <svg className="item-icon" style={{ color: "#34d399" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 9l3 3-3 3m5 0h3M5 20h14a2 2 0 002-2V6a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
        </svg>
      );
    }
    
    if ([".pdf", ".epub"].includes(e)) {
      return (
        <svg className="item-icon" style={{ color: "#f87171" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
        </svg>
      );
    }

    if ([".docx", ".doc", ".txt", ".rtf", ".md"].includes(e)) {
      return (
        <svg className="item-icon" style={{ color: "#60a5fa" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
        </svg>
      );
    }

    if ([".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico"].includes(e)) {
      return (
        <svg className="item-icon" style={{ color: "#fb7185" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
        </svg>
      );
    }

    if ([".mp3", ".wav", ".flac", ".ogg", ".m4a"].includes(e)) {
      return (
        <svg className="item-icon" style={{ color: "#c084fc" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 19V6l12-3v13M9 19c0 1.105-1.343 2-3 2s-3-.895-3-2 1.343-2 3-2 3 .895 3 2zm12-3c0 1.105-1.343 2-3 2s-3-.895-3-2 1.343-2 3-2 3 .895 3 2zM9 10l12-3" />
        </svg>
      );
    }

    if ([".mp4", ".mkv", ".avi", ".mov", ".wmv"].includes(e)) {
      return (
        <svg className="item-icon" style={{ color: "#f43f5e" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 10l4.553-2.276A1 1 0 0121 8.618v6.764a1 1 0 01-1.447.894L15 14M5 18h8a2 2 0 002-2V8a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
        </svg>
      );
    }

    if ([".zip", ".rar", ".7z", ".tar", ".gz"].includes(e)) {
      return (
        <svg className="item-icon" style={{ color: "#facc15" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 8h14M5 8a2 2 0 110-4h14a2 2 0 110 4M5 8v10a2 2 0 002 2h10a2 2 0 002-2V8m-9 4h4" />
        </svg>
      );
    }

    return (
      <svg className="item-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
      </svg>
    );
  };

  return (
    <div ref={containerRef} className="results-panel">
      {items.map((item, index) => {
        const section = sectionOf(item);
        const previous = index > 0 ? sectionOf(items[index - 1]) : null;
        const isActive = index === activeIndex;
        const isConfirming = item.type === "system" && item.confirm && confirmKey === `sys:${item.command}`;

        return (
          <React.Fragment key={`${section.key}-${item.type}-${item.path}-${item.name}-${index}`}>
            {(!previous || previous.key !== section.key) && <div className="group-header">{section.label}</div>}
            <div
              className={`result-item ${isActive ? "active" : ""} ${isConfirming ? "confirming" : ""}`}
              onClick={() => onItemClick(index)}
              onMouseMove={() => {
                if (index !== activeIndex) {
                  fromMouse.current = true;
                  onItemHover(index);
                }
              }}
            >
              <div className="item-icon-container">{renderIcon(item)}</div>
              <div className="item-details">
                <div style={{ display: "flex", alignItems: "center" }}>
                  <span className="item-title">
                    <Highlight text={item.name} ranges={item.ranges} />
                  </span>
                  {item.type === "service" && (
                    <span className={`status-badge ${item.status === "Running" ? "running" : "stopped"}`}>{item.status}</span>
                  )}
                </div>
                <span className="item-subtitle">{isConfirming ? "Press Enter again to confirm" : subtitleFor(item)}</span>
              </div>
            </div>
          </React.Fragment>
        );
      })}
    </div>
  );

  function renderIcon(item: SearchItem) {
    if (item.type === "app") {
      return item.icon ? (
        <img src={item.icon} className="item-icon app-icon" alt="" loading="lazy" />
      ) : (
        <svg className="item-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
        </svg>
      );
    }
    if (item.type === "file") return item.isFolder ? folderIcon() : getFileIcon(item.extension);
    if (item.type === "system") return item.iconName === "folder" ? folderIcon() : getSystemIcon(item.iconName);
    return getTypeIcon(item.type);
  }
};

function subtitleFor(item: SearchItem): string {
  switch (item.type) {
    case "app": return item.arguments ? `${item.path} ${item.arguments}` : item.path;
    case "file": return item.path;
    case "system": return item.description;
    case "calc": return `Calculator: ${item.expression}`;
    case "cmd": return `Run shell command: ${item.command}`;
    case "registry": return "Navigate to registry path";
    case "service": return `System service: ${item.serviceName}`;
    case "conversion": return "Copy converted value to clipboard";
    case "clip": return `Copied ${item.timestamp} - Enter to paste`;
    case "ai": return "Look up a quick answer from the web";
    case "web": return item.engine === "url" ? item.url ?? "" : `Open query in ${item.engine === "google" ? "Google Search" : "DuckDuckGo"}`;
  }
}

const folderIcon = () => (
  <svg className="item-icon" style={{ color: "#fbbf24" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 7a2 2 0 012-2h4l2 2h8a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2V7z" />
  </svg>
);

const colored = (color: string, path: string, extra?: string) => (
  <svg className="item-icon" style={{ color }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d={path} />
    {extra && <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d={extra} />}
  </svg>
);

function getTypeIcon(type: SearchItem["type"]) {
  switch (type) {
    case "calc": return colored("#a78bfa", "M9 7h6m0 10v-3m-3 3h.01M9 17h.01M9 14h.01M12 14h.01M15 11h.01M12 11h.01M9 11h.01M7 21h10a2 2 0 002-2V5a2 2 0 00-2-2H7a2 2 0 00-2 2v14a2 2 0 002 2z");
    case "cmd": return colored("#34d399", "M8 9l3 3-3 3m5 0h3M5 20h14a2 2 0 002-2V6a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z");
    case "registry": return colored("#fb923c", "M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10");
    case "service": return colored("#60a5fa", "M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z", "M15 12a3 3 0 11-6 0 3 3 0 016 0z");
    case "conversion": return colored("#38bdf8", "M8 7h12m0 0l-4-4m4 4l-4 4m0 6H4m0 0l4 4m-4-4l4-4");
    case "clip": return colored("#f472b6", "M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-3 7h3m-3 4h3m-6-4h.01M9 16h.01");
    case "ai": return colored("#c084fc", "M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z");
    default:
      return colored("#94a3b8", "M21 12a9 9 0 01-9 9m9-9a9 9 0 00-9-9m9 9H3m9 9a9 9 0 01-9-9m9 9c1.657 0 3-4.03 3-9s-1.343-9-3-9m0 18c-1.657 0-3-4.03-3-9s1.343-9 3-9m-9 9a9 9 0 019-9");
  }
}
