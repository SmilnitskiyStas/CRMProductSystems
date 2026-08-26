"use client";

import { useTranslations } from "next-intl";
import { ChevronUp, ChevronDown } from "lucide-react";

/**
 * Shared sortable-column-header button + pagination footer for the three Фаза 2 server-paginated
 * tables (PriceAudienceTable / FrequencyView/FrequencyAudienceTable / AllTimeView/
 * AllTimeCustomerTable). Not part of the original file list — a small internal helper factored
 * out to avoid tripling the same ~50 lines of header/pagination chrome three times (noted in the
 * task log per CLAUDE.md's "judgment call, implement per convention" guidance). Mirrors
 * `features/customers/components/CustomerTable.tsx`'s pagination visuals, extended with a
 * sortable-header button that table doesn't have.
 */
export function SortableHeader<TKey extends string>({
  label,
  sortKey,
  activeSort,
  activeDescending,
  onSort,
  align,
}: {
  label: string;
  sortKey: TKey;
  activeSort: TKey;
  activeDescending: boolean;
  onSort: (key: TKey) => void;
  align?: "left" | "right" | "center";
}) {
  const active = sortKey === activeSort;
  const justifyContent = align === "right" ? "flex-end" : align === "center" ? "center" : "flex-start";
  return (
    <button
      onClick={() => onSort(sortKey)}
      style={{
        display: "flex",
        alignItems: "center",
        gap: 3,
        justifyContent,
        width: "100%",
        background: "transparent",
        border: "none",
        padding: 0,
        cursor: "pointer",
        color: active ? "#9CA3AF" : "#4B5563",
        fontSize: 11,
        fontWeight: 600,
        textTransform: "uppercase",
        letterSpacing: "0.05em",
        whiteSpace: "nowrap",
      }}
    >
      {label}
      {active && (activeDescending ? <ChevronDown size={12} /> : <ChevronUp size={12} />)}
    </button>
  );
}

export function TablePaginationFooter({
  page,
  totalPages,
  totalLabel,
  onPageChange,
}: {
  page: number;
  totalPages: number;
  totalLabel: string;
  onPageChange: (p: number) => void;
}) {
  const t = useTranslations("Dashboard.priceSegments.table");

  if (totalPages <= 1) {
    return <div style={{ color: "#4B5563", fontSize: 12, marginTop: 10, textAlign: "right" }}>{totalLabel}</div>;
  }

  return (
    <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: 14 }}>
      <span style={{ color: "#4B5563", fontSize: 12 }}>{totalLabel}</span>
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
  );
}
