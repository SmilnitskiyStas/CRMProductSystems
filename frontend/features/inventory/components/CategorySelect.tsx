"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ChevronDown, ChevronRight, Search, X } from "lucide-react";
import type { CategoryDto } from "../types";

interface Props {
  value: string;
  onChange: (id: string) => void;
  categories: CategoryDto[];
  /** A category the tenant can't otherwise pick (hidden by business type / soft-deleted) but the item still sits on. */
  orphanOption?: { id: string; name: string } | null;
  noneLabel: string;
  placeholder: string;
  searchPlaceholder: string;
  emptyText: string;
  ariaLabel?: string;
}

const PANEL_MAX_HEIGHT = 320;

/**
 * Single-select category picker: a searchable, collapsible tree in a portal dropdown.
 * Replaces a flat indented native `<select>` — parents with children start collapsed so a
 * deep catalogue doesn't dump every row at once. Typing switches to a flat filtered list.
 * Portal + getBoundingClientRect positioning follows `components/ui/ActionMenu.tsx`.
 */
export function CategorySelect({
  value,
  onChange,
  categories,
  orphanOption = null,
  noneLabel,
  placeholder,
  searchPlaceholder,
  emptyText,
  ariaLabel,
}: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [pos, setPos] = useState<{ top: number; left: number; width: number } | null>(null);

  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);

  const { childrenByParent, byId, ancestorsOf } = useMemo(() => {
    const ids = new Set(categories.map((c) => c.id));
    const map = new Map<string | null, CategoryDto[]>();
    const dict = new Map<string, CategoryDto>();
    for (const c of categories) {
      dict.set(c.id, c);
      const parent = c.parentId && ids.has(c.parentId) ? c.parentId : null;
      map.set(parent, [...(map.get(parent) ?? []), c]);
    }
    for (const kids of map.values()) kids.sort((a, b) => a.name.localeCompare(b.name, "uk"));
    const ancestorsOf = (id: string): string[] => {
      const out: string[] = [];
      let cur = dict.get(id)?.parentId ?? null;
      while (cur && dict.has(cur) && !out.includes(cur)) {
        out.push(cur);
        cur = dict.get(cur)?.parentId ?? null;
      }
      return out;
    };
    return { childrenByParent: map, byId: dict, ancestorsOf };
  }, [categories]);

  const selectedLabel = useMemo(() => {
    if (!value) return null;
    return byId.get(value)?.name ?? (orphanOption?.id === value ? orphanOption.name : value);
  }, [value, byId, orphanOption]);

  const pathLabel = useCallback(
    (id: string) => ancestorsOf(id).reverse().map((a) => byId.get(a)?.name).filter(Boolean).join(" › "),
    [ancestorsOf, byId],
  );

  const calcPos = useCallback(() => {
    const r = triggerRef.current?.getBoundingClientRect();
    if (!r) return;
    setPos({ top: r.bottom + 4, left: r.left, width: r.width });
  }, []);

  const openPanel = useCallback(() => {
    calcPos();
    setQuery("");
    // Pre-expand the path down to the current selection so it's visible on open.
    setExpanded(value ? new Set(ancestorsOf(value)) : new Set());
    setOpen(true);
    requestAnimationFrame(() => searchRef.current?.focus());
  }, [calcPos, value, ancestorsOf]);

  useEffect(() => {
    if (!open) return;
    const reflow = () => calcPos();
    window.addEventListener("resize", reflow);
    window.addEventListener("scroll", reflow, true);
    const onDown = (e: MouseEvent) => {
      const t = e.target as Node;
      if (triggerRef.current?.contains(t) || panelRef.current?.contains(t)) return;
      setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("resize", reflow);
      window.removeEventListener("scroll", reflow, true);
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open, calcPos]);

  const pick = (id: string) => {
    onChange(id);
    setOpen(false);
  };

  const toggleExpand = (id: string) =>
    setExpanded((cur) => {
      const next = new Set(cur);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });

  const q = query.trim().toLocaleLowerCase("uk");
  const matches = q
    ? categories
        .filter((c) => c.name.toLocaleLowerCase("uk").includes(q))
        .sort((a, b) => a.name.localeCompare(b.name, "uk"))
    : [];

  const rowStyle = (active: boolean): React.CSSProperties => ({
    display: "flex",
    alignItems: "center",
    gap: 4,
    width: "100%",
    padding: "6px 8px",
    border: 0,
    borderRadius: 6,
    background: active ? "#1D3461" : "transparent",
    color: active ? "#BFDBFE" : "#D1D5DB",
    fontSize: 12,
    textAlign: "left",
    cursor: "pointer",
  });

  const renderNode = (category: CategoryDto, depth: number): React.ReactNode => {
    const kids = childrenByParent.get(category.id) ?? [];
    const isOpen = expanded.has(category.id);
    return (
      <div key={category.id}>
        <div style={{ display: "flex", alignItems: "center", paddingLeft: depth * 16 }}>
          {kids.length > 0 ? (
            <button
              type="button"
              aria-label={isOpen ? "Згорнути" : "Розгорнути"}
              onClick={() => toggleExpand(category.id)}
              style={{ display: "flex", padding: 2, border: 0, background: "transparent", color: "#6B7280", cursor: "pointer" }}
            >
              {isOpen ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
            </button>
          ) : (
            <span style={{ width: 18 }} />
          )}
          <button type="button" onClick={() => pick(category.id)} style={rowStyle(value === category.id)}>
            <span style={{ flex: 1 }}>{category.name}</span>
            {kids.length > 0 && <span style={{ color: "#4B5563", fontSize: 10 }}>{kids.length}</span>}
          </button>
        </div>
        {isOpen && kids.map((child) => renderNode(child, depth + 1))}
      </div>
    );
  };

  const roots = childrenByParent.get(null) ?? [];

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        aria-label={ariaLabel}
        onClick={() => (open ? setOpen(false) : openPanel())}
        style={{
          width: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          gap: 8,
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 8,
          color: selectedLabel ? "#E8EDF5" : "#6B7280",
          fontSize: 13,
          padding: "8px 12px",
          cursor: "pointer",
          boxSizing: "border-box",
        }}
      >
        <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
          {selectedLabel ?? placeholder}
        </span>
        <span style={{ display: "flex", alignItems: "center", gap: 2, flexShrink: 0 }}>
          {value && (
            <span
              role="button"
              aria-label={noneLabel}
              onClick={(e) => {
                e.stopPropagation();
                onChange("");
              }}
              style={{ display: "flex", color: "#6B7280" }}
            >
              <X size={13} />
            </span>
          )}
          <ChevronDown size={15} style={{ color: "#6B7280" }} />
        </span>
      </button>

      {open &&
        pos &&
        createPortal(
          <div
            ref={panelRef}
            style={{
              position: "fixed",
              top: pos.top,
              left: pos.left,
              width: pos.width,
              background: "#0D1117",
              border: "1px solid #293241",
              borderRadius: 10,
              boxShadow: "0 8px 24px rgba(0,0,0,0.6)",
              zIndex: 9999,
              padding: 8,
              boxSizing: "border-box",
            }}
          >
            <div style={{ position: "relative", marginBottom: 6 }}>
              <Search size={13} style={{ position: "absolute", left: 9, top: "50%", transform: "translateY(-50%)", color: "#6B7280" }} />
              <input
                ref={searchRef}
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder={searchPlaceholder}
                style={{
                  width: "100%",
                  boxSizing: "border-box",
                  background: "#111827",
                  border: "1px solid #374151",
                  borderRadius: 8,
                  padding: "7px 9px 7px 28px",
                  color: "#E8EDF5",
                  fontSize: 12,
                  outline: "none",
                }}
              />
            </div>

            <div style={{ maxHeight: PANEL_MAX_HEIGHT, overflowY: "auto" }}>
              {!q && (
                <button type="button" onClick={() => pick("")} style={rowStyle(!value)}>
                  <span style={{ marginLeft: 18 }}>{noneLabel}</span>
                </button>
              )}
              {!q && orphanOption && (
                <button type="button" onClick={() => pick(orphanOption.id)} style={rowStyle(value === orphanOption.id)}>
                  <span style={{ marginLeft: 18 }}>{orphanOption.name}</span>
                </button>
              )}

              {q ? (
                matches.length > 0 ? (
                  matches.map((c) => {
                    const path = pathLabel(c.id);
                    return (
                      <button key={c.id} type="button" onClick={() => pick(c.id)} style={rowStyle(value === c.id)}>
                        <span style={{ flex: 1, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                          {c.name}
                          {path && <span style={{ color: "#4B5563", fontSize: 10 }}>{"  ·  " + path}</span>}
                        </span>
                      </button>
                    );
                  })
                ) : (
                  <p style={{ color: "#6B7280", fontSize: 12, padding: "8px 6px", margin: 0 }}>{emptyText}</p>
                )
              ) : roots.length > 0 ? (
                roots.map((root) => renderNode(root, 0))
              ) : (
                <p style={{ color: "#6B7280", fontSize: 12, padding: "8px 6px", margin: 0 }}>{emptyText}</p>
              )}
            </div>
          </div>,
          document.body,
        )}
    </>
  );
}
