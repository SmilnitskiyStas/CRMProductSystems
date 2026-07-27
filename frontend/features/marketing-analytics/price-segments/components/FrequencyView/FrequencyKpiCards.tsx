"use client";

import { useTranslations, useLocale } from "next-intl";
import type { FrequencyOverviewDto } from "../../types";

interface Props {
  overview: FrequencyOverviewDto;
}

function KpiCard({ label, value, sub, color }: { label: string; value: string; sub?: string; color?: string }) {
  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "18px 20px", display: "flex", flexDirection: "column", gap: 6 }}>
      <div style={{ color: "#4B5563", fontSize: 12, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.05em" }}>{label}</div>
      <div style={{ color: color ?? "#E8EDF5", fontSize: 24, fontWeight: 700, fontFamily: "monospace", lineHeight: 1.1 }}>{value}</div>
      {sub && <div style={{ color: "#4B5563", fontSize: 11 }}>{sub}</div>}
    </div>
  );
}

/**
 * "Частота та реактивація" top KPIs (analysis doc §16/§17.6). `atRiskCount` is shown with BOTH
 * denominators explicitly, side by side, never just one — task log 420 documents the competitor
 * only ever showing the arguably-misleading "% of active current buyers" figure (its numerator
 * includes Sleeping customers, who by definition have zero CURRENT-period purchases).
 */
export function FrequencyKpiCards({ overview }: Props) {
  const t = useTranslations("Dashboard.priceSegments.frequency.kpi");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const changeSign = overview.activeBuyerCountChangePercent > 0 ? "+" : "";

  return (
    <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 12 }}>
      <KpiCard
        label={t("activeBuyers")}
        value={overview.activeCurrentBuyerCount.toLocaleString(intlLocale)}
        sub={t("activeBuyersChangeSub", {
          sign: changeSign,
          percent: overview.activeBuyerCountChangePercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 }),
        })}
        color="#60A5FA"
      />
      <KpiCard
        label={t("averageFrequency")}
        value={overview.averageFrequencyCurrent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}
        sub={t("averageFrequencyPreviousSub", { value: overview.averageFrequencyPrevious.toLocaleString(intlLocale, { maximumFractionDigits: 1 }) })}
        color="#2DD4BF"
      />
      <KpiCard
        label={t("atRisk")}
        value={overview.atRiskCount.toLocaleString(intlLocale)}
        sub={t("atRiskBothDenominatorsSub", {
          unionPercent: overview.atRiskPercentOfUnionPopulation.toLocaleString(intlLocale, { maximumFractionDigits: 1 }),
          activePercent: overview.atRiskPercentOfActiveCurrentBuyers.toLocaleString(intlLocale, { maximumFractionDigits: 1 }),
        })}
        color="#F87171"
      />
      <KpiCard
        label={t("averageSpend")}
        value={`${overview.averageSpendCurrentPeriod.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`}
        sub={t("unionPopulationSub", { count: overview.unionPopulationCount.toLocaleString(intlLocale) })}
      />
    </div>
  );
}
