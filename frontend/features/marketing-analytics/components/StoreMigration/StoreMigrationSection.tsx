"use client";

import { useMemo } from "react";
import { useTranslations, useLocale } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { canExportMarketingAnalyticsPii } from "@/lib/roles";
import { useStores } from "@/features/stores/hooks/useStores";
import { useStoreMigration, useStoreMigrationCustomers } from "../../hooks/useMarketingAnalytics";
import { StoreMigrationMatrix } from "./StoreMigrationMatrix";
import { StoreMigrationCustomerTable } from "./StoreMigrationCustomerTable";
import type { MarketingAnalyticsFilters, StoreNetFlowDto } from "../../types";

interface Props {
  filters: MarketingAnalyticsFilters;
  enabled: boolean;
}

const CUSTOMER_LIST_LIMIT = 100;

const sectionTitle: React.CSSProperties = { color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0, marginBottom: 12 };

// Local per-file KpiCard — same label/value/sub/color shape as the one defined inline in
// `app/(dashboard)/marketing-analytics/page.tsx`. Duplicated rather than shared, matching this
// codebase's existing convention (price-segments/page.tsx, FrequencyKpiCards.tsx,
// AllTimeKpiCards.tsx each define their own small local copy instead of a shared component).
function KpiCard({ label, value, sub, color }: { label: string; value: string; sub?: string; color?: string }) {
  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "18px 20px",
        display: "flex",
        flexDirection: "column",
        gap: 6,
      }}
    >
      <div style={{ color: "#4B5563", fontSize: 12, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.05em" }}>
        {label}
      </div>
      <div style={{ color: color ?? "#E8EDF5", fontSize: 24, fontWeight: 700, fontFamily: "monospace", lineHeight: 1.1 }}>
        {value}
      </div>
      {sub && <div style={{ color: "#4B5563", fontSize: 11 }}>{sub}</div>}
    </div>
  );
}

function bestNetFlow(stores: StoreNetFlowDto[], direction: "gain" | "loss"): StoreNetFlowDto | null {
  if (stores.length === 0) return null;
  const extreme = stores.reduce((a, b) => (direction === "gain" ? (b.net > a.net ? b : a) : b.net < a.net ? b : a));
  if (direction === "gain" && extreme.net <= 0) return null;
  if (direction === "loss" && extreme.net >= 0) return null;
  return extreme;
}

/**
 * "Міграція покупців між закладами" section (TASK-503) — always rendered below
 * `SegmentDetailPanel`, driven by the page's existing period/store `filters`/`enabled` state.
 * No filter UI of its own (reuses `PeriodStoreFilterBar` already on the page).
 *
 * Empty-state guard: for a tenant with <=1 store this analysis is meaningless (there's no
 * cross-store data to detect, and the backend would just return zeros anyway per the TASK-502
 * handoff note) — the endpoints aren't even called in that case.
 */
export function StoreMigrationSection({ filters, enabled }: Props) {
  const t = useTranslations("Dashboard.marketingAnalytics.storeMigration");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { data: me } = useMe();
  const canExportPii = canExportMarketingAnalyticsPii(me?.role, me?.permissions);

  const { data: stores = [] } = useStores();
  const multiStore = stores.length > 1;
  const active = enabled && multiStore;

  const { data: overview, isLoading } = useStoreMigration(filters, active);
  const { data: customers, isLoading: customersLoading } = useStoreMigrationCustomers(filters, CUSTOMER_LIST_LIMIT, active);

  const bestGain = useMemo(() => bestNetFlow(overview?.netFlowByStore ?? [], "gain"), [overview]);
  const worstLoss = useMemo(() => bestNetFlow(overview?.netFlowByStore ?? [], "loss"), [overview]);

  return (
    <section>
      <h2 style={sectionTitle}>{t("title")}</h2>

      {!multiStore ? (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{t("singleStoreNotice")}</div>
      ) : isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
      ) : !overview ? (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noData")}</div>
      ) : (
        <>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: 12,
              marginBottom: 20,
            }}
          >
            <KpiCard label={t("kpi.migrated")} value={overview.migratedCustomerCount.toLocaleString(intlLocale)} color="#60A5FA" />
            <KpiCard
              label={t("kpi.migratedShare")}
              value={`${overview.migratedSharePercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%`}
              sub={t("kpi.migratedShareSub", { count: overview.activeCustomerCount.toLocaleString(intlLocale) })}
              color="#2DD4BF"
            />
            <KpiCard
              label={t("kpi.bestGain")}
              value={bestGain ? bestGain.storeName : "—"}
              sub={bestGain ? t("kpi.netCustomers", { net: `+${bestGain.net.toLocaleString(intlLocale)}` }) : undefined}
              color="#4ADE80"
            />
            <KpiCard
              label={t("kpi.worstLoss")}
              value={worstLoss ? worstLoss.storeName : "—"}
              sub={worstLoss ? t("kpi.netCustomers", { net: worstLoss.net.toLocaleString(intlLocale) }) : undefined}
              color="#F87171"
            />
          </div>

          <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
            <StoreMigrationMatrix flows={overview.flows} />
            <StoreMigrationCustomerTable
              rows={customers ?? []}
              isLoading={customersLoading}
              limit={CUSTOMER_LIST_LIMIT}
              exportContext={{ storeIds: filters.storeIds, from: overview.periodFrom, to: overview.periodTo }}
              canExportPii={canExportPii}
            />
          </div>
        </>
      )}
    </section>
  );
}
