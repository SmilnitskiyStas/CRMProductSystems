"use client";

// Debounced category typeahead (supplier-portal expansion #8, Phase 6e).
// Hits GET /api/categories/search (all active platform_categories). Portal dropdown
// positioned like components/ui/ActionMenu (createPortal + getBoundingClientRect,
// outside-click + scroll/resize dismissal). Dark-theme inline styles to match the
// supplier cabinet forms.

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useTranslations } from "next-intl";
import { Search, X } from "lucide-react";
import { useCategorySearch } from "../hooks/useCatalog";

export interface CategoryRef {
  id: string;
  name: string;
}

interface Props {
  value: CategoryRef | null;
  onChange: (value: CategoryRef | null) => void;
  placeholder?: string;
  disabled?: boolean;
  /** Forwarded to the <input> so an external <label htmlFor> can target it. */
  inputId?: string;
}

interface DropdownPos {
  top: number;
  left: number;
  width: number;
}

const inputWrapStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: 8,
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px",
  boxSizing: "border-box",
};

export function CategoryTypeahead({ value, onChange, placeholder, disabled, inputId }: Props) {
  const t = useTranslations("Dashboard.ui.categoryTypeahead");

  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [debounced, setDebounced] = useState("");
  const [pos, setPos] = useState<DropdownPos | null>(null);

  const wrapRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Debounce the query feeding the search hook.
  useEffect(() => {
    const id = setTimeout(() => setDebounced(query), 250);
    return () => clearTimeout(id);
  }, [query]);

  const { data: results = [], isFetching } = useCategorySearch(debounced);
  const term = debounced.trim();

  const calcPos = useCallback(() => {
    if (!wrapRef.current) return;
    const rect = wrapRef.current.getBoundingClientRect();
    setPos({
      top: rect.bottom + 4 + window.scrollY,
      left: rect.left + window.scrollX,
      width: rect.width,
    });
  }, []);

  const openMenu = () => {
    if (disabled) return;
    calcPos();
    setOpen(true);
  };

  // Outside-click dismissal.
  useEffect(() => {
    if (!open) return;
    function handle(e: MouseEvent) {
      const target = e.target as Node;
      if (wrapRef.current?.contains(target) || menuRef.current?.contains(target)) return;
      setOpen(false);
      setQuery("");
    }
    document.addEventListener("mousedown", handle);
    return () => document.removeEventListener("mousedown", handle);
  }, [open]);

  // Reposition on scroll / resize while open.
  useEffect(() => {
    if (!open) return;
    const reCalc = () => calcPos();
    window.addEventListener("scroll", reCalc, true);
    window.addEventListener("resize", reCalc);
    return () => {
      window.removeEventListener("scroll", reCalc, true);
      window.removeEventListener("resize", reCalc);
    };
  }, [open, calcPos]);

  const select = (r: { id: string; name: string }) => {
    onChange({ id: r.id, name: r.name });
    setOpen(false);
    setQuery("");
  };

  const clear = () => {
    onChange(null);
    setQuery("");
    setDebounced("");
    setOpen(false);
  };

  const displayValue = open ? query : value?.name ?? "";

  const dropdownBody = useMemo(() => {
    if (term.length < 2) {
      return <div style={hintStyle}>{t("minChars")}</div>;
    }
    if (isFetching && results.length === 0) {
      return <div style={hintStyle}>{t("searching")}</div>;
    }
    if (results.length === 0) {
      return <div style={hintStyle}>{t("empty")}</div>;
    }
    return results.map((r) => (
      <button
        key={r.id}
        type="button"
        onClick={() => select(r)}
        style={rowStyle}
        onMouseEnter={(e) => {
          (e.currentTarget as HTMLElement).style.background = "#1F2937";
        }}
        onMouseLeave={(e) => {
          (e.currentTarget as HTMLElement).style.background = "transparent";
        }}
      >
        <span style={{ display: "flex", flexDirection: "column", gap: 2, minWidth: 0 }}>
          <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>{r.name}</span>
          {r.parentName && (
            <span style={{ color: "#4B5563", fontSize: 11 }}>{r.parentName}</span>
          )}
        </span>
        {r.itemCount > 0 && (
          <span style={{ color: "#4B5563", fontSize: 11, flexShrink: 0 }}>{r.itemCount}</span>
        )}
      </button>
    ));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [term, isFetching, results, t]);

  const dropdown =
    open && pos
      ? createPortal(
          <div
            ref={menuRef}
            style={{
              position: "absolute",
              top: pos.top,
              left: pos.left,
              width: pos.width,
              maxHeight: 280,
              overflowY: "auto",
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              boxShadow: "0 8px 24px rgba(0,0,0,0.6)",
              zIndex: 9999,
              padding: 4,
            }}
          >
            {dropdownBody}
          </div>,
          document.body,
        )
      : null;

  return (
    <div ref={wrapRef} style={{ position: "relative" }}>
      <div style={{ ...inputWrapStyle, opacity: disabled ? 0.6 : 1 }}>
        <Search size={14} color="#4B5563" style={{ flexShrink: 0 }} />
        <input
          id={inputId}
          ref={inputRef}
          type="text"
          disabled={disabled}
          value={displayValue}
          placeholder={placeholder ?? t("placeholder")}
          onFocus={openMenu}
          onChange={(e) => {
            setQuery(e.target.value);
            if (!open) openMenu();
          }}
          style={{
            flex: 1,
            minWidth: 0,
            background: "transparent",
            border: "none",
            outline: "none",
            color: "#E8EDF5",
            fontSize: 13,
          }}
        />
        {value && !disabled && (
          <button
            type="button"
            onClick={clear}
            title={t("clear")}
            style={{
              display: "flex",
              alignItems: "center",
              background: "transparent",
              border: "none",
              color: "#6B7280",
              cursor: "pointer",
              padding: 0,
              flexShrink: 0,
            }}
          >
            <X size={14} />
          </button>
        )}
      </div>
      {dropdown}
    </div>
  );
}

const hintStyle: React.CSSProperties = {
  padding: "10px 12px",
  color: "#4B5563",
  fontSize: 12,
};

const rowStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  justifyContent: "space-between",
  gap: 10,
  width: "100%",
  padding: "8px 10px",
  borderRadius: 8,
  background: "transparent",
  border: "none",
  cursor: "pointer",
  textAlign: "left",
  transition: "background 0.1s",
};
