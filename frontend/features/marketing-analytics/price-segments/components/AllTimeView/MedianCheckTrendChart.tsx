"use client";

import { useTranslations, useLocale } from "next-intl";
import { LineChart, Line, XAxis, YAxis, Tooltip, Legend, CartesianGrid, ResponsiveContainer } from "recharts";
import type { MedianCheckMonthPointDto } from "../../types";

interface Props {
  monthlyTrend: MedianCheckMonthPointDto[];
}

const MONTH_KEYS = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

/**
 * Monthly all-time median-check trend (analysis doc §12) with a second line for items/receipt on
 * its own right-hand axis — the competitor's own chart subtitle ties the two together explicitly
 * ("і чи беруть клієнти більше товарів у чеку"), so this isn't just a price trend in isolation.
 * A brand-new tenant (or one filtered to a tiny store selection) may have zero monthly points —
 * rendered as an explicit empty state rather than a broken/blank chart.
 */
export function MedianCheckTrendChart({ monthlyTrend }: Props) {
  const t = useTranslations("Dashboard.priceSegments.allTime.trendChart");
  const tMonth = useTranslations("Dashboard.priceSegments.allTime.trendChart.months");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const data = monthlyTrend.map((p) => ({
    name: `${tMonth(MONTH_KEYS[p.month - 1] ?? "jan")} ${String(p.year).slice(2)}`,
    medianCheck: p.medianCheck,
    itemsPerReceipt: p.itemsPerReceipt,
  }));

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "16px 16px 8px" }}>
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 4 }}>{t("title")}</div>
      <div style={{ color: "#6B7280", fontSize: 11.5, marginBottom: 12 }}>{t("subtitle")}</div>

      {data.length === 0 ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "24px 0", textAlign: "center" }}>{t("empty")}</div>
      ) : (
        <ResponsiveContainer width="100%" height={280}>
          <LineChart data={data} margin={{ left: 0, right: 8, top: 4, bottom: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#161B26" vertical={false} />
            <XAxis dataKey="name" tick={{ fill: "#6B7280", fontSize: 10.5 }} axisLine={false} tickLine={false} interval="preserveStartEnd" />
            <YAxis yAxisId="check" tick={{ fill: "#4B5563", fontSize: 11 }} axisLine={false} tickLine={false} width={44} />
            <YAxis yAxisId="items" orientation="right" tick={{ fill: "#4B5563", fontSize: 11 }} axisLine={false} tickLine={false} width={36} />
            <Tooltip
              contentStyle={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, color: "#E8EDF5", fontSize: 12 }}
              formatter={(value, name) => [
                name === "medianCheck"
                  ? `${Number(value).toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`
                  : Number(value).toLocaleString(intlLocale, { maximumFractionDigits: 1 }),
                name === "medianCheck" ? t("medianCheckSeries") : t("itemsPerReceiptSeries"),
              ]}
            />
            <Legend
              formatter={(value) => (value === "medianCheck" ? t("medianCheckSeries") : t("itemsPerReceiptSeries"))}
              wrapperStyle={{ fontSize: 12, color: "#9CA3AF" }}
            />
            <Line yAxisId="check" type="monotone" dataKey="medianCheck" name="medianCheck" stroke="#3B82F6" strokeWidth={2} dot={false} />
            <Line yAxisId="items" type="monotone" dataKey="itemsPerReceipt" name="itemsPerReceipt" stroke="#2DD4BF" strokeWidth={2} dot={false} />
          </LineChart>
        </ResponsiveContainer>
      )}
    </div>
  );
}
