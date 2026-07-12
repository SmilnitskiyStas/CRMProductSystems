"use client";

import { useEffect, useState } from "react";
import { X, Search, RotateCcw } from "lucide-react";
import { useUsers } from "@/features/users/hooks/useUsers";
import { useLocations } from "@/features/locations/hooks/useLocations";
import type { NotificationHistoryFilters, NotificationEventType } from "../types";
import { EVENT_TYPE_LABELS } from "../types";

interface Props {
  open: boolean;
  onClose: () => void;
  filters: NotificationHistoryFilters;
  onChange: (updater: (prev: NotificationHistoryFilters) => NotificationHistoryFilters) => void;
}

const EVENT_TYPE_ENTRIES = Object.entries(EVENT_TYPE_LABELS) as [NotificationEventType, string][];

const labelStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 12,
  marginBottom: 6,
  display: "block",
};

const fieldStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "9px 12px",
  outline: "none",
  width: "100%",
  boxSizing: "border-box",
};

const selectStyle: React.CSSProperties = {
  ...fieldStyle,
  appearance: "none",
  cursor: "pointer",
};

/**
 * Hand-rolled overlay filter panel — follows the same fixed-panel + backdrop
 * pattern as NotificationDetailDrawer.tsx (no shadcn Sheet primitive in this repo).
 */
export function NotificationFilterDrawer({ open, onClose, filters, onChange }: Props) {
  // Local draft for the search box only — debounced before it reaches `filters`
  // so we don't refetch on every keystroke. Other fields apply immediately.
  const [searchDraft, setSearchDraft] = useState(filters.search ?? "");

  // Keep the draft in sync when the panel (re)opens or filters are reset externally.
  useEffect(() => {
    if (open) setSearchDraft(filters.search ?? "");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const handle = setTimeout(() => {
      onChange((prev) => {
        const next = searchDraft.trim() || undefined;
        if (prev.search === next) return prev;
        return { ...prev, search: next, page: 1 };
      });
    }, 300);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchDraft, open]);

  useEffect(() => {
    if (!open) return;
    const handler = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [open, onClose]);

  const { data: users } = useUsers();
  const { data: locations } = useLocations();

  if (!open) return null;

  function set<K extends keyof NotificationHistoryFilters>(key: K, value: NotificationHistoryFilters[K]) {
    onChange((prev) => ({ ...prev, [key]: value, page: 1 }));
  }

  function handleReset() {
    setSearchDraft("");
    onChange((prev) => ({ page: 1, pageSize: prev.pageSize }));
  }

  const hasActiveFilters = Boolean(
    filters.search || filters.eventType || filters.userId || filters.storeId || filters.dateFrom || filters.dateTo,
  );

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,0.5)", zIndex: 40 }}
      />

      {/* Panel */}
      <div
        style={{
          position: "fixed", top: 0, right: 0, bottom: 0,
          width: 380, maxWidth: "100vw",
          background: "#0D1117",
          borderLeft: "1px solid #1F2937",
          zIndex: 50,
          display: "flex", flexDirection: "column",
          overflowY: "auto",
        }}
      >
        {/* Header */}
        <div style={{
          padding: "20px 24px 16px",
          borderBottom: "1px solid #1F2937",
          display: "flex", alignItems: "center", justifyContent: "space-between",
          flexShrink: 0,
        }}>
          <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>
            Фільтри
          </div>
          <button
            onClick={onClose}
            style={{
              background: "transparent", border: "1px solid #1F2937",
              borderRadius: 8, padding: "6px 8px", cursor: "pointer", color: "#6B7280",
              display: "flex", alignItems: "center",
            }}
          >
            <X size={16} />
          </button>
        </div>

        {/* Body */}
        <div style={{ padding: 24, display: "flex", flexDirection: "column", gap: 18, flex: 1 }}>
          {/* Search */}
          <div>
            <label style={labelStyle}>Пошук за ключовим словом</label>
            <div style={{ position: "relative" }}>
              <Search size={14} style={{ position: "absolute", left: 12, top: "50%", transform: "translateY(-50%)", color: "#4B5563" }} />
              <input
                type="text"
                value={searchDraft}
                onChange={(e) => setSearchDraft(e.target.value)}
                placeholder="Наприклад: молоко, договір…"
                style={{ ...fieldStyle, paddingLeft: 34 }}
              />
            </div>
          </div>

          {/* Event type */}
          <div>
            <label style={labelStyle}>Категорія</label>
            <select
              value={filters.eventType ?? ""}
              onChange={(e) => set("eventType", (e.target.value as NotificationEventType) || undefined)}
              style={selectStyle}
            >
              <option value="" style={{ background: "#0D1117" }}>Усі категорії</option>
              {EVENT_TYPE_ENTRIES.map(([value, label]) => (
                <option key={value} value={value} style={{ background: "#0D1117" }}>
                  {label}
                </option>
              ))}
            </select>
          </div>

          {/* Employee */}
          <div>
            <label style={labelStyle}>Працівник</label>
            <select
              value={filters.userId ?? ""}
              onChange={(e) => set("userId", e.target.value || undefined)}
              style={selectStyle}
            >
              <option value="" style={{ background: "#0D1117" }}>Усі працівники</option>
              {users?.map((u) => (
                <option key={u.id} value={u.id} style={{ background: "#0D1117" }}>
                  {u.fullName}
                </option>
              ))}
            </select>
          </div>

          {/* Store */}
          <div>
            <label style={labelStyle}>Магазин</label>
            <select
              value={filters.storeId ?? ""}
              onChange={(e) => set("storeId", e.target.value || undefined)}
              style={selectStyle}
            >
              <option value="" style={{ background: "#0D1117" }}>Усі магазини</option>
              {locations?.map((l) => (
                <option key={l.id} value={l.id} style={{ background: "#0D1117" }}>
                  {l.name}
                </option>
              ))}
            </select>
          </div>

          {/* Date range */}
          <div>
            <label style={labelStyle}>Період</label>
            <div style={{ display: "flex", gap: 10 }}>
              <div style={{ flex: 1 }}>
                <label style={{ ...labelStyle, fontSize: 11 }}>З дати</label>
                <input
                  type="date"
                  value={filters.dateFrom?.slice(0, 10) ?? ""}
                  max={filters.dateTo?.slice(0, 10)}
                  onChange={(e) => set("dateFrom", e.target.value || undefined)}
                  style={fieldStyle}
                />
              </div>
              <div style={{ flex: 1 }}>
                <label style={{ ...labelStyle, fontSize: 11 }}>По дату</label>
                <input
                  type="date"
                  value={filters.dateTo?.slice(0, 10) ?? ""}
                  min={filters.dateFrom?.slice(0, 10)}
                  onChange={(e) => set("dateTo", e.target.value ? `${e.target.value}T23:59:59.999` : undefined)}
                  style={fieldStyle}
                />
              </div>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div style={{
          padding: "16px 24px", borderTop: "1px solid #1F2937", flexShrink: 0,
        }}>
          <button
            onClick={handleReset}
            disabled={!hasActiveFilters && !searchDraft}
            style={{
              display: "flex", alignItems: "center", justifyContent: "center", gap: 6,
              width: "100%",
              background: "transparent", border: "1px solid #1F2937",
              borderRadius: 8, padding: "9px 12px",
              color: "#6B7280", fontSize: 13,
              cursor: hasActiveFilters || searchDraft ? "pointer" : "default",
              opacity: hasActiveFilters || searchDraft ? 1 : 0.5,
            }}
          >
            <RotateCcw size={13} />
            Скинути фільтри
          </button>
        </div>
      </div>
    </>
  );
}
