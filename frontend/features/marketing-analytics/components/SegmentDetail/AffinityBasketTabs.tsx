"use client";

import { useState, useEffect } from "react";
import { useTranslations, useLocale } from "next-intl";
import { ShoppingBasket, Sparkles } from "lucide-react";
import { useRfmAffinity, useRfmBasket } from "../../hooks/useMarketingAnalytics";
import type { MarketingAnalyticsFilters, RfmSegmentKey } from "../../types";
import { ExportProductPairBuyersButton, type ExportBaseContext } from "../ExportButtons";

type CrossSellTab = "affinity" | "basket";

interface Props {
  segmentKey: RfmSegmentKey;
  /** null before the user picks a top product — renders the "Оберіть товар зліва" empty state. */
  productName: string | null;
  filters: MarketingAnalyticsFilters;
  exportBase: ExportBaseContext;
}

function tabBtnStyle(active: boolean): React.CSSProperties {
  return {
    padding: "6px 14px",
    fontSize: 12,
    fontWeight: 600,
    borderRadius: 6,
    border: "1px solid #1F2937",
    cursor: "pointer",
    background: active ? "#1D3461" : "#0D1117",
    color: active ? "#93C5FD" : "#6B7280",
    transition: "background 0.1s, color 0.1s",
  };
}

/**
 * Two tabs solving different problems (RFM_ANALYSIS.md §12) — never merged into one list:
 * - Афінність (lift): who buys MORE than the segment average, over the whole period, not
 *   necessarily in the same receipt. Cross-sell between visits.
 * - Разом у чеку: literal same-receipt co-occurrence. Bundles/shelf placement.
 * Each tab is fetched lazily and only while active (the other stays cached once visited).
 */
export function AffinityBasketTabs({ segmentKey, productName, filters, exportBase }: Props) {
  const t = useTranslations("Dashboard.marketingAnalytics.crossSell");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [tab, setTab] = useState<CrossSellTab>("affinity");

  // Switching to a different anchor product should land back on Affinity rather than
  // silently staying on whatever tab was active for the PREVIOUS product.
  useEffect(() => {
    setTab("affinity");
  }, [productName]);

  const { data: affinity, isLoading: affinityLoading } = useRfmAffinity(
    segmentKey,
    productName,
    filters,
    tab === "affinity" && !!productName,
  );
  const { data: basket, isLoading: basketLoading } = useRfmBasket(
    segmentKey,
    productName,
    filters,
    tab === "basket" && !!productName,
  );

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, overflow: "hidden", display: "flex", flexDirection: "column" }}>
      <div style={{ padding: "14px 16px 12px", borderBottom: "1px solid #1F2937" }}>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: productName ? 10 : 0 }}>
          {t("title")}
        </div>
        {productName && (
          <div style={{ display: "flex", gap: 6 }}>
            <button style={tabBtnStyle(tab === "affinity")} onClick={() => setTab("affinity")}>
              {t("affinityTab")}
            </button>
            <button style={tabBtnStyle(tab === "basket")} onClick={() => setTab("basket")}>
              {t("basketTab")}
            </button>
          </div>
        )}
      </div>

      {!productName ? (
        <div
          style={{
            flex: 1,
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            gap: 10,
            padding: "40px 20px",
            color: "#4B5563",
            textAlign: "center",
            minHeight: 200,
          }}
        >
          <ShoppingBasket size={28} strokeWidth={1.5} />
          <span style={{ fontSize: 13 }}>{t("emptyNoProduct")}</span>
        </div>
      ) : (
        <div style={{ padding: "10px 16px 4px" }}>
          <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 8 }}>
            {t("anchorPrefix")} <strong style={{ color: "#9CA3AF" }}>{productName}</strong>
          </div>

          {tab === "affinity" ? (
            affinityLoading ? (
              <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>{t("loading")}</div>
            ) : !affinity || affinity.items.length === 0 ? (
              <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0", textAlign: "center" }}>{t("empty")}</div>
            ) : (
              <div style={{ maxHeight: 340, overflowY: "auto", paddingBottom: 8 }}>
                {affinity.items.map((item) => (
                  <div
                    key={item.productName}
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: 10,
                      padding: "9px 0",
                      borderBottom: "1px solid #161B26",
                    }}
                  >
                    <Sparkles size={13} color="#A78BFA" style={{ flexShrink: 0 }} />
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div style={{ color: "#D1D5DB", fontSize: 13, fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                        {item.productName}
                      </div>
                      <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
                        {t("affinityDetail", {
                          both: item.bothBuyersCount.toLocaleString(intlLocale),
                          anchorShare: item.shareAmongAnchorBuyersPercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 }),
                          segmentShare: item.shareAmongSegmentPercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 }),
                        })}
                      </div>
                    </div>
                    <div style={{ textAlign: "right", flexShrink: 0 }}>
                      <div style={{ color: "#A78BFA", fontSize: 14, fontWeight: 700, fontFamily: "monospace" }}>
                        ×{item.lift.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}
                      </div>
                      <div style={{ color: "#4B5563", fontSize: 10 }}>{t("liftLabel")}</div>
                    </div>
                    <ExportProductPairBuyersButton {...exportBase} productName={productName} pairedProductName={item.productName} />
                  </div>
                ))}
              </div>
            )
          ) : basketLoading ? (
            <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>{t("loading")}</div>
          ) : !basket || basket.items.length === 0 ? (
            <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0", textAlign: "center" }}>{t("empty")}</div>
          ) : (
            <div style={{ maxHeight: 340, overflowY: "auto", paddingBottom: 8 }}>
              {basket.items.map((item) => (
                <div
                  key={item.productName}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: 10,
                    padding: "9px 0",
                    borderBottom: "1px solid #161B26",
                  }}
                >
                  <ShoppingBasket size={13} color="#60A5FA" style={{ flexShrink: 0 }} />
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ color: "#D1D5DB", fontSize: 13, fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                      {item.productName}
                    </div>
                    <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
                      {t("basketDetail", { receipts: item.bothReceiptsCount.toLocaleString(intlLocale) })}
                      {" · "}
                      {item.barcode ?? t("noBarcode")}
                    </div>
                  </div>
                  <div style={{ textAlign: "right", flexShrink: 0 }}>
                    <div style={{ color: "#60A5FA", fontSize: 14, fontWeight: 700, fontFamily: "monospace" }}>
                      {item.togetherSharePercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%
                    </div>
                    <div style={{ color: "#4B5563", fontSize: 10 }}>{t("togetherShareLabel")}</div>
                  </div>
                  <ExportProductPairBuyersButton {...exportBase} productName={productName} pairedProductName={item.productName} />
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
