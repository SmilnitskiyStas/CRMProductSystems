"use client";

import { useTranslations, useLocale } from "next-intl";
import { Table, type TableColumn } from "@/components/ui/Table";
import type { WorstProductsDto, WorstProductRowDto } from "../types";

interface Props {
  data: WorstProductsDto;
  /** TASK-493: row-click drill-down — reuses the exact same onRowClick/selectedProductId shape
   * as PosTopProductsTable (TASK-484) so both tables can drive one shared ProductTrendPanel
   * selection state on /analytics/pos, no new state needed. Omitted means rows render exactly as
   * before — no cursor change, no click handler attached. */
  onRowClick?: (productId: string, productName: string) => void;
  /** Currently drilled-into product, if any — reuses PosTopProductsTable's own hover color
   * (#111827) for a persistent "active row" highlight instead of introducing a new color. */
  selectedProductId?: string | null;
}

/**
 * Dead-stock counterpart to PosTopProductsTable (TASK-490's `pos/worst-products` endpoint):
 * active, on-hand-stock items sorted ascending by sales revenue, so true zero-sale products
 * surface first. currentStock is the extra column that makes a zero-revenue row actionable —
 * "N units sitting unsold" — which PosTopProductsTable has no equivalent of. No barcode column
 * (WorstProductRowDto carries no barcode field, unlike PosTopProductItem).
 * Migrated to the shared `Table` component (table-unification migration, Batch B). The leading
 * `#` row-number column occupies index 0 (same override pattern as StockTable's checkbox
 * column), pushing the name column to index 1 with an explicit `align: "left"` override.
 * isRowSelected now drives Table's canonical #0F1825 highlight instead of this file's own
 * #111827 — a deliberate consolidation onto one shared selection color across every migrated
 * table, not a functional change (hover always tracked the row already via Table's own state).
 */
export function WorstProductsTable({ data, onRowClick, selectedProductId }: Props) {
  const t = useTranslations("Dashboard.analytics.pos.worstProducts");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (!data || data.products.length === 0) {
    return (
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 10,
          padding: "20px 16px",
          color: "#4B5563",
          fontSize: 13,
          textAlign: "center",
        }}
      >
        {t("empty")}
      </div>
    );
  }

  const columns: TableColumn<WorstProductRowDto>[] = [
    {
      key: "index",
      align: "center",
      width: 32,
      cellStyle: { color: "#374151", fontFamily: "monospace" },
      header: "#",
      render: (_item, index) => index + 1,
    },
    {
      key: "name",
      align: "left",
      header: t("headers.name"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (item) => item.productName,
    },
    {
      key: "revenue",
      header: t("headers.revenue"),
      cellStyle: { color: "#4ADE80", fontFamily: "monospace" },
      render: (item) => `${item.salesRevenue.toLocaleString(intlLocale)} ₴`,
    },
    {
      key: "quantity",
      header: t("headers.quantity"),
      cellStyle: { fontFamily: "monospace" },
      render: (item) => item.unitsSold.toLocaleString(intlLocale),
    },
    {
      key: "receipts",
      header: t("headers.receipts"),
      cellStyle: { fontFamily: "monospace" },
      render: (item) => item.transactionCount.toLocaleString(intlLocale),
    },
    {
      key: "currentStock",
      header: t("headers.currentStock"),
      // Same amber the rest of this feature already uses for "warning"-class status counts
      // (see CategoryDetailPanel.tsx's p.warning cell) rather than a brand-new color.
      cellStyle: { color: "#FBBF24", fontWeight: 600, fontFamily: "monospace" },
      render: (item) => item.currentStock.toLocaleString(intlLocale),
    },
  ];

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, overflow: "hidden" }}>
      <div style={{ padding: "16px 16px 12px", borderBottom: "1px solid #1F2937" }}>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{t("title")}</div>
      </div>
      <Table
        columns={columns}
        rows={data.products}
        rowKey={(item) => item.productId}
        onRowClick={onRowClick ? (item) => onRowClick(item.productId, item.productName) : undefined}
        isRowSelected={(item) => !!onRowClick && selectedProductId === item.productId}
      />
    </div>
  );
}
