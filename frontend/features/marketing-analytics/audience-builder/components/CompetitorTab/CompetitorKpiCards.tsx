"use client";

import { useTranslations, useLocale } from "next-intl";
import type { CompetitorOverviewDto } from "../../types";

const cardStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: "18px 20px",
  display: "flex",
  flexDirection: "column",
  gap: 6,
};
const labelStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 12,
  fontWeight: 500,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
};
const valueStyle = (color?: string): React.CSSProperties => ({
  color: color ?? "#E8EDF5",
  fontSize: 24,
  fontWeight: 700,
  fontFamily: "monospace",
  lineHeight: 1.1,
});

interface Props {
  overview: CompetitorOverviewDto | undefined;
  isLoading: boolean;
}

/** НОВА АУДИТОРІЯ / ТОВАРІВ КОНКУРЕНТА / КУПЛЕНО ОДИНИЦЬ / СУМА ВИТРАТ (analysis §18).
 * `unitsPurchased`/`totalSpend` are always period-scoped regardless of horizon — only who counts
 * as "new" changes (task log 429). */
export function CompetitorKpiCards({ overview, isLoading }: Props) {
  const t = useTranslations("Dashboard.audienceBuilder.competitorKpi");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (isLoading || !overview) {
    return <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>;
  }

  return (
    <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(190px, 1fr))", gap: 12 }}>
      <div style={cardStyle}>
        <div style={labelStyle}>{t("newAudience")}</div>
        <div style={valueStyle("#F87171")}>{overview.newAudienceCount.toLocaleString(intlLocale)}</div>
      </div>
      <div style={cardStyle}>
        <div style={labelStyle}>{t("competitorItems")}</div>
        <div style={valueStyle()}>{overview.competitorItemsCount.toLocaleString(intlLocale)}</div>
      </div>
      <div style={cardStyle}>
        <div style={labelStyle}>{t("unitsPurchased")}</div>
        <div style={valueStyle("#FBBF24")}>{overview.unitsPurchased.toLocaleString(intlLocale, { maximumFractionDigits: 0 })}</div>
      </div>
      <div style={cardStyle}>
        <div style={labelStyle}>{t("totalSpend")}</div>
        <div style={valueStyle("#FBBF24")}>{overview.totalSpend.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴</div>
      </div>
    </div>
  );
}
