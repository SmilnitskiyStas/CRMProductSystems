"use client";

import { useTranslations, useLocale } from "next-intl";
import { BarChart, Bar, XAxis, YAxis, Tooltip, Cell, CartesianGrid, ResponsiveContainer } from "recharts";
import type { AllTimeSegmentDistributionDto, PriceSegmentKey } from "../../types";

interface Props {
  distribution: AllTimeSegmentDistributionDto[];
  selectedSegment: PriceSegmentKey | null;
  onSelect: (segment: PriceSegmentKey | null) => void;
}

const ACTIVE_COLOR = "#3B82F6";
const INACTIVE_COLOR = "#374151";

/**
 * Whole-base distribution across all 7 price tiers (analysis doc §13). Clickable bars AND a
 * duplicate row of tier buttons below them, both driving the SAME onSelect — the analysis doc
 * documents the competitor offering exactly both entry points ("Дублюючі кнопки сегментів
 * розміщені нижче графіка"), the buttons being the more discoverable/accessible one since hovering
 * a bar to find it's clickable isn't obvious. Clicking the already-selected bar/button again
 * clears back to "Всі" — the competitor has no such toggle-off; this closes that gap the same way
 * PriceAudienceCards already does for the comparison tab.
 */
export function SegmentDistributionChart({ distribution, selectedSegment, onSelect }: Props) {
  const t = useTranslations("Dashboard.priceSegments.allTime.distributionChart");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const data = distribution.map((d) => ({
    segment: d.segment,
    name: d.rangeLabelUa,
    customerCount: d.customerCount,
    averageLtv: d.averageLtv,
  }));

  function toggle(segment: PriceSegmentKey) {
    onSelect(selectedSegment === segment ? null : segment);
  }

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "16px 16px 12px" }}>
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 2 }}>{t("title")}</div>
      <div style={{ color: "#6B7280", fontSize: 11.5, marginBottom: 12 }}>{t("subtitle")}</div>
      <ResponsiveContainer width="100%" height={240}>
        <BarChart data={data} margin={{ left: 0, right: 8, top: 4, bottom: 0 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#161B26" vertical={false} />
          <XAxis dataKey="name" tick={{ fill: "#6B7280", fontSize: 11 }} axisLine={false} tickLine={false} />
          <YAxis tick={{ fill: "#4B5563", fontSize: 11 }} axisLine={false} tickLine={false} />
          <Tooltip
            contentStyle={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, color: "#E8EDF5", fontSize: 12 }}
            formatter={(value, name) => [
              name === "customerCount"
                ? Number(value).toLocaleString(intlLocale)
                : `${Number(value).toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`,
              name === "customerCount" ? t("customersSeries") : t("ltvSeries"),
            ]}
            cursor={{ fill: "rgba(255,255,255,0.03)" }}
          />
          <Bar
            dataKey="customerCount"
            name="customerCount"
            radius={[4, 4, 0, 0]}
            maxBarSize={40}
            onClick={(entry) => toggle(entry.payload.segment as PriceSegmentKey)}
            style={{ cursor: "pointer" }}
          >
            {data.map((d) => (
              <Cell key={d.segment} fill={selectedSegment === null || selectedSegment === d.segment ? ACTIVE_COLOR : INACTIVE_COLOR} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>

      <div style={{ display: "flex", flexWrap: "wrap", gap: 6, marginTop: 14 }}>
        <button
          onClick={() => onSelect(null)}
          style={{
            padding: "6px 12px",
            fontSize: 12,
            fontWeight: 600,
            borderRadius: 6,
            border: "1px solid #1F2937",
            cursor: "pointer",
            background: selectedSegment === null ? "#1D3461" : "#0D1117",
            color: selectedSegment === null ? "#93C5FD" : "#6B7280",
          }}
        >
          {t("allSegmentsButton")}
        </button>
        {distribution.map((d) => (
          <button
            key={d.segment}
            onClick={() => toggle(d.segment)}
            style={{
              padding: "6px 12px",
              fontSize: 12,
              fontWeight: 600,
              borderRadius: 6,
              border: "1px solid #1F2937",
              cursor: "pointer",
              background: selectedSegment === d.segment ? "#1D3461" : "#0D1117",
              color: selectedSegment === d.segment ? "#93C5FD" : "#6B7280",
            }}
          >
            {d.rangeLabelUa}
          </button>
        ))}
      </div>
    </div>
  );
}
