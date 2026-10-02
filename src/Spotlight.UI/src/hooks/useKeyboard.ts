import { useEffect, useRef, useState } from "react";

interface KeyboardOptions {
  itemCount: number;
  /** Selection returns to the top whenever this changes (the query), but not when async results arrive. */
  resetKey: string;
  /** `secondary` is true for Ctrl+Enter (reveal in Explorer / copy path). */
  onExecute: (index: number, secondary: boolean) => void;
  onToggleActions: () => void;
  onEscape: () => void;
}

export function useKeyboard({ itemCount, resetKey, onExecute, onToggleActions, onEscape }: KeyboardOptions) {
  const [activeIndex, setActiveIndex] = useState(0);

  // Always call the latest callbacks without re-subscribing the listener on every render.
  const handlers = useRef({ onExecute, onToggleActions, onEscape, itemCount, activeIndex });
  useEffect(() => {
    handlers.current = { onExecute, onToggleActions, onEscape, itemCount, activeIndex };
  });

  useEffect(() => {
    setActiveIndex(0);
  }, [resetKey]);

  useEffect(() => {
    setActiveIndex((prev) => (itemCount === 0 ? 0 : Math.min(prev, itemCount - 1)));
  }, [itemCount]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const { itemCount: count, activeIndex: active } = handlers.current;

      switch (e.key) {
        case "Escape":
          e.preventDefault();
          handlers.current.onEscape();
          break;
        case "ArrowDown":
          e.preventDefault();
          setActiveIndex((prev) => (count > 0 ? (prev + 1) % count : 0));
          break;
        case "ArrowUp":
          e.preventDefault();
          setActiveIndex((prev) => (count > 0 ? (prev - 1 + count) % count : 0));
          break;
        case "PageDown":
          e.preventDefault();
          setActiveIndex((prev) => Math.min(prev + 6, Math.max(count - 1, 0)));
          break;
        case "PageUp":
          e.preventDefault();
          setActiveIndex((prev) => Math.max(prev - 6, 0));
          break;
        case "Enter":
          e.preventDefault();
          if (count > 0) handlers.current.onExecute(active, e.ctrlKey || e.metaKey);
          break;
        default:
          if ((e.key === "k" || e.key === "K") && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            handlers.current.onToggleActions();
          } else if ((e.key === "n" || e.key === "N") && e.ctrlKey) {
            e.preventDefault();
            setActiveIndex((prev) => (count > 0 ? (prev + 1) % count : 0));
          } else if ((e.key === "p" || e.key === "P") && e.ctrlKey) {
            e.preventDefault();
            setActiveIndex((prev) => (count > 0 ? (prev - 1 + count) % count : 0));
          }
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  return { activeIndex, setActiveIndex };
}
