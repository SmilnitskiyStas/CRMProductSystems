"use client";

import { ChevronUp, ChevronDown } from "lucide-react";

/**
 * Shared clickable sortable-column-header button, in the existing dark inline-style theme.
 * Extracted from `features/marketing-analytics/price-segments/components/TableControls.tsx`'s
 * `SortableHeader` (left as-is there, out of scope — see that file's own doc comment, same
 * "stop the bleeding here, don't churn working unrelated code" precedent as `Pagination.tsx`'s
 * extraction) so lists outside marketing-analytics (receipts/transfers/write-offs/stock/
 * locations) can reuse the same sortable-header chrome without importing across feature
 * boundaries. Same generic-over-`TKey` / chevron-on-active-column behavior as the original.
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
  align?: "left" | "right";
}) {
  const active = sortKey === activeSort;
  return (
    <button
      onClick={() => onSort(sortKey)}
      style={{
        display: "flex",
        alignItems: "center",
        gap: 3,
        justifyContent: align === "right" ? "flex-end" : "flex-start",
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
