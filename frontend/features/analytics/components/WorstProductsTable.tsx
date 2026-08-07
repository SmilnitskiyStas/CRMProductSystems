"use client";

import { useTranslations, useLocale } from "next-intl";
import type { WorstProductsDto } from "../types";

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
// Distinct from tdRevenue's green — this is the "evidence" column (units sitting unsold), styled
// with the same amber the rest of this feature already uses for "warning"-class status counts
// (see CategoryDetailPanel.tsx's p.warning cell) rather than a brand-new color.
const tdStock: React.CSSProperties = { ...tdNum, color: "#FBBF24", fontWeight: 600 };

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

/**
 * Dead-stock counterpart to PosTopProductsTable (TASK-490's `pos/worst-products` endpoint):
 * active, on-hand-stock items sorted ascending by sales revenue, so true zero-sale products
 * surface first. currentStock is the extra column that makes a zero-revenue row actionable —
 * "N units sitting unsold" — which PosTopProductsTable has no equivalent of. No barcode column
 * (WorstProductRowDto carries no barcode field, unlike PosTopProductItem).
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
              <th style={{ ...thStyle(), textAlign: "right" }}>{t("headers.revenue")}</th>
              <th style={{ ...thStyle(), textAlign: "right" }}>{t("headers.quantity")}</th>
              <th style={{ ...thStyle(), textAlign: "right" }}>{t("headers.receipts")}</th>
              <th style={{ ...thStyle(), textAlign: "right" }}>{t("headers.currentStock")}</th>
            </tr>
          </thead>
          <tbody>
            {data.products.map((item, idx) => {
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
                  <td style={tdRevenue}>{item.salesRevenue.toLocaleString(intlLocale)} ₴</td>
                  <td style={tdNum}>{item.unitsSold.toLocaleString(intlLocale)}</td>
                  <td style={tdNum}>{item.transactionCount.toLocaleString(intlLocale)}</td>
                  <td style={tdStock}>{item.currentStock.toLocaleString(intlLocale)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
