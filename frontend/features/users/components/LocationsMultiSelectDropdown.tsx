"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ChevronDown } from "lucide-react";

export interface LocationsMultiSelectOption {
  id: string;
  name: string;
}

interface Props {
  locations: LocationsMultiSelectOption[];
  selectedIds: string[];
  onToggle: (id: string) => void;
  /** Button/summary text once at least one location is selected, e.g. "3 магазини обрано". */
  summaryLabel: string;
  /** Button text while nothing is selected yet, e.g. "Оберіть магазини". */
  placeholderLabel: string;
  /** "Готово"/"Done" button rendered at the bottom of the open panel. */
  doneLabel: string;
  disabled?: boolean;
}

interface PanelPos {
  top: number;
  left: number;
  width: number;
}

/**
 * Closed-by-default multi-select dropdown for store/location assignment (TASK-397, Bug 1 item
 * 3 — product owner explicitly asked for a collapsed dropdown rather than a permanently-
 * expanded checkbox list). Renders a single button summarizing the current selection; clicking
 * it opens a floating checkbox panel (portal + viewport-anchored positioning, outside-click and
 * scroll/resize handling — same proven pattern as `ActionMenu`/`Sidebar`'s
 * `CollapsedGroupTrigger`) that closes on outside click or the "Done" button.
 *
 * Purely presentational/controlled: selection state and persistence (fetch current value, Save
 * button, API calls) stay with the caller. `UserLocationsEditor` wraps this with its own
 * fetch+save+dirty-check logic (editing an existing user); `InviteUserModal` wraps it with the
 * invite form's own local `territoryIds` state (no separate save button — the whole invite form
 * submits together, then a follow-up `PUT /api/users/:id/locations` applies the chosen list).
 */
export function LocationsMultiSelectDropdown({
  locations,
  selectedIds,
  onToggle,
  summaryLabel,
  placeholderLabel,
  doneLabel,
  disabled,
}: Props) {
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState<PanelPos | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  const calcPos = useCallback(() => {
    if (!triggerRef.current) return;
    const rect = triggerRef.current.getBoundingClientRect();
    setPos({
      top: rect.bottom + 4 + window.scrollY,
      left: rect.left + window.scrollX,
      width: rect.width,
    });
  }, []);

  const toggleOpen = () => {
    if (disabled) return;
    if (!open) calcPos();
    setOpen((v) => !v);
  };

  // Close on outside click
  useEffect(() => {
    if (!open) return;
    function handle(e: MouseEvent) {
      const target = e.target as Node;
      if (triggerRef.current?.contains(target) || panelRef.current?.contains(target)) return;
      setOpen(false);
    }
    document.addEventListener("mousedown", handle);
    return () => document.removeEventListener("mousedown", handle);
  }, [open]);

  // Reposition on scroll / resize
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

  const panel =
    open && pos
      ? createPortal(
          <div
            ref={panelRef}
            style={{
              position: "absolute",
              top: pos.top,
              left: pos.left,
              width: Math.max(pos.width, 220),
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              boxShadow: "0 8px 24px rgba(0,0,0,0.6)",
              zIndex: 9999,
              padding: 8,
            }}
          >
            <div style={{ display: "flex", flexDirection: "column", gap: 2, maxHeight: 220, overflowY: "auto" }}>
              {locations.map((loc) => (
                <label
                  key={loc.id}
                  style={{
                    display: "flex", alignItems: "center", gap: 8,
                    fontSize: 13, color: "#E8EDF5", cursor: "pointer",
                    padding: "6px 6px", borderRadius: 6,
                  }}
                  onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = "#1F2937"; }}
                  onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = "transparent"; }}
                >
                  <input
                    type="checkbox"
                    checked={selectedIds.includes(loc.id)}
                    onChange={() => onToggle(loc.id)}
                  />
                  {loc.name}
                </label>
              ))}
            </div>
            <button
              type="button"
              onClick={() => setOpen(false)}
              style={{
                marginTop: 8, width: "100%", padding: "7px 0",
                background: "transparent", border: "1px solid #374151", borderRadius: 7,
                color: "#9CA3AF", fontSize: 12, fontWeight: 600, cursor: "pointer",
              }}
            >
              {doneLabel}
            </button>
          </div>,
          document.body,
        )
      : null;

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        onClick={toggleOpen}
        disabled={disabled}
        style={{
          width: "100%",
          display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8,
          background: "#0D1117",
          border: "1px solid #374151",
          borderRadius: 8,
          padding: "9px 12px",
          color: selectedIds.length > 0 ? "#E8EDF5" : "#6B7280",
          fontSize: 13,
          cursor: disabled ? "not-allowed" : "pointer",
          opacity: disabled ? 0.6 : 1,
        }}
      >
        <span>{selectedIds.length > 0 ? summaryLabel : placeholderLabel}</span>
        <ChevronDown size={14} style={{ opacity: 0.6, flexShrink: 0 }} />
      </button>
      {panel}
    </>
  );
}
