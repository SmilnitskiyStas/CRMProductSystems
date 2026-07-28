"use client";

import { useTranslations, useLocale } from "next-intl";
import type { AudienceOverviewDto } from "../../types";

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
  overview: AudienceOverviewDto | undefined;
  isLoading: boolean;
}

/** УЧАСНИКІВ / ТОВАРІВ У ВИБІРЦІ / КУПЛЕНО ОДИНИЦЬ / СУМА ВИТРАТ (analysis §14.1). The
 * itemsInSelection hint below folds in the competitor's "Запит відповідає N товарам · повний
 * список — у вкладці «Знайдені товари»" info line (analysis §13), since it needs the same
 * server-computed count this card already displays. */
export function BuyersKpiCards({ overview, isLoading }: Props) {
  const t = useTranslations("Dashboard.audienceBuilder.buyersKpi");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (isLoading || !overview) {
    return <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(190px, 1fr))", gap: 12 }}>
        <div style={cardStyle}>
          <div style={labelStyle}>{t("participants")}</div>
          <div style={valueStyle("#60A5FA")}>{overview.participantsCount.toLocaleString(intlLocale)}</div>
        </div>
        <div style={cardStyle}>
          <div style={labelStyle}>{t("itemsInSelection")}</div>
          <div style={valueStyle()}>{overview.itemsInSelectionCount.toLocaleString(intlLocale)}</div>
        </div>
        <div style={cardStyle}>
          <div style={labelStyle}>{t("unitsPurchased")}</div>
          <div style={valueStyle("#4ADE80")}>{overview.unitsPurchased.toLocaleString(intlLocale, { maximumFractionDigits: 0 })}</div>
        </div>
        <div style={cardStyle}>
          <div style={labelStyle}>{t("totalSpend")}</div>
          <div style={valueStyle("#4ADE80")}>{overview.totalSpend.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴</div>
        </div>
      </div>
      <p style={{ color: "#4B5563", fontSize: 11.5, margin: 0 }}>{t("itemsInSelectionHint")}</p>
    </div>
  );
}
