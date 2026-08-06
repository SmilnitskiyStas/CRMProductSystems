"use client";

import { useTranslations, useLocale } from "next-intl";
import { TrendingUp, TrendingDown, Minus } from "lucide-react";
import type { PostCampaignRfmActivityDto } from "../types";

interface Props {
  data: PostCampaignRfmActivityDto | undefined;
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

/**
 * Raw sign of the change → icon (Up/Down/Minus); "goodness" of that change → color. For
 * checks/turnover/average-check, higher is better (raw sign === goodness). For recency, LOWER is
 * better (source doc §22: "тут менше — краще") — `lowerIsBetter` flips only the color, never the
 * icon, so a negative recency delta still shows a down-arrow (the number genuinely went down) but
 * in green (that IS the good outcome) — brief's explicit "positive-color/down-arrow" convention.
 * Same raw-sign icon selection as `price-segments/components/AllTimeView/AllTimeKpiCards.tsx`'s
 * `InsightChip` (this codebase's actual precedent for a delta indicator — `BehaviorPanel.tsx`,
 * cited in the original brief, turned out on inspection to show plain windowed stats with no
 * before/after comparison at all, so it carries no delta convention of its own to follow; noted in
 * the task log).
 */
function DeltaBadge({ value, lowerIsBetter, notAvailable }: { value: number | null; lowerIsBetter?: boolean; notAvailable: string }) {
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (value === null) {
    return <span style={{ color: "#4B5563", fontSize: 12.5, fontWeight: 600 }}>{notAvailable}</span>;
  }
  const isGood = lowerIsBetter ? value < 0 : value > 0;
  const isBad = lowerIsBetter ? value > 0 : value < 0;
  const color = isGood ? "#4ADE80" : isBad ? "#F87171" : "#9CA3AF";
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
 * 4 R/F/M activity stat cards (source doc §18/§19/§20/§21/§22): Чеки / Оборот / Середній чек /
 * Давність — each shows the AFTER value + delta% vs before. Every *After/*DeltaPercent field can
 * be null (division-by-zero contract, source doc §36.7) — rendered as "н/д", never "0%"/"0".
 */
export function RfmActivityCards({ data, isLoading }: Props) {
  const t = useTranslations("Dashboard.postCampaign.rfmActivity");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const notAvailable = t("notAvailable");

  if (isLoading || !data) {
    return <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: 12 }}>
        <div style={cardStyle}>
          <div style={labelStyle}>{t("checks")}</div>
          <div style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, fontFamily: "monospace" }}>{data.checksAfter.toLocaleString(intlLocale)}</div>
          <DeltaBadge value={data.checksDeltaPercent} notAvailable={notAvailable} />
        </div>

        <div style={cardStyle}>
          <div style={labelStyle}>{t("turnover")}</div>
          <div style={{ color: "#2DD4BF", fontSize: 22, fontWeight: 700, fontFamily: "monospace" }}>
            {data.turnoverAfter.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴
          </div>
          <DeltaBadge value={data.turnoverDeltaPercent} notAvailable={notAvailable} />
        </div>

        <div style={cardStyle}>
          <div style={labelStyle}>{t("averageCheck")}</div>
          <div style={{ color: data.averageCheckAfter === null ? "#374151" : "#E8EDF5", fontSize: 22, fontWeight: 700, fontFamily: "monospace" }}>
            {data.averageCheckAfter === null ? "—" : `${data.averageCheckAfter.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`}
          </div>
          <DeltaBadge value={data.averageCheckDeltaPercent} notAvailable={notAvailable} />
        </div>

        <div style={cardStyle}>
          <div style={labelStyle}>{t("recency")}</div>
          <div style={{ color: data.recencyAfterDays === null ? "#374151" : "#E8EDF5", fontSize: 22, fontWeight: 700, fontFamily: "monospace" }}>
            {data.recencyAfterDays === null ? "—" : t("daysSuffix", { n: data.recencyAfterDays.toLocaleString(intlLocale, { maximumFractionDigits: 1 }) })}
          </div>
          <DeltaBadge value={data.recencyDeltaPercent} lowerIsBetter notAvailable={notAvailable} />
        </div>
      </div>

      {/* Transparency footnote (source doc §22.1: "показувати знаменник і правило для клієнтів
          без історії") — how many segment members actually had a purchase in each window at all,
          since customers with none are excluded from the recency average rather than defaulted. */}
      <p style={{ color: "#4B5563", fontSize: 11, margin: 0 }}>
        {t("recencyDenominatorFootnote", { before: data.recencyDenominatorBefore, after: data.recencyDenominatorAfter })}
      </p>
    </div>
  );
}
