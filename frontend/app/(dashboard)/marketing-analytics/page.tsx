"use client";

import { useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { useQueryClient } from "@tanstack/react-query";
import { Info } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_VIEW_ANALYTICS, hasRole } from "@/lib/roles";
import { useRequireTab } from "@/lib/useRequireTab";
import { useModules } from "@/features/modules/hooks/useModules";
import { useMarketingAnalyticsOverview } from "@/features/marketing-analytics/hooks/useMarketingAnalytics";
import { PeriodStoreFilterBar } from "@/features/marketing-analytics/components/PeriodStoreFilterBar";
import { SegmentGrid } from "@/features/marketing-analytics/components/SegmentGrid";
import { SegmentDetailPanel } from "@/features/marketing-analytics/components/SegmentDetail/SegmentDetailPanel";
import { StoreMigrationSection } from "@/features/marketing-analytics/components/StoreMigration/StoreMigrationSection";
import type {
  MarketingAnalyticsFilters,
  MarketingAnalyticsPeriodPreset,
  RfmSegmentKey,
} from "@/features/marketing-analytics/types";

function toDateStr(d: Date): string {
  return d.toISOString().slice(0, 10);
}
function defaultCustomFrom(): string {
  const d = new Date();
  d.setDate(d.getDate() - 30);
  return toDateStr(d);
}
function defaultCustomTo(): string {
  return toDateStr(new Date());
}

const sectionTitle: React.CSSProperties = { color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0, marginBottom: 12 };

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

export default function MarketingAnalyticsPage() {
  const t = useTranslations("Dashboard.marketingAnalytics.page");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const queryClient = useQueryClient();

  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, CAN_VIEW_ANALYTICS) : null;
  // Mirrors Sidebar.tsx's "marketing_analytics" NavGroup gate (item key "/marketing-analytics",
  // group key "marketing_analytics") — same pattern as /analytics's own useRequireTab call.
  const effectiveAccess = useRequireTab("/marketing-analytics", "marketing_analytics", roleAccess === true);
  const access = roleAccess === null ? null : effectiveAccess;

  // Module gate (marketing_analytics) — disabled while role access hasn't resolved yet, so
  // this never fires for a user who's about to see AccessDenied anyway. `modulesData`
  // undefined (loading, or role not yet resolved) defaults `moduleActive` to true to avoid a
  // flash of the lock screen before the real value arrives — same convention as
  // auto-service/marketplace pages.
  const { data: modulesData } = useModules(access === true);
  const moduleActive = !modulesData || modulesData.modules.includes("marketing_analytics");

  const [period, setPeriod] = useState<MarketingAnalyticsPeriodPreset>("6m");
  const [customFrom, setCustomFrom] = useState<string>(defaultCustomFrom);
  const [customTo, setCustomTo] = useState<string>(defaultCustomTo);
  const [storeIds, setStoreIds] = useState<string[]>([]);
  const [selectedSegment, setSelectedSegment] = useState<RfmSegmentKey | null>(null);

  // The one filter object every query in this feature keys off of (per the brief) — any
  // change here (period, custom dates, or stores) is a brand-new React Query key, so a
  // filter switch can never show a mix of old and new numbers anywhere on the page.
  const filters: MarketingAnalyticsFilters = useMemo(
    () => ({
      period,
      from: period === "custom" ? customFrom : undefined,
      to: period === "custom" ? customTo : undefined,
      storeIds,
    }),
    [period, customFrom, customTo, storeIds],
  );

  const enabled = access === true && moduleActive;
  const { data: overview, isLoading, isFetching } = useMarketingAnalyticsOverview(filters, enabled);

  function handleRefresh() {
    queryClient.invalidateQueries({ queryKey: ["marketing-analytics"] });
  }

  function handleSelectSegment(key: RfmSegmentKey) {
    // Clicking the already-open card again collapses it (RFM_ANALYSIS.md §10's close button,
    // mirrored on the card itself for a quicker toggle).
    setSelectedSegment((prev) => (prev === key ? null : key));
  }

  if (access === null) return null;
  if (!access) return <AccessDenied title={t("title")} />;

  if (!moduleActive) {
    return (
      <div style={{ padding: "80px 32px", textAlign: "center", display: "flex", flexDirection: "column", alignItems: "center", gap: 16 }}>
        <div style={{ fontSize: 40 }}>🔒</div>
        <h2 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>{t("moduleGate.title")}</h2>
        <p style={{ color: "#4B5563", fontSize: 14, maxWidth: 440 }}>{t("moduleGate.body")}</p>
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 24, width: "100%" }}>
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>{t("subtitle")}</p>
      </div>

      {/* Historical-data disclaimer — a visible banner, not a footnote (plan requirement):
          RFM/LTV only reflects sales with a customer link, which only exists going forward
          from the loyalty program's rollout — past anonymous POS sales can't be attributed. */}
      <div
        style={{
          background: "#0F1F3D",
          border: "1px solid #1D4ED8",
          borderRadius: 10,
          padding: "12px 16px",
          display: "flex",
          gap: 10,
          alignItems: "flex-start",
        }}
      >
        <Info size={16} color="#60A5FA" style={{ flexShrink: 0, marginTop: 1 }} />
        <p style={{ color: "#93C5FD", fontSize: 12.5, lineHeight: 1.5, margin: 0 }}>{t("historicalDataBanner")}</p>
      </div>

      <PeriodStoreFilterBar
        period={period}
        onPeriodChange={setPeriod}
        customFrom={customFrom}
        customTo={customTo}
        onCustomRangeChange={(from, to) => {
          setCustomFrom(from);
          setCustomTo(to);
        }}
        storeIds={storeIds}
        onStoreIdsChange={setStoreIds}
        onRefresh={handleRefresh}
        isRefreshing={isFetching}
      />

      <section>
        <h2 style={sectionTitle}>{t("overviewSection")}</h2>
        {isLoading ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
        ) : overview ? (
          <>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
                gap: 12,
                marginBottom: 20,
              }}
            >
              <KpiCard label={t("kpi.periodCustomers")} value={overview.periodCustomerCount.toLocaleString(intlLocale)} color="#60A5FA" />
              <KpiCard
                label={t("kpi.periodRevenue")}
                value={`${overview.periodRevenue.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`}
                color="#4ADE80"
              />
              <KpiCard label={t("kpi.registeredBase")} value={overview.registeredCustomerCount.toLocaleString(intlLocale)} />
              <KpiCard
                label={t("kpi.everPurchased")}
                value={overview.everPurchasedCustomerCount.toLocaleString(intlLocale)}
                sub={t("kpi.everPurchasedShareSub", {
                  percent: overview.everPurchasedSharePercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 }),
                })}
                color="#2DD4BF"
              />
            </div>

            <SegmentGrid overview={overview} selectedKey={selectedSegment} onSelect={handleSelectSegment} />
          </>
        ) : (
          <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noData")}</div>
        )}
      </section>

      {selectedSegment && overview && (
        <SegmentDetailPanel
          segmentKey={selectedSegment}
          filters={filters}
          periodFrom={overview.periodFrom}
          periodTo={overview.periodTo}
          onClose={() => setSelectedSegment(null)}
        />
      )}

      <StoreMigrationSection filters={filters} enabled={enabled} />
    </div>
  );
}
