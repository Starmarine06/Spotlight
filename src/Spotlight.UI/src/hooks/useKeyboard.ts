import { useEffect, useState } from "react";

export function useKeyboard(
  itemCount: number,
  onExecute: (index: number) => void,
  onToggleActions: () => void,
  onEscape: () => void
) {
  const [activeIndex, setActiveIndex] = useState(0);

  // Reset active index if list size changes
  useEffect(() => {
    setActiveIndex(0);
  }, [itemCount]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onEscape();
      } else if (e.key === "ArrowDown") {
        e.preventDefault();
        setActiveIndex((prev) => (itemCount > 0 ? (prev + 1) % itemCount : 0));
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        setActiveIndex((prev) => (itemCount > 0 ? (prev - 1 + itemCount) % itemCount : 0));
      } else if (e.key === "Enter") {
        e.preventDefault();
        if (itemCount > 0) {
          onExecute(activeIndex);
        }
      } else if (e.key === "k" && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        onToggleActions();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [itemCount, activeIndex, onExecute, onToggleActions, onEscape]);

  return { activeIndex, setActiveIndex };
}
