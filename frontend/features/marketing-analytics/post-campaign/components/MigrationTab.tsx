"use client";

import { useTranslations, useLocale } from "next-intl";
import { ArrowUpCircle, MinusCircle, ArrowDownCircle } from "lucide-react";
import { MigrationDonut } from "./MigrationDonuts";
import { TransitionMatrix } from "./TransitionMatrix";
import { RecommendationCard } from "./RecommendationCard";
import type { PostCampaignMigrationDto } from "../types";

interface Props {
  data: PostCampaignMigrationDto | undefined;
  isLoading: boolean;
}

const kpiCardStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: "14px 16px",
  display: "flex",
  alignItems: "center",
  gap: 10,
  flex: 1,
  minWidth: 150,
};

/**
 * "Міграція сегментів" tab (source doc §25): 3 movement KPIs, before/after distribution donuts,
 * transition matrix, recommendation (no AI explain button here — see `RecommendationCard`'s own
 * doc comment for why).
 */
export function MigrationTab({ data, isLoading }: Props) {
  const t = useTranslations("Dashboard.postCampaign.migrationTab");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const total = data ? data.upCount + data.stableCount + data.downCount : 0;
  const pct = (n: number) => (total > 0 ? `${Math.round((n / total) * 100)}%` : "—");

  if (isLoading || !data) {
    return <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", flexWrap: "wrap", gap: 12 }}>
        <div style={kpiCardStyle}>
          <ArrowUpCircle size={20} color="#4ADE80" />
          <div>
            <div style={{ color: "#4B5563", fontSize: 11, textTransform: "uppercase" }}>{t("up")}</div>
            <div style={{ color: "#4ADE80", fontSize: 18, fontWeight: 700, fontFamily: "monospace" }}>
              {data.upCount.toLocaleString(intlLocale)} <span style={{ color: "#6B7280", fontSize: 12 }}>({pct(data.upCount)})</span>
            </div>
          </div>
        </div>
        <div style={kpiCardStyle}>
          <MinusCircle size={20} color="#9CA3AF" />
          <div>
            <div style={{ color: "#4B5563", fontSize: 11, textTransform: "uppercase" }}>{t("stable")}</div>
            <div style={{ color: "#9CA3AF", fontSize: 18, fontWeight: 700, fontFamily: "monospace" }}>
              {data.stableCount.toLocaleString(intlLocale)} <span style={{ color: "#6B7280", fontSize: 12 }}>({pct(data.stableCount)})</span>
            </div>
          </div>
        </div>
        <div style={kpiCardStyle}>
          <ArrowDownCircle size={20} color="#F87171" />
          <div>
            <div style={{ color: "#4B5563", fontSize: 11, textTransform: "uppercase" }}>{t("down")}</div>
            <div style={{ color: "#F87171", fontSize: 18, fontWeight: 700, fontFamily: "monospace" }}>
              {data.downCount.toLocaleString(intlLocale)} <span style={{ color: "#6B7280", fontSize: 12 }}>({pct(data.downCount)})</span>
            </div>
          </div>
        </div>
      </div>

      <div style={{ display: "flex", flexWrap: "wrap", gap: 16 }}>
        <MigrationDonut title={t("beforeDonutTitle")} distribution={data.beforeDistribution} isLoading={false} />
        <MigrationDonut title={t("afterDonutTitle")} distribution={data.afterDistribution} isLoading={false} />
      </div>

      <TransitionMatrix matrix={data.matrix} isLoading={false} />

      <RecommendationCard recommendation={data.recommendation} />
    </div>
  );
}
