"use client";

import { useTranslations, useLocale } from "next-intl";
import { PieChart, Pie, Cell, Tooltip, ResponsiveContainer } from "recharts";
import type { PostCampaignSummaryDto } from "../types";

interface Props {
  summary: PostCampaignSummaryDto | undefined;
  isLoading: boolean;
}

// Reuses the exact same 4 semantic colors already established elsewhere in this app's RFM color
// language (`features/marketing-analytics/types.ts`'s RFM_COLOR_PALETTE: positive/stable/risk/
// dormant) rather than inventing a new behavior-status palette — reactivated=positive(teal),
// retained=stable(green), dropped=risk(red), notReturned=dormant(gray).
const STATUS_COLORS = {
  reactivated: "#2DD4BF",
  retained: "#4ADE80",
  dropped: "#F87171",
  notReturned: "#6B7280",
} as const;

/**
 * "Статус сегмента" donut (source doc §16) — center shows the full analyzed segment size, legend
 * lists only NON-ZERO categories (source doc's explicit rule), each with count + share of the
 * full segment (one decimal).
 */
export function SegmentStatusDonut({ summary, isLoading }: Props) {
  const t = useTranslations("Dashboard.postCampaign.statusDonut");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (isLoading || !summary) {
    return (
      <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "16px 16px" }}>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 12 }}>{t("title")}</div>
        <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
      </div>
    );
  }

  const total = summary.matchedCount;
  const items = [
    { key: "reactivated", label: t("reactivated"), value: summary.reactivated, color: STATUS_COLORS.reactivated },
    { key: "retained", label: t("retained"), value: summary.retained, color: STATUS_COLORS.retained },
    { key: "dropped", label: t("dropped"), value: summary.dropped, color: STATUS_COLORS.dropped },
    { key: "notReturned", label: t("notReturned"), value: summary.notReturned, color: STATUS_COLORS.notReturned },
  ].filter((i) => i.value > 0);

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "16px 16px" }}>
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 4 }}>{t("title")}</div>
      <div style={{ color: "#6B7280", fontSize: 11.5, marginBottom: 8 }}>{t("subtitle")}</div>

      {total === 0 ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "24px 0", textAlign: "center" }}>{t("empty")}</div>
      ) : (
        <div style={{ display: "flex", alignItems: "center", gap: 20, flexWrap: "wrap" }}>
          <div style={{ position: "relative", width: 160, height: 160, flexShrink: 0 }}>
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie data={items} dataKey="value" nameKey="label" innerRadius={48} outerRadius={72} paddingAngle={items.length > 1 ? 2 : 0} stroke="none">
                  {items.map((i) => (
                    <Cell key={i.key} fill={i.color} />
                  ))}
                </Pie>
                <Tooltip
                  contentStyle={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, color: "#E8EDF5", fontSize: 12 }}
                  formatter={(value, name) => [`${Number(value).toLocaleString(intlLocale)}`, name]}
                />
              </PieChart>
            </ResponsiveContainer>
            <div style={{ position: "absolute", inset: 0, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", pointerEvents: "none" }}>
              <span style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, fontFamily: "monospace" }}>{total.toLocaleString(intlLocale)}</span>
              <span style={{ color: "#4B5563", fontSize: 10, textTransform: "uppercase" }}>{t("centerLabel")}</span>
            </div>
          </div>

          <div style={{ display: "flex", flexDirection: "column", gap: 8, flex: 1, minWidth: 160 }}>
            {items.map((i) => (
              <div key={i.key} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 10 }}>
                <span style={{ display: "flex", alignItems: "center", gap: 7 }}>
                  <span style={{ width: 9, height: 9, borderRadius: "50%", background: i.color, flexShrink: 0 }} />
                  <span style={{ color: "#9CA3AF", fontSize: 12.5 }}>{i.label}</span>
                </span>
                <span style={{ color: "#E8EDF5", fontSize: 12.5, fontFamily: "monospace" }}>
                  {i.value.toLocaleString(intlLocale)}{" "}
                  <span style={{ color: "#4B5563" }}>({((i.value / total) * 100).toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%)</span>
                </span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
