import React, { useRef, useEffect } from "react";

interface SearchBarProps {
  value: string;
  onChange: (value: string) => void;
  onFocus: () => void;
}

export const SearchBar: React.FC<SearchBarProps> = ({ value, onChange, onFocus }) => {
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  // Listen to native window activation triggers
  useEffect(() => {
    const handleNativeFocus = () => {
      if (inputRef.current) {
        inputRef.current.focus();
        inputRef.current.select();
      }
    };

    // Typing anywhere (e.g. after clicking a result) should still land in the search box.
    const handleTyping = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.altKey || e.metaKey || e.key.length !== 1) return;
      if (document.activeElement !== inputRef.current) inputRef.current?.focus();
    };

    window.addEventListener("focus", handleNativeFocus);
    window.addEventListener("window-shown", handleNativeFocus);
    window.addEventListener("keydown", handleTyping, true);

    return () => {
      window.removeEventListener("focus", handleNativeFocus);
      window.removeEventListener("window-shown", handleNativeFocus);
      window.removeEventListener("keydown", handleTyping, true);
    };
  }, []);

  return (
    <div className="search-header">
      <svg
        className="search-icon"
        xmlns="http://www.w3.org/2000/svg"
        fill="none"
        viewBox="0 0 24 24"
        strokeWidth={2}
        stroke="currentColor"
      >
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.602 10.602Z"
        />
      </svg>
      <input
        ref={inputRef}
        type="text"
        className="search-input"
        placeholder="Search apps, files, calculator, commands..."
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onFocus={onFocus}
        spellCheck={false}
        autoComplete="off"
      />
    </div>
  );
};
