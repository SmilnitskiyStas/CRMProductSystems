"use client";

import { useTranslations, useLocale } from "next-intl";
import type { RfmTopProductDto } from "../../types";
import { ExportProductBuyersButton, type ExportBaseContext } from "../ExportButtons";

interface Props {
  products: RfmTopProductDto[];
  isLoading: boolean;
  selectedProductName: string | null;
  onSelectProduct: (productName: string) => void;
  exportBase: ExportBaseContext;
}

/**
 * Top products ranked by COVERAGE (% of segment customers who bought it), not sales volume —
 * RFM_ANALYSIS.md §11.1 is explicit that this is the whole point (a small group of very
 * frequent buyers must not be able to push a product up the ranking). Coverage% is shown
 * first/most prominently for that reason. Backend already sorts by rank; this panel just
 * renders in the given order.
 */
export function TopProductsPanel({ products, isLoading, selectedProductName, onSelectProduct, exportBase }: Props) {
  const t = useTranslations("Dashboard.marketingAnalytics.topProducts");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, overflow: "hidden" }}>
      <div style={{ padding: "14px 16px 12px", borderBottom: "1px solid #1F2937" }}>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{t("title")}</div>
      </div>

      {isLoading ? (
        <div style={{ padding: "20px 16px", color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
      ) : products.length === 0 ? (
        <div style={{ padding: "20px 16px", color: "#4B5563", fontSize: 13, textAlign: "center" }}>{t("empty")}</div>
      ) : (
        <div style={{ maxHeight: 420, overflowY: "auto" }}>
          {products.map((p) => {
            const active = p.productName === selectedProductName;
            return (
              <div
                key={`${p.rank}-${p.productName}`}
                onClick={() => onSelectProduct(p.productName)}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 12,
                  padding: "10px 16px",
                  borderBottom: "1px solid #161B26",
                  background: active ? "#0F1F3D" : "transparent",
                  borderLeft: active ? "2px solid #3B82F6" : "2px solid transparent",
                  cursor: "pointer",
                  transition: "background 0.1s",
                }}
                onMouseEnter={(e) => {
                  if (!active) (e.currentTarget as HTMLElement).style.background = "#111827";
                }}
                onMouseLeave={(e) => {
                  if (!active) (e.currentTarget as HTMLElement).style.background = "transparent";
                }}
              >
                <div
                  style={{
                    width: 22,
                    height: 22,
                    borderRadius: 6,
                    flexShrink: 0,
                    background: "#161B26",
                    color: "#6B7280",
                    fontSize: 11,
                    fontWeight: 700,
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                  }}
                >
                  {p.rank}
                </div>

                <div style={{ flex: 1, minWidth: 0 }}>
                  <div
                    style={{
                      color: active ? "#E8EDF5" : "#D1D5DB",
                      fontSize: 13,
                      fontWeight: active ? 600 : 500,
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {p.productName}
                  </div>
                  <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
                    {t("buyersAndReceipts", {
                      buyers: p.uniqueCustomerCount.toLocaleString(intlLocale),
                      receipts: p.receiptCount.toLocaleString(intlLocale),
                    })}
                    {" · "}
                    {p.barcode ?? t("noBarcode")}
                  </div>
                </div>

                <div style={{ textAlign: "right", flexShrink: 0 }}>
                  <div style={{ color: "#2DD4BF", fontSize: 15, fontWeight: 700, fontFamily: "monospace" }}>
                    {p.coveragePercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%
                  </div>
                  <div style={{ color: "#4B5563", fontSize: 10 }}>{t("coverageLabel")}</div>
                </div>

                <ExportProductBuyersButton {...exportBase} productName={p.productName} />
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
