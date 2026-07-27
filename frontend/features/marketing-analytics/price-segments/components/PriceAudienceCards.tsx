"use client";

import { useTranslations, useLocale } from "next-intl";
import type { PriceAudienceSummaryDto, PriceAudienceKey } from "../types";
import { PRICE_AUDIENCE_COLORS } from "../types";

interface Props {
  audiences: PriceAudienceSummaryDto[];
  selectedAudience: PriceAudienceKey | null;
  onSelect: (audience: PriceAudienceKey) => void;
}

/**
 * 4 clickable audience cards — RealGrowth / PriceGrowth / Declining / Stable. Stable gets full
 * parity here on purpose (analysis doc §7.4/§25.3: the competitor has a KPI for it but no card,
 * list, or export at all — a documented gap this app closes). Clicking toggles the table below
 * (the page owns the toggle logic — "click the active card again to collapse", same convention
 * as `features/marketing-analytics/components/SegmentGrid.tsx`).
 */
export function PriceAudienceCards({ audiences, selectedAudience, onSelect }: Props) {
  const t = useTranslations("Dashboard.priceSegments.audienceCards");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  return (
    <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(230px, 1fr))", gap: 12 }}>
      {audiences.map((a) => {
        const active = a.audience === selectedAudience;
        const c = PRICE_AUDIENCE_COLORS[a.audience];
        return (
          <button
            key={a.audience}
            onClick={() => onSelect(a.audience)}
            style={{
              textAlign: "left",
              cursor: "pointer",
              background: active ? c.bg : "#0D1117",
              border: `1px solid ${active ? c.accent : "#1F2937"}`,
              borderRadius: 10,
              padding: "14px 16px",
              display: "flex",
              flexDirection: "column",
              gap: 8,
              transition: "border-color 0.1s, background 0.1s",
            }}
          >
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 8 }}>
              <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 700, lineHeight: 1.3 }}>{a.labelUa}</span>
              <span style={{ width: 8, height: 8, borderRadius: "50%", background: c.accent, flexShrink: 0, marginTop: 4 }} />
            </div>

            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", marginTop: 2 }}>
              <span style={{ color: "#E8EDF5", fontSize: 19, fontWeight: 700, fontFamily: "monospace" }}>
                {a.customerCount.toLocaleString(intlLocale)}
              </span>
              <span style={{ color: c.accent, fontSize: 12, fontWeight: 600 }}>
                {a.sharePercentOfAnalyzed.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%
              </span>
            </div>

            <div style={{ height: 4, borderRadius: 2, background: "#161B26", overflow: "hidden" }}>
              <div
                style={{
                  height: "100%",
                  width: `${Math.min(100, a.sharePercentOfAnalyzed)}%`,
                  background: c.accent,
                }}
              />
            </div>

            <div style={{ display: "flex", justifyContent: "space-between", fontSize: 11, marginTop: 2 }}>
              <span style={{ color: "#4B5563" }}>{t("ltvLabel")}</span>
              <span style={{ color: "#9CA3AF", fontFamily: "monospace" }}>
                {a.averageLtv.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴
              </span>
            </div>
          </button>
        );
      })}

      {audiences.length > 0 && (
        <p style={{ gridColumn: "1 / -1", color: "#4B5563", fontSize: 12, margin: "2px 0 0" }}>{t("percentBasisHint")}</p>
      )}
    </div>
  );
}
