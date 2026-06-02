import React from "react";
import type { SearchItem } from "../types";

interface PreviewPaneProps {
  item: SearchItem | null;
}

export const PreviewPane: React.FC<PreviewPaneProps> = ({ item }) => {
  if (!item) {
    return (
      <div className="preview-panel">
        <div className="preview-empty">
          <div className="preview-logo">Spotlight</div>
          <p style={{ fontSize: "13px", opacity: 0.6 }}>
            Type to search files and launch apps.<br />
            Use <kbd>↑</kbd> <kbd>↓</kbd> and <kbd>Enter</kbd> to control.
          </p>
        </div>
      </div>
    );
  }

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return "0 Bytes";
    const k = 1024;
    const sizes = ["Bytes", "KB", "MB", "GB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + " " + sizes[i];
  };

  const formatDate = (dateStr: string) => {
    try {
      const date = new Date(dateStr);
      if (isNaN(date.getTime())) return dateStr;
      return date.toLocaleDateString(undefined, {
        year: "numeric",
        month: "short",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      });
    } catch {
      return dateStr;
    }
  };

  return (
    <div className="preview-panel">
      {item.type === "ai" ? (
        <div className="preview-ai">
          <div className="ai-header">
            <svg
              style={{ width: "32px", height: "32px", color: "#c084fc", filter: "drop-shadow(0 0 8px rgba(192, 132, 252, 0.4))" }}
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z"
              />
            </svg>
            <span className="ai-title">Quick Web Search</span>
          </div>
          
          <div className="ai-content-box">
            {item.loading ? (
              <div className="ai-loading">
                <div className="spinner"></div>
                <span>Searching the web...</span>
              </div>
            ) : item.error ? (
              <div className="ai-error-box">
                <svg style={{ width: "24px", height: "24px", color: "#ef4444" }} fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                </svg>
                <span className="error-title">Failed to fetch search result</span>
                <p className="error-message">
                  {item.error}
                </p>
              </div>
            ) : item.answer ? (
              <div className="ai-answer-container">
                <div className="ai-answer-text" style={{ whiteSpace: "pre-wrap" }}>{item.answer}</div>
              </div>
            ) : (
              <div className="ai-empty">
                <p>Type your search starting with <kbd>?</kbd> (e.g. <code>? speed of light</code>)</p>
              </div>
            )}
          </div>

          {item.answer && (
            <div style={{ fontSize: "11px", color: "var(--text-muted)", marginTop: "12px", textAlign: "center" }}>
              Press <kbd>Enter</kbd> to copy this result to clipboard.
            </div>
          )}
        </div>
      ) : item.type === "calc" ? (
        <div className="preview-calculator">
          <svg
            style={{ width: "40px", height: "40px", color: "var(--accent-primary)" }}
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M9 7h6m0 10v-3m-3 3h.01M9 17h.01M9 14h.01M12 14h.01M15 11h.01M12 11h.01M9 11h.01M7 21h10a2 2 0 002-2V5a2 2 0 00-2-2H7a2 2 0 00-2 2v14a2 2 0 002 2z"
            />
          </svg>
          <div className="calc-expr">{item.expression} =</div>
          <div className="calc-result">{item.result}</div>
          <div style={{ fontSize: "11px", color: "var(--text-muted)", marginTop: "12px" }}>
            Press <kbd>Enter</kbd> to copy this result to your clipboard.
          </div>
        </div>
      ) : item.type === "conversion" ? (
        <div className="preview-calculator">
          <svg
            style={{ width: "40px", height: "40px", color: "var(--accent-secondary)" }}
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M8 7h12m0 0l-4-4m4 4l-4 4m0 6H4m0 0l4 4m-4-4l4-4"
            />
          </svg>
          <div className="calc-expr">Conversion Result:</div>
          <div className="calc-result" style={{ fontSize: "28px" }}>{item.result}</div>
          <div style={{ fontSize: "11px", color: "var(--text-muted)", marginTop: "12px" }}>
            Press <kbd>Enter</kbd> to copy this value to your clipboard.
          </div>
        </div>
      ) : item.type === "clip" ? (
        <div className="preview-clipboard">
          <div style={{ display: "flex", justifyContent: "center", marginBottom: "8px" }}>
            <svg
              style={{ width: "36px", height: "36px", color: "#f472b6" }}
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-3 7h3m-3 4h3m-6-4h.01M9 16h.01"
              />
            </svg>
          </div>
          <div className="clip-meta">
            <span className="clip-time">Copied: {formatDate(item.timestamp)}</span>
            <span className="clip-size">{item.fullText.length} chars</span>
          </div>
          <div className="clip-preview-container">
            <pre className="clip-preview-text">{item.fullText}</pre>
          </div>
          <div style={{ fontSize: "11px", color: "var(--text-muted)", marginTop: "8px", textAlign: "center", lineHeight: "1.5" }}>
            Press <kbd>Enter</kbd> to paste clip. <kbd>Ctrl+C</kbd> to copy.<br />
            Press <kbd>Ctrl+K</kbd> for more clipboard actions.
          </div>
        </div>
      ) : (
        <div className="preview-content">
          <div className="preview-header">
            <div className="preview-large-icon">
              {item.type === "app" ? (
                item.icon ? (
                  <img src={`data:image/png;base64,${item.icon}`} alt={item.name} />
                ) : (
                  <svg fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                  </svg>
                )
              ) : item.type === "file" ? (
                <svg fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
                </svg>
              ) : item.type === "system" ? (
                <svg fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                </svg>
              ) : item.type === "cmd" ? (
                <svg fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 9l3 3-3 3m5 0h3M5 20h14a2 2 0 002-2V6a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                </svg>
              ) : item.type === "registry" ? (
                <svg fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
                </svg>
              ) : item.type === "service" ? (
                <svg fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                </svg>
              ) : (
                <svg fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 12a9 9 0 01-9 9m9-9a9 9 0 00-9-9m9 9H3m9 9a9 9 0 01-9-9m9 9c1.657 0 3-4.03 3-9s-1.343-9-3-9m0 18c-1.657 0-3-4.03-3-9s1.343-9 3-9m-9 9a9 9 0 019-9" />
                </svg>
              )}
            </div>
            <div className="preview-title" style={{ wordBreak: "break-word", padding: "0 8px" }}>
              {item.name}
            </div>
            <div className="preview-type-badge">{item.type === "cmd" ? "Command" : item.type}</div>
          </div>

          <div className="preview-details-list">
            {item.type === "app" && (
              <>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Application Path</span>
                  <span className="preview-detail-value">{item.path}</span>
                </div>
                {item.arguments && (
                  <div className="preview-detail-item">
                    <span className="preview-detail-label">Arguments</span>
                    <span className="preview-detail-value" style={{ fontFamily: "monospace", fontSize: "12px", background: "rgba(0,0,0,0.2)", padding: "4px 8px", borderRadius: "4px" }}>
                      {item.arguments}
                    </span>
                  </div>
                )}
              </>
            )}

            {item.type === "file" && (
              <>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Full Path</span>
                  <span className="preview-detail-value">{item.path}</span>
                </div>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">File Size</span>
                  <span className="preview-detail-value">{formatBytes(item.size)}</span>
                </div>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Last Modified</span>
                  <span className="preview-detail-value">{formatDate(item.dateModified)}</span>
                </div>
              </>
            )}

            {item.type === "system" && (
              <>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Action Description</span>
                  <span className="preview-detail-value">{item.description}</span>
                </div>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Command Signature</span>
                  <span className="preview-detail-value" style={{ fontFamily: "monospace" }}>
                    {item.command}
                  </span>
                </div>
              </>
            )}

            {item.type === "cmd" && (
              <>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Shell Command</span>
                  <span className="preview-detail-value" style={{ fontFamily: "monospace", fontSize: "12px", background: "rgba(0,0,0,0.25)", padding: "8px", borderRadius: "6px", border: "1px solid var(--border-color)", wordBreak: "break-all" }}>
                    &gt; {item.command || "ipconfig"}
                  </span>
                </div>
                <div className="preview-detail-item" style={{ marginTop: "12px" }}>
                  <span className="preview-detail-value" style={{ fontSize: "12px", color: "var(--text-muted)" }}>
                    Press <kbd>Enter</kbd> to execute this command in a new visible Command Prompt window.
                  </span>
                </div>
              </>
            )}

            {item.type === "registry" && (
              <>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Registry Key Path</span>
                  <span className="preview-detail-value" style={{ wordBreak: "break-all" }}>
                    {item.registryPath || "HKCU\\Software"}
                  </span>
                </div>
                <div className="preview-detail-item" style={{ marginTop: "12px" }}>
                  <span className="preview-detail-value" style={{ fontSize: "12px", color: "var(--text-muted)" }}>
                    Press <kbd>Enter</kbd> to open Registry Editor directly focused on this key.
                  </span>
                </div>
              </>
            )}

            {item.type === "service" && (
              <>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Display Name</span>
                  <span className="preview-detail-value">{item.displayName}</span>
                </div>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Service Name</span>
                  <span className="preview-detail-value" style={{ fontFamily: "monospace" }}>{item.serviceName}</span>
                </div>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Status</span>
                  <span className="preview-detail-value" style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                    <span style={{
                      width: "8px",
                      height: "8px",
                      borderRadius: "50%",
                      background: item.status === "Running" ? "#10b981" : "#ef4444",
                      boxShadow: item.status === "Running" ? "0 0 8px #10b981" : "0 0 8px #ef4444"
                    }} />
                    {item.status}
                  </span>
                </div>
                <div className="preview-detail-item" style={{ marginTop: "12px" }}>
                  <span className="preview-detail-value" style={{ fontSize: "12px", color: "var(--text-muted)" }}>
                    Press <kbd>Enter</kbd> to {item.status === "Running" ? "Stop" : "Start"} this service.<br />
                    Press <kbd>Ctrl+K</kbd> for more service control actions.
                  </span>
                </div>
              </>
            )}

            {item.type === "web" && (
              <>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Web Query</span>
                  <span className="preview-detail-value">"{item.query}"</span>
                </div>
                <div className="preview-detail-item">
                  <span className="preview-detail-label">Search Provider</span>
                  <span className="preview-detail-value">
                    {item.engine === "google" ? "Google Search" : "DuckDuckGo"}
                  </span>
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
};
