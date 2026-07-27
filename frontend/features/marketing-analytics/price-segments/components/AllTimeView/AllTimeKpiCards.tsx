"use client";

import { useTranslations, useLocale } from "next-intl";
import { TrendingUp, TrendingDown, Minus } from "lucide-react";
import type { PriceSegmentsAllTimeOverviewDto } from "../../types";

interface Props {
  overview: PriceSegmentsAllTimeOverviewDto;
}

function KpiCard({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "18px 20px", display: "flex", flexDirection: "column", gap: 6 }}>
      <div style={{ color: "#4B5563", fontSize: 12, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.05em" }}>{label}</div>
      <div style={{ color: color ?? "#E8EDF5", fontSize: 24, fontWeight: 700, fontFamily: "monospace", lineHeight: 1.1 }}>{value}</div>
    </div>
  );
}

function InsightChip({ label, value, direction }: { label: string; value: string; direction: "up" | "down" | "flat" }) {
  const color = direction === "up" ? "#4ADE80" : direction === "down" ? "#F87171" : "#9CA3AF";
  const Icon = direction === "up" ? TrendingUp : direction === "down" ? TrendingDown : Minus;
  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, padding: "10px 14px", display: "flex", alignItems: "center", gap: 8 }}>
      <Icon size={14} color={color} style={{ flexShrink: 0 }} />
      <div>
        <div style={{ color: "#4B5563", fontSize: 10.5, textTransform: "uppercase", letterSpacing: "0.03em" }}>{label}</div>
        <div style={{ color, fontSize: 14, fontWeight: 700, fontFamily: "monospace" }}>{value}</div>
      </div>
    </div>
  );
}

/**
 * "Весь час" KPIs (analysis doc §11/§12): 4 primary all-time stats — note `networkAverageCheck`
 * is the network's ARITHMETIC mean check (`turnover/purchases`), deliberately NOT the per-customer
 * MEDIAN "typical check" used everywhere else in this feature (task log 420 / analysis doc §11
 * warns explicitly against conflating the two) — plus a row of auto-insights (§12.1-12.4), each
 * independently nullable: a brand-new tenant won't have 2+ comparable history points for months
 * or years yet, and null means "not enough history", never zero.
 */
export function AllTimeKpiCards({ overview }: Props) {
  const t = useTranslations("Dashboard.priceSegments.allTime.kpi");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { insights } = overview;
  const hasAnyInsight =
    insights.yoyPercent !== null ||
    insights.last3MonthsTrendPercent !== null ||
    insights.belowPeakPercent !== null ||
    insights.itemsPerReceiptChangePercent !== null;

  function pct(v: number, digits = 1): string {
    const sign = v > 0 ? "+" : "";
    return `${sign}${v.toLocaleString(intlLocale, { maximumFractionDigits: digits })}%`;
  }
  function trendDirection(v: number): "up" | "down" | "flat" {
    if (v > 0) return "up";
    if (v < 0) return "down";
    return "flat";
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: 12 }}>
        <KpiCard label={t("customersInBase")} value={overview.customersInBase.toLocaleString(intlLocale)} color="#60A5FA" />
        <KpiCard
          label={t("networkAverageCheck")}
          value={`${overview.networkAverageCheck.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`}
          color="#4ADE80"
        />
        <KpiCard label={t("purchasesTotal")} value={overview.purchasesTotal.toLocaleString(intlLocale)} />
        <KpiCard label={t("turnoverTotal")} value={`${overview.turnoverTotal.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`} color="#2DD4BF" />
      </div>

      {hasAnyInsight ? (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: 10 }}>
          {insights.yoyPercent !== null && (
            <InsightChip label={t("yoy")} value={pct(insights.yoyPercent)} direction={trendDirection(insights.yoyPercent)} />
          )}
          {insights.last3MonthsTrendPercent !== null && (
            <InsightChip label={t("last3Months")} value={pct(insights.last3MonthsTrendPercent)} direction={trendDirection(insights.last3MonthsTrendPercent)} />
          )}
          {insights.itemsPerReceiptChangePercent !== null && (
            <InsightChip
              label={t("itemsPerReceiptChange")}
              value={pct(insights.itemsPerReceiptChangePercent)}
              direction={trendDirection(insights.itemsPerReceiptChangePercent)}
            />
          )}
          {insights.belowPeakPercent !== null && (
            <InsightChip
              label={t("belowPeak", {
                peak: insights.historicalPeakMedianCheck?.toLocaleString(intlLocale, { maximumFractionDigits: 0 }) ?? "—",
              })}
              value={pct(insights.belowPeakPercent)}
              // belowPeakPercent is (latest/peak - 1)*100 — mathematically never positive (peak IS
              // the historical max); 0 means "at the all-time peak right now", which is good news.
              direction={insights.belowPeakPercent === 0 ? "up" : "down"}
            />
          )}
        </div>
      ) : (
        <p style={{ color: "#4B5563", fontSize: 12, margin: 0 }}>{t("noInsightsYet")}</p>
      )}
    </div>
  );
}
