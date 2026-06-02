declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: string) => void;
        addEventListener: (event: string, callback: (e: any) => void) => void;
        removeEventListener: (event: string, callback: (e: any) => void) => void;
      };
    };
  }
}

type BridgeCallback = (payload: any) => void;

class WebViewBridge {
  private listeners: { [key: string]: BridgeCallback[] } = {};

  constructor() {
    if (window.chrome?.webview) {
      window.chrome.webview.addEventListener("message", (event: any) => {
        try {
          const message = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
          const { type, payload } = message;
          if (type && this.listeners[type]) {
            this.listeners[type].forEach(cb => cb(payload));
          }
        } catch (e) {
          console.error("Failed to parse webview message:", e);
        }
      });
    }
  }

  public send(type: string, data: any = {}) {
    if (window.chrome?.webview) {
      window.chrome.webview.postMessage(JSON.stringify({ type, ...data }));
    } else {
      console.log(`[Bridge Mock Send] Type: ${type}`, data);
      
      // Provide developer mock responses when running in standard browser
      if (type === "init") {
        setTimeout(() => {
          this.trigger("apps_loaded", [
            { Name: "Google Chrome", TargetPath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe", IconBase64: "" },
            { Name: "Notepad", TargetPath: "C:\\Windows\\System32\\notepad.exe", IconBase64: "" },
            { Name: "Visual Studio Code", TargetPath: "C:\\Program Files\\Microsoft VS Code\\Code.exe", IconBase64: "" },
            { Name: "Calculator", TargetPath: "C:\\Windows\\System32\\calc.exe", IconBase64: "" },
            { Name: "File Explorer", TargetPath: "C:\\Windows\\explorer.exe", IconBase64: "" }
          ]);
        }, 150);
      } else if (type === "search_files") {
        if (!data.query) return;
        setTimeout(() => {
          this.trigger("files_results", {
            query: data.query,
            files: [
              { Name: `Presentation_${data.query}.pptx`, Path: `C:\\Users\\User\\Documents\\Presentation_${data.query}.pptx`, Size: 1024 * 1024 * 4.2, DateModified: new Date().toISOString(), Extension: ".pptx" },
              { Name: `Report_${data.query}.pdf`, Path: `C:\\Users\\User\\Reports\\Report_${data.query}.pdf`, Size: 1024 * 450, DateModified: new Date().toISOString(), Extension: ".pdf" },
              { Name: `index_${data.query}.tsx`, Path: `C:\\Projects\\app\\src\\index_${data.query}.tsx`, Size: 1024 * 12, DateModified: new Date().toISOString(), Extension: ".tsx" }
            ]
          });
        }, 200);
      } else if (type === "get_clipboard_history" || type === "delete_clipboard_item" || type === "clear_clipboard_history") {
        setTimeout(() => {
          this.trigger("clipboard_history_loaded", [
            { FullText: "Spotlight is a fast keyboard-driven search and command launcher.", Timestamp: "2026-06-02 12:00:00" },
            { FullText: "npm run dev", Timestamp: "2026-06-02 11:30:00" },
            { FullText: "const value = Math.max(a, b);", Timestamp: "2026-06-02 10:45:00" },
            { FullText: "https://github.com/google/deepmind", Timestamp: "2026-06-02 09:15:00" }
          ]);
        }, 150);
      } else if (type === "ask_gemini") {
        setTimeout(() => {
          this.trigger("gemini_response", {
            query: data.query,
            answer: `Google AI Grounding Search Answer for: **"${data.query}"**\n\nGoogle AI is Google's division dedicated to artificial intelligence. By using Gemini 1.5 Flash with search tools, it retrieves real-time Google search summaries and grounds the responses.\n\n*   **Search grounding query:** ${data.query}\n*   **Status:** Running and grounded\n*   **Timestamp:** ${new Date().toLocaleTimeString()}\n\nTo view native results, configure your API key in gemini_key.txt or setting GEMINI_API_KEY environment variable.`,
          });
        }, 1200);
      }
    }
  }

  public on(type: string, callback: BridgeCallback) {
    if (!this.listeners[type]) {
      
      this.listeners[type] = [];
    }
    this.listeners[type].push(callback);
  }

  public off(type: string, callback: BridgeCallback) {
    if (!this.listeners[type]) return;
    this.listeners[type] = this.listeners[type].filter(cb => cb !== callback);
  }

  private trigger(type: string, payload: any) {
    if (this.listeners[type]) {
      this.listeners[type].forEach(cb => cb(payload));
    }
  }
}

export const bridge = new WebViewBridge();
