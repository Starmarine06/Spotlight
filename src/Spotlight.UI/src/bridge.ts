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
          this.trigger(message.type, message.payload);
        } catch (e) {
          console.error("Failed to parse webview message:", e);
        }
      });
    }
  }

  public send(type: string, data: any = {}) {
    if (window.chrome?.webview) {
      window.chrome.webview.postMessage(JSON.stringify({ type, ...data }));
      return;
    }

    // Plain-browser development: answer with mock data so the UI can be worked on without the host.
    console.log(`[Bridge Mock Send] ${type}`, data);
    switch (type) {
      case "init":
        setTimeout(() => {
          this.trigger("app_info", { version: "dev", indexReady: true, hotkeys: ["Ctrl+Space"] });
          this.trigger("apps_loaded", [
            { Id: "1", Name: "Google Chrome", TargetPath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe", Exe: "chrome" },
            { Id: "2", Name: "Notepad", TargetPath: "C:\\Windows\\System32\\notepad.exe", Exe: "notepad" },
            { Id: "3", Name: "Visual Studio Code", TargetPath: "C:\\Program Files\\Microsoft VS Code\\Code.exe", Exe: "Code" },
            { Id: "4", Name: "Calculator", TargetPath: "C:\\Windows\\System32\\calc.exe", Exe: "calc" },
            { Id: "5", Name: "Microsoft Word", TargetPath: "C:\\Program Files\\Microsoft Office\\WINWORD.EXE", Exe: "WINWORD" },
            { Id: "6", Name: "Windows Terminal", TargetPath: "C:\\Program Files\\WindowsApps\\wt.exe", Exe: "wt" },
          ]);
        }, 100);
        break;
      case "search_files":
        setTimeout(() => {
          this.trigger("files_results", {
            id: data.id,
            query: data.query,
            ready: true,
            files: [
              { Name: `Report_${data.query}.pdf`, Path: `C:\\Users\\User\\Documents\\Report_${data.query}.pdf`, Size: 450000, DateModified: new Date().toISOString(), Extension: ".pdf", IsFolder: false, Score: 4200 },
              { Name: `${data.query}`, Path: `C:\\Users\\User\\Projects\\${data.query}`, Size: 0, DateModified: new Date().toISOString(), Extension: "", IsFolder: true, Score: 3900 },
            ],
          });
        }, 60);
        break;
      case "get_clipboard_history":
      case "delete_clipboard_item":
      case "clear_clipboard_history":
        setTimeout(() => {
          this.trigger("clipboard_history_loaded", [
            { FullText: "npm run dev", Timestamp: "2026-06-02 11:30:00" },
            { FullText: "https://github.com/Starmarine06/Spotlight", Timestamp: "2026-06-02 09:15:00" },
          ]);
        }, 80);
        break;
      case "ask_gemini":
        setTimeout(() => this.trigger("gemini_response", { query: data.query, answer: `Mock answer for "${data.query}"`, error: null }), 600);
        break;
    }
  }

  public on(type: string, callback: BridgeCallback) {
    (this.listeners[type] ??= []).push(callback);
  }

  public off(type: string, callback: BridgeCallback) {
    if (!this.listeners[type]) return;
    this.listeners[type] = this.listeners[type].filter((cb) => cb !== callback);
  }

  private trigger(type: string, payload: any) {
    this.listeners[type]?.forEach((cb) => cb(payload));
  }
}

export const bridge = new WebViewBridge();
