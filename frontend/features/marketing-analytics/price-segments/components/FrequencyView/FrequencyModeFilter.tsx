"use client";

import type { CSSProperties } from "react";
import { useTranslations, useLocale } from "next-intl";
import type { FrequencyAudienceSummaryDto, FrequencyAudienceKey, PriceSegmentKey } from "../../types";
import { FREQUENCY_AUDIENCE_COLORS, ALL_PRICE_TIERS } from "../../types";

interface TierOption {
  segment: PriceSegmentKey;
  rangeLabelUa: string;
}

interface Props {
  audiences: FrequencyAudienceSummaryDto[];
  selectedAudience: FrequencyAudienceKey | null;
  onSelectAudience: (a: FrequencyAudienceKey) => void;
  /** Shown/editable ONLY when Declining is selected (analysis doc §17.2: the field only appears
   * once "Купують рідше" is the active filter) — the value itself is still sent on every
   * overview/table request regardless of which audience is currently displayed, since it also
   * reshapes the top-level Declining/Other KPI split. */
  declineThresholdPercent: number | undefined;
  onDeclineThresholdChange: (v: number | undefined) => void;
  minSpend: number | undefined;
  onMinSpendChange: (v: number | undefined) => void;
  maxSpend: number | undefined;
  onMaxSpendChange: (v: number | undefined) => void;
  priceSegmentFilter: PriceSegmentKey | undefined;
  onPriceSegmentFilterChange: (v: PriceSegmentKey | undefined) => void;
  /** Real ₴ range labels for the price-segment select, sourced from the All-time distribution
   * (same live network-wide tier boundaries regardless of mode, design doc §1.2) — undefined
   * while that hasn't loaded yet, in which case the bare tier key is shown as a placeholder. */
  tierOptions: TierOption[] | undefined;
}

const inputStyle: CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 10px",
  outline: "none",
};
const labelStyle: CSSProperties = { color: "#4B5563", fontSize: 12, marginBottom: 4, display: "block" };

/**
 * Frequency & reactivation's audience picker (4 clickable cards — Sleeping/Declining/Growing/
 * Other, analysis doc §17) PLUS the audience-scoped extra filters (§17.2/§19/§20). For Sleeping,
 * the min/max-spend and price-segment filter LABELS flip to their previous-period wording (task
 * log 420: the backend already re-orients these three filters against the customer's PREVIOUS
 * period for Sleeping — current spend/segment is always 0/undefined by definition) — this only
 * needs a label change on the frontend, not new logic, and deliberately does NOT disable the
 * fields the way the competitor's own equivalent effectively breaks per analysis doc §19.2/§20.2.
 */
export function FrequencyModeFilter({
  audiences,
  selectedAudience,
  onSelectAudience,
  declineThresholdPercent,
  onDeclineThresholdChange,
  minSpend,
  onMinSpendChange,
  maxSpend,
  onMaxSpendChange,
  priceSegmentFilter,
  onPriceSegmentFilterChange,
  tierOptions,
}: Props) {
  const t = useTranslations("Dashboard.priceSegments.frequency.filter");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const isSleeping = selectedAudience === "Sleeping";

  function tierLabel(key: PriceSegmentKey): string {
    return tierOptions?.find((o) => o.segment === key)?.rangeLabelUa ?? key;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(220px, 1fr))", gap: 12 }}>
        {audiences.map((a) => {
          const active = a.audience === selectedAudience;
          const c = FREQUENCY_AUDIENCE_COLORS[a.audience];
          return (
            <button
              key={a.audience}
              onClick={() => onSelectAudience(a.audience)}
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
                <span style={{ color: "#E8EDF5", fontSize: 19, fontWeight: 700, fontFamily: "monospace" }}>{a.customerCount.toLocaleString(intlLocale)}</span>
                <span style={{ color: c.accent, fontSize: 12, fontWeight: 600 }}>
                  {a.sharePercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%
                </span>
              </div>

              <div style={{ display: "flex", justifyContent: "space-between", fontSize: 11, marginTop: 2 }}>
                <span style={{ color: "#4B5563" }}>{t("ltvLabel")}</span>
                <span style={{ color: "#9CA3AF", fontFamily: "monospace" }}>{a.averageLtv.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴</span>
              </div>
            </button>
          );
        })}
      </div>

      {selectedAudience && (
        <div
          style={{
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 10,
            padding: "14px 16px",
            display: "flex",
            flexWrap: "wrap",
            gap: 16,
            alignItems: "flex-end",
          }}
        >
          {selectedAudience === "Declining" && (
            <div>
              <label style={labelStyle}>{t("declineThresholdLabel")}</label>
              <input
                type="number"
                min={1}
                max={100}
                value={declineThresholdPercent ?? ""}
                placeholder={t("declineThresholdPlaceholder")}
                onChange={(e) => onDeclineThresholdChange(e.target.value === "" ? undefined : Number(e.target.value))}
                style={{ ...inputStyle, width: 110 }}
              />
            </div>
          )}

          <div>
            <label style={labelStyle}>{isSleeping ? t("minSpendLabelPrevious") : t("minSpendLabelCurrent")}</label>
            <input
              type="number"
              min={0}
              value={minSpend ?? ""}
              onChange={(e) => onMinSpendChange(e.target.value === "" ? undefined : Number(e.target.value))}
              style={{ ...inputStyle, width: 140 }}
            />
          </div>
          <div>
            <label style={labelStyle}>{isSleeping ? t("maxSpendLabelPrevious") : t("maxSpendLabelCurrent")}</label>
            <input
              type="number"
              min={0}
              value={maxSpend ?? ""}
              onChange={(e) => onMaxSpendChange(e.target.value === "" ? undefined : Number(e.target.value))}
              style={{ ...inputStyle, width: 140 }}
            />
          </div>
          <div>
            <label style={labelStyle}>{isSleeping ? t("priceSegmentLabelPrevious") : t("priceSegmentLabelCurrent")}</label>
            <select
              value={priceSegmentFilter ?? ""}
              onChange={(e) => onPriceSegmentFilterChange(e.target.value === "" ? undefined : (e.target.value as PriceSegmentKey))}
              style={{ ...inputStyle, width: 170 }}
            >
              <option value="">{t("allSegmentsOption")}</option>
              {ALL_PRICE_TIERS.map((tier) => (
                <option key={tier} value={tier}>
                  {tierLabel(tier)}
                </option>
              ))}
            </select>
          </div>
        </div>
      )}
    </div>
  );
}
