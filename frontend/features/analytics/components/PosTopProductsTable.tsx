"use client";

import { useTranslations, useLocale } from "next-intl";
import { Table, type TableColumn } from "@/components/ui/Table";
import type { PosTopProductsDto, PosTopProductItem } from "../types";

interface Props {
  data: PosTopProductsDto;
  /** TASK-484: row-click drill-down — opens ProductTrendPanel inline on /analytics/pos (renamed
   * from PosProductTrendPanel in TASK-488 when /analytics gained its own drill-down path to the
   * same panel). Omitted (as at the /analytics/pos day-detail composition, PosDayDetailPanel.tsx)
   * means rows render exactly as before — no cursor change, no click handler attached. */
  onRowClick?: (productId: string, productName: string) => void;
  /** Currently drilled-into product, if any — reuses this table's own existing hover color
   * (#111827) for a persistent "active row" highlight instead of introducing a new color. */
  selectedProductId?: string | null;
}

export function PosTopProductsTable({ data, onRowClick, selectedProductId }: Props) {
  const t = useTranslations("Dashboard.analytics.pos.topProducts");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (!data || data.items.length === 0) {
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

  const columns: TableColumn<PosTopProductItem>[] = [
    {
      key: "index",
      header: "#",
      width: 32,
      align: "center",
      cellStyle: { color: "#374151", fontFamily: "monospace" },
      render: (_item, idx) => idx + 1,
    },
    { key: "name", header: t("headers.name"), cellStyle: { color: "#E8EDF5", fontWeight: 500 }, render: (item) => item.productName },
    { key: "barcode", header: t("headers.barcode"), align: "left", cellStyle: { color: "#6B7280", fontFamily: "monospace" }, render: (item) => item.barcode },
    {
      key: "revenue",
      header: t("headers.revenue"),
      align: "right",
      cellStyle: { color: "#4ADE80", fontFamily: "monospace" },
      render: (item) => `${item.totalRevenue.toLocaleString(intlLocale)} ₴`,
    },
    {
      key: "quantity",
      header: t("headers.quantity"),
      align: "right",
      cellStyle: { fontFamily: "monospace" },
      render: (item) => item.totalQuantity.toLocaleString(intlLocale),
    },
    {
      key: "receipts",
      header: t("headers.receipts"),
      align: "right",
      cellStyle: { fontFamily: "monospace" },
      render: (item) => item.transactionCount.toLocaleString(intlLocale),
    },
  ];

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{t("title")}</div>
      <Table
        columns={columns}
        rows={data.items}
        rowKey={(item) => item.productId}
        onRowClick={onRowClick ? (item) => onRowClick(item.productId, item.productName) : undefined}
        isRowSelected={(item) => !!onRowClick && selectedProductId === item.productId}
      />
    </div>
  );
}
