"use client";

import { useTranslations, useLocale } from "next-intl";
import { TrendingUp, TrendingDown, Minus } from "lucide-react";
import type { PostCampaignSummaryDto } from "../types";

interface Props {
  summary: PostCampaignSummaryDto | undefined;
  isLoading: boolean;
}

const cardStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: "16px 18px",
  display: "flex",
  flexDirection: "column",
  gap: 6,
};
const labelStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 11.5,
  fontWeight: 500,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
};
const valueStyle = (color?: string): React.CSSProperties => ({
  color: color ?? "#E8EDF5",
  fontSize: 22,
  fontWeight: 700,
  fontFamily: "monospace",
  lineHeight: 1.1,
});

/** null → "н/д" (never "0%") — mirrors `PostCampaignBehaviorClassifier`'s own division-by-zero
 * contract (source doc §36.7). Positive = green/up, negative = red/down, zero = neutral/flat —
 * same raw-sign convention as `price-segments/components/AllTimeView/AllTimeKpiCards.tsx`'s
 * `InsightChip` (this codebase's actual precedent for a value+direction+color indicator). */
function DeltaBadge({ value }: { value: number | null }) {
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const t = useTranslations("Dashboard.postCampaign.topKpi");

  if (value === null) {
    return <span style={{ color: "#4B5563", fontSize: 12.5, fontWeight: 600 }}>{t("notAvailable")}</span>;
  }
  const color = value > 0 ? "#4ADE80" : value < 0 ? "#F87171" : "#9CA3AF";
  const Icon = value > 0 ? TrendingUp : value < 0 ? TrendingDown : Minus;
  return (
    <span style={{ display: "inline-flex", alignItems: "center", gap: 4, color, fontSize: 12.5, fontWeight: 600 }}>
      <Icon size={12} />
      {value > 0 ? "+" : ""}
      {value.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%
    </span>
  );
}

/**
 * 5 top KPI cards (source doc §11): Гроші після / Реактивовані / Утримані / Відпали / Не
 * повернулись. The 5th card is a deliberate fix over the competitor per brief item 5 / source doc
 * §31.10 ("Не повернулись видно лише нижче" is flagged as a weakness) — this app shows it here too,
 * not only in the Overview donut.
 */
export function TopKpiCards({ summary, isLoading }: Props) {
  const t = useTranslations("Dashboard.postCampaign.topKpi");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const notAvailable = t("notAvailable");
  const fmtPct = (v: number | null) => (v === null ? notAvailable : `${v.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%`);

  if (isLoading || !summary) {
    return <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>;
  }

  return (
    <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(190px, 1fr))", gap: 12 }}>
      <div style={cardStyle}>
        <div style={labelStyle}>{t("moneyAfter")}</div>
        <div style={valueStyle("#4ADE80")}>
          {summary.moneyAfter.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴
        </div>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <span style={{ color: "#6B7280", fontSize: 11.5 }}>{t("buyersAfter", { count: summary.buyersAfter })}</span>
          <DeltaBadge value={summary.moneyDeltaPercent} />
        </div>
      </div>

      <div style={cardStyle}>
        <div style={labelStyle}>{t("reactivated")}</div>
        <div style={valueStyle("#2DD4BF")}>{summary.reactivated.toLocaleString(intlLocale)}</div>
        <div style={{ color: "#6B7280", fontSize: 11.5 }}>
          {summary.inactiveBefore === 0 ? t("allActiveBefore") : t("reactivationRate", { rate: fmtPct(summary.reactivationRatePercent) })}
        </div>
      </div>

      <div style={cardStyle}>
        <div style={labelStyle}>{t("retained")}</div>
        <div style={valueStyle("#4ADE80")}>{summary.retained.toLocaleString(intlLocale)}</div>
        <div style={{ color: "#6B7280", fontSize: 11.5 }}>{t("retentionRate", { rate: fmtPct(summary.retentionRatePercent) })}</div>
      </div>

      <div style={cardStyle}>
        <div style={labelStyle}>{t("dropped")}</div>
        <div style={valueStyle("#F87171")}>{summary.dropped.toLocaleString(intlLocale)}</div>
        <div style={{ color: "#6B7280", fontSize: 11.5 }}>{t("churnRate", { rate: fmtPct(summary.churnRatePercent) })}</div>
      </div>

      <div style={cardStyle}>
        <div style={labelStyle}>{t("notReturned")}</div>
        <div style={valueStyle("#6B7280")}>{summary.notReturned.toLocaleString(intlLocale)}</div>
        <div style={{ color: "#4B5563", fontSize: 11.5 }}>{t("notReturnedHint")}</div>
      </div>
    </div>
  );
}
