import React, { useEffect, useState } from "react";
import type { SearchItem } from "../types";

interface ActionMenuProps {
  item: SearchItem;
  onClose: () => void;
  onExecuteAction: (actionType: string) => void;
}

interface Action {
  label: string;
  type: string;
  /** Ctrl+<key> triggers the action while the menu is open. */
  key?: string;
}

function actionsFor(item: SearchItem): Action[] {
  switch (item.type) {
    case "app":
      return [
        { label: "Open Application", type: "open" },
        { label: "Run as Administrator", type: "run_admin", key: "a" },
        { label: "Show in File Explorer", type: "show_explorer", key: "o" },
        { label: "Copy Path", type: "copy_path", key: "c" },
      ];
    case "file":
      return [
        { label: item.isFolder ? "Open Folder" : "Open File", type: "open" },
        { label: "Show in File Explorer", type: "show_explorer", key: "o" },
        { label: "Copy Full Path", type: "copy_path", key: "c" },
        { label: "Copy Name", type: "copy_name", key: "n" },
      ];
    case "system":
      return [{ label: "Run", type: "open" }];
    case "calc":
    case "conversion":
      return [{ label: "Copy Result to Clipboard", type: "open" }];
    case "web":
      return [{ label: "Open in Browser", type: "open" }];
    case "cmd":
      return [
        { label: "Run Command", type: "open" },
        { label: "Copy Command Text", type: "copy_path", key: "c" },
      ];
    case "registry":
      return [
        { label: "Open Registry Key", type: "open" },
        { label: "Copy Registry Path", type: "copy_path", key: "c" },
      ];
    case "service":
      return [
        { label: "Start Service", type: "start", key: "s" },
        { label: "Stop Service", type: "stop", key: "d" },
        { label: "Restart Service", type: "restart", key: "r" },
      ];
    case "ai":
      return [{ label: "Copy Answer to Clipboard", type: "open" }];
    case "clip":
      return [
        { label: "Paste Clip", type: "open" },
        { label: "Copy to Clipboard", type: "copy_text", key: "c" },
        { label: "Delete Clip", type: "delete", key: "d" },
        { label: "Clear Clipboard History", type: "clear_history", key: "r" },
      ];
  }
}

export const ActionMenu: React.FC<ActionMenuProps> = ({ item, onClose, onExecuteAction }) => {
  const [activeIndex, setActiveIndex] = useState(0);
  const actions = actionsFor(item);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      e.stopPropagation();

      if (e.key === "Escape" || ((e.key === "k" || e.key === "K") && e.ctrlKey)) {
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
      } else if (e.ctrlKey) {
        const match = actions.find((a) => a.key === e.key.toLowerCase());
        if (match) {
          e.preventDefault();
          onExecuteAction(match.type);
        }
      }
    };

    window.addEventListener("keydown", handleKeyDown, true);
    return () => window.removeEventListener("keydown", handleKeyDown, true);
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
            <kbd>{act.key ? `Ctrl+${act.key.toUpperCase()}` : "↵"}</kbd>
          </div>
        ))}
      </div>
    </div>
  );
};
