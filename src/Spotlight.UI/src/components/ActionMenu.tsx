import React, { useEffect, useState } from "react";
import type { SearchItem } from "../types";

interface ActionMenuProps {
  item: SearchItem;
  onClose: () => void;
  onExecuteAction: (actionType: string) => void;
}

export const ActionMenu: React.FC<ActionMenuProps> = ({ item, onClose, onExecuteAction }) => {
  const [activeIndex, setActiveIndex] = useState(0);

  const actions: { label: string; shortcut: string; type: string }[] = [];

  if (item.type === "app") {
    actions.push(
      { label: "Open Application", shortcut: "↵", type: "open" },
      { label: "Copy Executable Path", shortcut: "Ctrl+C", type: "copy_path" }
    );
  } else if (item.type === "file") {
    actions.push(
      { label: "Open File", shortcut: "↵", type: "open" },
      { label: "Show in File Explorer", shortcut: "Ctrl+O", type: "show_explorer" },
      { label: "Copy File Path", shortcut: "Ctrl+C", type: "copy_path" }
    );
  } else if (item.type === "system") {
    actions.push({ label: "Execute Command", shortcut: "↵", type: "open" });
  } else if (item.type === "calc") {
    actions.push({ label: "Copy Result to Clipboard", shortcut: "↵", type: "copy_result" });
  } else if (item.type === "web") {
    actions.push({ label: `Search in Browser`, shortcut: "↵", type: "open" });
  } else if (item.type === "cmd") {
    actions.push(
      { label: "Run Command", shortcut: "↵", type: "open" },
      { label: "Copy Command Text", shortcut: "Ctrl+C", type: "copy_path" }
    );
  } else if (item.type === "registry") {
    actions.push(
      { label: "Open Registry Key", shortcut: "↵", type: "open" },
      { label: "Copy Registry Path", shortcut: "Ctrl+C", type: "copy_path" }
    );
  } else if (item.type === "service") {
    actions.push(
      { label: "Start Service", shortcut: "Ctrl+S", type: "start" },
      { label: "Stop Service", shortcut: "Ctrl+D", type: "stop" },
      { label: "Restart Service", shortcut: "Ctrl+R", type: "restart" }
    );
  } else if (item.type === "conversion") {
    actions.push({ label: "Copy Result to Clipboard", shortcut: "↵", type: "copy_result" });
  } else if (item.type === "ai") {
    actions.push({ label: "Copy Answer to Clipboard", shortcut: "↵", type: "copy_result" });
  } else if (item.type === "clip") {
    actions.push(
      { label: "Paste Clip", shortcut: "↵", type: "paste" },
      { label: "Copy to Clipboard", shortcut: "Ctrl+C", type: "copy_text" },
      { label: "Delete Clip", shortcut: "Ctrl+D", type: "delete" },
      { label: "Clear Clipboard History", shortcut: "Ctrl+R", type: "clear_history" }
    );
  }

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      e.stopPropagation();

      if (e.key === "Escape") {
        e.preventDefault();
        onClose();
      } else if (e.key === "ArrowDown") {
        e.preventDefault();
        setActiveIndex((prev) => (prev + 1) % actions.length);
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        setActiveIndex((prev) => (prev - 1 + actions.length) % actions.length);
      } else if (e.key === "Enter") {
        e.preventDefault();
        onExecuteAction(actions[activeIndex].type);
      }
    };

    window.addEventListener("keydown", handleKeyDown, true);
    return () => {
      window.removeEventListener("keydown", handleKeyDown, true);
    };
  }, [activeIndex, actions, onClose, onExecuteAction]);

  return (
    <div className="action-overlay">
      <div className="action-menu-header">Actions for "{item.name}"</div>
      <div className="action-menu-list">
        {actions.map((act, idx) => (
          <div
            key={act.type}
            className={`action-menu-item ${idx === activeIndex ? "active" : ""}`}
            onClick={() => onExecuteAction(act.type)}
            onMouseEnter={() => setActiveIndex(idx)}
          >
            <div className="action-menu-item-left">
              <svg className="action-menu-item-icon" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" />
              </svg>
              <span>{act.label}</span>
            </div>
            <kbd>{act.shortcut}</kbd>
          </div>
        ))}
      </div>
    </div>
  );
};
