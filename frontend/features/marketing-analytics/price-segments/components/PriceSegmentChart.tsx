"use client";

import { useTranslations, useLocale } from "next-intl";
import { BarChart, Bar, XAxis, YAxis, Tooltip, Legend, CartesianGrid, ResponsiveContainer } from "recharts";
import type { PriceSegmentDistributionPointDto } from "../types";

interface Props {
  distribution: PriceSegmentDistributionPointDto[];
}

/** 7 tiers, current vs previous period — grouped bars (analysis doc §6.2). Always renders all 7
 * tiers even when a tier has zero customers in both periods (backend guarantees exactly 7
 * entries). Sum of the "current" series equals `currentPeriodBuyerCount`, NOT `analyzedCount` —
 * see the KPI footnote in page.tsx for why those two numbers legitimately differ. */
export function PriceSegmentChart({ distribution }: Props) {
  const t = useTranslations("Dashboard.priceSegments.chart");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const data = distribution.map((d) => ({
    name: d.rangeLabelUa,
    current: d.currentCount,
    previous: d.previousCount,
  }));

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "16px 16px 8px" }}>
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 12 }}>{t("title")}</div>
      <ResponsiveContainer width="100%" height={260}>
        <BarChart data={data} margin={{ left: 0, right: 8, top: 4, bottom: 0 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#161B26" vertical={false} />
          <XAxis dataKey="name" tick={{ fill: "#6B7280", fontSize: 11 }} axisLine={false} tickLine={false} />
          <YAxis tick={{ fill: "#4B5563", fontSize: 11 }} axisLine={false} tickLine={false} />
          <Tooltip
            contentStyle={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, color: "#E8EDF5", fontSize: 12 }}
            formatter={(value, name) => [
              Number(value).toLocaleString(intlLocale),
              name === "current" ? t("currentSeries") : t("previousSeries"),
            ]}
            cursor={{ fill: "rgba(255,255,255,0.03)" }}
          />
          <Legend
            formatter={(value) => (value === "current" ? t("currentSeries") : t("previousSeries"))}
            wrapperStyle={{ fontSize: 12, color: "#9CA3AF" }}
          />
          <Bar dataKey="current" name="current" fill="#3B82F6" radius={[4, 4, 0, 0]} maxBarSize={28} />
          <Bar dataKey="previous" name="previous" fill="#374151" radius={[4, 4, 0, 0]} maxBarSize={28} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
