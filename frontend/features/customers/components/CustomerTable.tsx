"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import type { Customer } from "../types";
import { Table, type TableColumn } from "@/components/ui/Table";

function formatDate(iso: string, intlLocale: string) {
  return new Date(iso).toLocaleDateString(intlLocale, {
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
  const t = useTranslations("Dashboard.customers.table");
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
            {t("edit")}
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
            {t("delete")}
          </button>
        </div>
      )}
    </div>
  );
}

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
  const t = useTranslations("Dashboard.customers.table");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const uah = new Intl.NumberFormat(intlLocale, { style: "currency", currency: "UAH" });

  const columns: TableColumn<Customer>[] = [
    {
      key: "name",
      header: t("headerName"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500, maxWidth: 220, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" },
      render: (c) => c.name,
    },
    {
      key: "phone",
      header: t("headerPhone"),
      render: (c) => (
        <span style={{ color: c.phone ? "#9CA3AF" : "#374151", fontSize: 12 }}>
          {c.phone ?? "—"}
        </span>
      ),
    },
    {
      key: "email",
      header: t("headerEmail"),
      cellStyle: { maxWidth: 200, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" },
      render: (c) => (
        <span style={{ color: c.email ? "#9CA3AF" : "#374151", fontSize: 12 }}>
          {c.email ?? "—"}
        </span>
      ),
    },
    {
      key: "tags",
      header: t("headerTags"),
      render: (c) => (
        <div style={{ display: "inline-flex", flexWrap: "wrap", gap: 4 }}>
          {c.tags.length > 0
            ? c.tags.slice(0, 3).map((tag) => <TagBadge key={tag} tag={tag} />)
            : <span style={{ color: "#374151", fontSize: 12 }}>—</span>
          }
          {c.tags.length > 3 && (
            <span style={{ color: "#4B5563", fontSize: 11 }}>+{c.tags.length - 3}</span>
          )}
        </div>
      ),
    },
    {
      key: "orders",
      header: t("headerOrders"),
      render: (c) => <span style={{ color: "#9CA3AF", fontSize: 13 }}>{c.totalOrders}</span>,
    },
    {
      key: "spent",
      header: t("headerSpent"),
      render: (c) => (
        <span style={{ color: "#4ADE80", fontSize: 13, fontWeight: 500 }}>
          {uah.format(c.totalSpent)}
        </span>
      ),
    },
    {
      key: "createdAt",
      header: t("headerCreatedAt"),
      render: (c) => (
        <span style={{ color: "#4B5563", fontSize: 12 }}>{formatDate(c.createdAt, intlLocale)}</span>
      ),
    },
    {
      key: "actions",
      header: "",
      render: (c) => (
        <div onClick={(e) => e.stopPropagation()}>
          <ActionMenu
            onEdit={() => onEdit(c)}
            onDelete={() => onDelete(c)}
          />
        </div>
      ),
    },
  ];

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
          placeholder={t("searchPlaceholder")}
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
      <Table
        columns={columns}
        rows={customers}
        rowKey={(c) => c.id}
        onRowClick={onRowClick}
        isLoading={isLoading}
        minWidth={900}
        emptyMessage={isLoading ? t("loading") : (search ? t("emptySearch") : t("emptyNone"))}
      />

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
            {t("totalLabel", { count: totalCount })}
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
              {t("prev")}
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
              {t("next")}
            </button>
          </div>
        </div>
      )}

      {totalPages <= 1 && totalCount > 0 && (
        <div style={{ color: "#4B5563", fontSize: 12, marginTop: 10, textAlign: "right" }}>
          {t("totalLabel", { count: totalCount })}
        </div>
      )}
    </div>
  );
}
