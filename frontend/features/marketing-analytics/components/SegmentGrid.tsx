"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import type { RfmOverviewDto, RfmSegmentKey, RfmColorTokens } from "../types";
import { RFM_SEGMENT_COLOR_GROUP, RFM_COLOR_PALETTE, RFM_CANNOT_LOSE_THEM_ACCENT } from "../types";

interface Props {
  overview: RfmOverviewDto;
  selectedKey: RfmSegmentKey | null;
  onSelect: (key: RfmSegmentKey) => void;
}

function colorFor(key: RfmSegmentKey): RfmColorTokens {
  const base = RFM_COLOR_PALETTE[RFM_SEGMENT_COLOR_GROUP[key]];
  // CannotLoseThem (former Champions, most urgent) gets a darker red than AtRisk — same
  // two-intensity convention features/analytics already uses for critical vs. expired.
  return key === "CannotLoseThem" ? { ...base, accent: RFM_CANNOT_LOSE_THEM_ACCENT } : base;
}

/**
 * 11 segment cards + a separate "no purchase" card (RFM_ANALYSIS.md §7/§8). Clicking a card
 * toggles the detail panel BELOW this grid — this component never hides itself, it only
 * reports the selection up via onSelect (the page decides what "same key clicked again"
 * means). The "no purchase" card has no click handler at all: there's no segment-detail
 * endpoint for it (it isn't one of the 11 RFM segments).
 */
export function SegmentGrid({ overview, selectedKey, onSelect }: Props) {
  const t = useTranslations("Dashboard.marketingAnalytics.segmentGrid");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [hoveredKey, setHoveredKey] = useState<string | null>(null);

  return (
    <div>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(230px, 1fr))", gap: 12 }}>
        {overview.segments.map((seg) => {
          const active = seg.key === selectedKey;
          const hovered = seg.key === hoveredKey;
          const c = colorFor(seg.key);
          return (
            <button
              key={seg.key}
              onClick={() => onSelect(seg.key)}
              onMouseEnter={() => setHoveredKey(seg.key)}
              onMouseLeave={() => setHoveredKey(null)}
              style={{
                textAlign: "left",
                cursor: "pointer",
                background: active ? c.bg : hovered ? "#111827" : "#0D1117",
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
                <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 700, lineHeight: 1.3 }}>{seg.labelUa}</span>
                <span style={{ width: 8, height: 8, borderRadius: "50%", background: c.accent, flexShrink: 0, marginTop: 4 }} />
              </div>
              <span style={{ color: "#6B7280", fontSize: 11, lineHeight: 1.4, minHeight: 30 }}>{seg.shortDescriptionUa}</span>

              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", marginTop: 2 }}>
                <span style={{ color: "#E8EDF5", fontSize: 19, fontWeight: 700, fontFamily: "monospace" }}>
                  {seg.customerCount.toLocaleString(intlLocale)}
                </span>
                <span style={{ color: c.accent, fontSize: 12, fontWeight: 600 }}>
                  {seg.sharePercentOfPeriodCustomers.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%
                </span>
              </div>

              <div style={{ height: 4, borderRadius: 2, background: "#161B26", overflow: "hidden" }}>
                <div
                  style={{
                    height: "100%",
                    width: `${Math.min(100, seg.sharePercentOfPeriodCustomers)}%`,
                    background: c.accent,
                  }}
                />
              </div>

              <div style={{ display: "flex", justifyContent: "space-between", fontSize: 11, marginTop: 2 }}>
                <span style={{ color: "#4B5563" }}>{t("revenueLabel")}</span>
                <span style={{ color: "#9CA3AF", fontFamily: "monospace" }}>
                  {seg.revenue.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴{" "}
                  <span style={{ color: "#4B5563" }}>
                    ({seg.sharePercentOfPeriodRevenue.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%)
                  </span>
                </span>
              </div>
            </button>
          );
        })}

        {/* "Без покупок" — separate, non-clickable (RFM_ANALYSIS.md §5: not one of the 11 transactional segments). */}
        <div
          style={{
            background: "#0D1117",
            border: "1px dashed #374151",
            borderRadius: 10,
            padding: "14px 16px",
            display: "flex",
            flexDirection: "column",
            gap: 8,
          }}
        >
          <span style={{ color: "#9CA3AF", fontSize: 13, fontWeight: 700 }}>{t("noPurchaseTitle")}</span>
          <span style={{ color: "#4B5563", fontSize: 11, lineHeight: 1.4, minHeight: 30 }}>{t("noPurchaseDescription")}</span>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", marginTop: 2 }}>
            <span style={{ color: "#9CA3AF", fontSize: 19, fontWeight: 700, fontFamily: "monospace" }}>
              {overview.noPurchase.customerCount.toLocaleString(intlLocale)}
            </span>
            <span style={{ color: "#6B7280", fontSize: 12, fontWeight: 600 }}>
              {overview.noPurchase.sharePercentOfRegisteredBase.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%
            </span>
          </div>
          <span style={{ color: "#4B5563", fontSize: 10 }}>{t("noPurchaseHint")}</span>
        </div>
      </div>

      {!selectedKey && <p style={{ color: "#4B5563", fontSize: 12, marginTop: 14, marginBottom: 0 }}>{t("hint")}</p>}
    </div>
  );
}
