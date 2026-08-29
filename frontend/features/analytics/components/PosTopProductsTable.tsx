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

const ROW_BORDER = "1px solid #1F2937";

const baseTd: React.CSSProperties = {
  padding: "10px 16px",
  fontSize: 13,
  borderBottom: ROW_BORDER,
  borderRight: "1px solid #1F2937",
};

const tdText: React.CSSProperties = { ...baseTd, color: "#E8EDF5", fontWeight: 500 };
const tdMuted: React.CSSProperties = { ...baseTd, color: "#6B7280", fontFamily: "monospace" };
const tdNum: React.CSSProperties = { ...baseTd, color: "#9CA3AF", fontFamily: "monospace", textAlign: "right" };
const tdRevenue: React.CSSProperties = { ...tdNum, color: "#4ADE80" };

function thStyle(): React.CSSProperties {
  return {
    padding: "10px 16px",
    color: "#4B5563",
    fontSize: 11,
    fontWeight: 600,
    textTransform: "uppercase",
    letterSpacing: "0.05em",
    borderBottom: "1px solid #374151",
    borderRight: "1px solid #374151",
    background: "#0A0F1A",
    textAlign: "left",
  };
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

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, overflow: "hidden" }}>
      <div style={{ padding: "16px 16px 12px", borderBottom: "1px solid #1F2937" }}>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{t("title")}</div>
      </div>
      <div style={{ overflowX: "auto" }}>
        <table style={{ width: "100%", borderCollapse: "collapse" }}>
          <thead>
            <tr>
              <th style={{ ...thStyle(), width: 32, textAlign: "center" }}>#</th>
              <th style={thStyle()}>{t("headers.name")}</th>
              <th style={thStyle()}>{t("headers.barcode")}</th>
              <th style={{ ...thStyle(), textAlign: "right" }}>{t("headers.revenue")}</th>
              <th style={{ ...thStyle(), textAlign: "right" }}>{t("headers.quantity")}</th>
              <th style={{ ...thStyle(), textAlign: "right" }}>{t("headers.receipts")}</th>
            </tr>
          </thead>
          <tbody>
            {data.items.map((item, idx) => {
              const isSelected = !!onRowClick && selectedProductId === item.productId;
              return (
                <tr
                  key={item.productId}
                  onClick={onRowClick ? () => onRowClick(item.productId, item.productName) : undefined}
                  style={{
                    transition: "background 0.1s",
                    cursor: onRowClick ? "pointer" : undefined,
                    background: isSelected ? "#111827" : "transparent",
                  }}
                  onMouseEnter={(e) => ((e.currentTarget as HTMLElement).style.background = "#111827")}
                  onMouseLeave={(e) => ((e.currentTarget as HTMLElement).style.background = isSelected ? "#111827" : "transparent")}
                >
                  <td style={{ ...tdMuted, textAlign: "center", color: "#374151" }}>{idx + 1}</td>
                  <td style={tdText}>{item.productName}</td>
                  <td style={tdMuted}>{item.barcode}</td>
                  <td style={tdRevenue}>{item.totalRevenue.toLocaleString(intlLocale)} ₴</td>
                  <td style={tdNum}>{item.totalQuantity.toLocaleString(intlLocale)}</td>
                  <td style={tdNum}>{item.transactionCount.toLocaleString(intlLocale)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
