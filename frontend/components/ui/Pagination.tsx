"use client";

import { useTranslations } from "next-intl";

/**
 * Generic prev/next + "page X / Y" + total-count-label pagination footer, in the existing
 * dark inline-style theme (no shadcn dependency). Extracted from
 * `features/marketing-analytics/price-segments/components/TableControls.tsx`'s
 * `TablePaginationFooter` (kept as-is, out of scope) so lists outside marketing-analytics
 * (receipts/transfers/write-offs/stock) can share the same visuals without duplicating the
 * price-segments-specific translation namespace. Uses generic `Common.prev`/`Common.next`/
 * `Common.pageOf`/`Common.totalLabel` keys instead.
 */
export function Pagination({
  page,
  totalPages,
  totalCount,
  onPageChange,
}: {
  page: number;
  totalPages: number;
  totalCount: number;
  onPageChange: (p: number) => void;
}) {
  const t = useTranslations("Common");
  const totalLabel = t("totalLabel", { count: totalCount });

  if (totalPages <= 1) {
    return (
      <div style={{ color: "#4B5563", fontSize: 12, marginTop: 10, textAlign: "right" }}>
        {totalLabel}
      </div>
    );
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
        <span style={{ color: "#6B7280", fontSize: 12 }}>{t("pageOf", { page, totalPages })}</span>
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
