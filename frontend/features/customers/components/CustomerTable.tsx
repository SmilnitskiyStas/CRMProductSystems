"use client";

import { useEffect, useRef, useState } from "react";
import type { Customer } from "../types";

const UAH = new Intl.NumberFormat("uk-UA", { style: "currency", currency: "UAH" });

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

// ── Sub-components ─────────────────────────────────────────────────────────────

function TagBadge({ tag }: { tag: string }) {
  return (
    <span
      style={{
        background: "#0a1628",
        border: "1px solid #1D4ED8",
        borderRadius: 20,
        padding: "2px 8px",
        color: "#60A5FA",
        fontSize: 11,
        fontWeight: 500,
        whiteSpace: "nowrap",
      }}
    >
      {tag}
    </span>
  );
}

function ActionMenu({
  onEdit,
  onDelete,
}: {
  onEdit: () => void;
  onDelete: () => void;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    function handler(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [open]);

  return (
    <div ref={ref} style={{ position: "relative" }}>
      <button
        onClick={(e) => { e.stopPropagation(); setOpen((v) => !v); }}
        style={{
          background: "transparent",
          border: "1px solid #1F2937",
          borderRadius: 6,
          padding: "4px 8px",
          color: "#6B7280",
          fontSize: 14,
          cursor: "pointer",
          lineHeight: 1,
        }}
      >
        ⋯
      </button>
      {open && (
        <div
          style={{
            position: "absolute",
            right: 0,
            top: "calc(100% + 4px)",
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 8,
            zIndex: 100,
            minWidth: 140,
            overflow: "hidden",
            boxShadow: "0 4px 16px rgba(0,0,0,0.4)",
          }}
        >
          <button
            onClick={(e) => { e.stopPropagation(); setOpen(false); onEdit(); }}
            style={{
              width: "100%",
              textAlign: "left",
              background: "transparent",
              border: "none",
              padding: "9px 14px",
              color: "#9CA3AF",
              fontSize: 13,
              cursor: "pointer",
              display: "block",
            }}
            onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = "#111827"; }}
            onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = "transparent"; }}
          >
            Редагувати
          </button>
          <button
            onClick={(e) => { e.stopPropagation(); setOpen(false); onDelete(); }}
            style={{
              width: "100%",
              textAlign: "left",
              background: "transparent",
              border: "none",
              borderTop: "1px solid #1F2937",
              padding: "9px 14px",
              color: "#F87171",
              fontSize: 13,
              cursor: "pointer",
              display: "block",
            }}
            onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = "#2d0a0a"; }}
            onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = "transparent"; }}
          >
            Видалити
          </button>
        </div>
      )}
    </div>
  );
}

// ── Grid config ────────────────────────────────────────────────────────────────

const GRID = "minmax(160px,1fr) 130px 180px 160px 80px 140px 120px 60px";
const HEADERS = ["Ім'я", "Телефон", "Email", "Теги", "Замовлень", "Витрачено", "Дата реєстрації", ""];

// ── Main component ─────────────────────────────────────────────────────────────

interface Props {
  customers: Customer[];
  totalCount: number;
  page: number;
  totalPages: number;
  search: string;
  isLoading: boolean;
  onSearchChange: (v: string) => void;
  onPageChange: (p: number) => void;
  onRowClick: (c: Customer) => void;
  onEdit: (c: Customer) => void;
  onDelete: (c: Customer) => void;
}

export function CustomerTable({
  customers,
  totalCount,
  page,
  totalPages,
  search,
  isLoading,
  onSearchChange,
  onPageChange,
  onRowClick,
  onEdit,
  onDelete,
}: Props) {
  // Debounced search
  const [localSearch, setLocalSearch] = useState(search);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  function handleSearchInput(v: string) {
    setLocalSearch(v);
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => {
      onSearchChange(v);
      onPageChange(1);
    }, 300);
  }

  return (
    <div>
      {/* Search bar */}
      <div style={{ marginBottom: 16 }}>
        <input
          value={localSearch}
          onChange={(e) => handleSearchInput(e.target.value)}
          placeholder="Пошук за іменем, телефоном або email…"
          style={{
            width: "100%",
            maxWidth: 400,
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 8,
            padding: "8px 12px",
            color: "#E8EDF5",
            fontSize: 13,
            outline: "none",
            boxSizing: "border-box",
          }}
        />
      </div>

      {/* Table */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          overflow: "auto",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "grid",
            gridTemplateColumns: GRID,
            padding: "10px 16px",
            borderBottom: "1px solid #1F2937",
            background: "#0A1020",
            minWidth: 900,
          }}
        >
          {HEADERS.map((h) => (
            <div
              key={h}
              style={{
                color: "#4B5563",
                fontSize: 11,
                fontWeight: 600,
                textTransform: "uppercase",
                letterSpacing: "0.05em",
              }}
            >
              {h}
            </div>
          ))}
        </div>

        {/* Rows */}
        {isLoading ? (
          <div style={{ padding: "40px 16px", textAlign: "center", color: "#374151", fontSize: 13 }}>
            Завантаження…
          </div>
        ) : customers.length === 0 ? (
          <div style={{ padding: "40px 16px", textAlign: "center", color: "#374151", fontSize: 13 }}>
            {search ? "Нічого не знайдено" : "Клієнтів ще немає"}
          </div>
        ) : (
          customers.map((c) => (
            <div
              key={c.id}
              onClick={() => onRowClick(c)}
              style={{
                display: "grid",
                gridTemplateColumns: GRID,
                padding: "12px 16px",
                borderBottom: "1px solid #0F1924",
                cursor: "pointer",
                alignItems: "center",
                minWidth: 900,
                transition: "background 0.1s",
              }}
              onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = "#0A1628"; }}
              onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = "transparent"; }}
            >
              {/* Name */}
              <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                {c.name}
              </div>

              {/* Phone */}
              <div style={{ color: c.phone ? "#9CA3AF" : "#374151", fontSize: 12 }}>
                {c.phone ?? "—"}
              </div>

              {/* Email */}
              <div style={{ color: c.email ? "#9CA3AF" : "#374151", fontSize: 12, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                {c.email ?? "—"}
              </div>

              {/* Tags */}
              <div style={{ display: "flex", flexWrap: "wrap", gap: 4 }}>
                {c.tags.length > 0
                  ? c.tags.slice(0, 3).map((t) => <TagBadge key={t} tag={t} />)
                  : <span style={{ color: "#374151", fontSize: 12 }}>—</span>
                }
                {c.tags.length > 3 && (
                  <span style={{ color: "#4B5563", fontSize: 11 }}>+{c.tags.length - 3}</span>
                )}
              </div>

              {/* Orders count */}
              <div style={{ color: "#9CA3AF", fontSize: 13 }}>
                {c.totalOrders}
              </div>

              {/* Total spent */}
              <div style={{ color: "#4ADE80", fontSize: 13, fontWeight: 500 }}>
                {UAH.format(c.totalSpent)}
              </div>

              {/* Created at */}
              <div style={{ color: "#4B5563", fontSize: 12 }}>
                {formatDate(c.createdAt)}
              </div>

              {/* Actions */}
              <div onClick={(e) => e.stopPropagation()}>
                <ActionMenu
                  onEdit={() => onEdit(c)}
                  onDelete={() => onDelete(c)}
                />
              </div>
            </div>
          ))
        )}
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginTop: 14,
          }}
        >
          <span style={{ color: "#4B5563", fontSize: 12 }}>
            Усього: {totalCount}
          </span>
          <div style={{ display: "flex", gap: 6, alignItems: "center" }}>
            <button
              disabled={page <= 1}
              onClick={() => onPageChange(page - 1)}
              style={{
                background: "transparent",
                border: "1px solid #1F2937",
                borderRadius: 6,
                padding: "5px 12px",
                color: page <= 1 ? "#374151" : "#9CA3AF",
                fontSize: 12,
                cursor: page <= 1 ? "default" : "pointer",
              }}
            >
              ← Назад
            </button>
            <span style={{ color: "#6B7280", fontSize: 12 }}>
              {page} / {totalPages}
            </span>
            <button
              disabled={page >= totalPages}
              onClick={() => onPageChange(page + 1)}
              style={{
                background: "transparent",
                border: "1px solid #1F2937",
                borderRadius: 6,
                padding: "5px 12px",
                color: page >= totalPages ? "#374151" : "#9CA3AF",
                fontSize: 12,
                cursor: page >= totalPages ? "default" : "pointer",
              }}
            >
              Вперед →
            </button>
          </div>
        </div>
      )}

      {totalPages <= 1 && totalCount > 0 && (
        <div style={{ color: "#4B5563", fontSize: 12, marginTop: 10, textAlign: "right" }}>
          Усього: {totalCount}
        </div>
      )}
    </div>
  );
}
