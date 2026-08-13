"use client";

import { useEffect, useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { useQueryClient } from "@tanstack/react-query";
import { Info } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_VIEW_ANALYTICS, hasRole, canExportMarketingAnalyticsPii } from "@/lib/roles";
import { useRequireTab } from "@/lib/useRequireTab";
import { useStoreContext } from "@/lib/useStoreContext";
import { useModules } from "@/features/modules/hooks/useModules";
import {
  usePriceSegmentsOverview,
  usePriceAudienceTable,
  useAllTimeOverview,
  useAllTimeCustomerTable,
  useFrequencyOverview,
  useFrequencyAudienceTable,
} from "@/features/marketing-analytics/price-segments/hooks/usePriceSegments";
import { ModeTabs, type PriceSegmentsMode } from "@/features/marketing-analytics/price-segments/components/ModeTabs";
import { ComparisonFilterBar } from "@/features/marketing-analytics/price-segments/components/ComparisonFilterBar";
import { PriceSegmentChart } from "@/features/marketing-analytics/price-segments/components/PriceSegmentChart";
import { PriceAudienceCards } from "@/features/marketing-analytics/price-segments/components/PriceAudienceCards";
import { PriceAudienceTable } from "@/features/marketing-analytics/price-segments/components/PriceAudienceTable";
import { AllTimeKpiCards } from "@/features/marketing-analytics/price-segments/components/AllTimeView/AllTimeKpiCards";
import { MedianCheckTrendChart } from "@/features/marketing-analytics/price-segments/components/AllTimeView/MedianCheckTrendChart";
import { SegmentDistributionChart } from "@/features/marketing-analytics/price-segments/components/AllTimeView/SegmentDistributionChart";
import { SegmentRecommendationCard } from "@/features/marketing-analytics/price-segments/components/AllTimeView/SegmentRecommendationCard";
import { AllTimeCustomerTable } from "@/features/marketing-analytics/price-segments/components/AllTimeView/AllTimeCustomerTable";
import { FrequencyKpiCards } from "@/features/marketing-analytics/price-segments/components/FrequencyView/FrequencyKpiCards";
import { FrequencyModeFilter } from "@/features/marketing-analytics/price-segments/components/FrequencyView/FrequencyModeFilter";
import { FrequencyAudienceTable } from "@/features/marketing-analytics/price-segments/components/FrequencyView/FrequencyAudienceTable";
import type {
  PriceSegmentsPeriodPreset,
  PriceSegmentsPeriodFilters,
  PriceAudienceKey,
  PriceAudienceSortBy,
  PriceSegmentKey,
  AllTimeSortBy,
  FrequencyAudienceKey,
  FrequencySortBy,
} from "@/features/marketing-analytics/price-segments/types";

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

function KpiCard({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "18px 20px", display: "flex", flexDirection: "column", gap: 6 }}>
      <div style={{ color: "#4B5563", fontSize: 12, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.05em" }}>{label}</div>
      <div style={{ color: color ?? "#E8EDF5", fontSize: 24, fontWeight: 700, fontFamily: "monospace", lineHeight: 1.1 }}>{value}</div>
    </div>
  );
}

/**
 * Фаза 2 — price segments + frequency/reactivation (TASK-421). Three modes as TABS (never
 * separate pages/routes), sharing one route — see ModeTabs.tsx's own doc comment for why "Весь
 * час" is a genuinely separate mode rather than a period preset. Comparison and Frequency share
 * the same period+store filter bar (analysis doc §4.1); All-time reuses the same bar with
 * `hidePeriod` so only the store selector remains.
 */
export default function PriceSegmentsPage() {
  const t = useTranslations("Dashboard.priceSegments.page");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const queryClient = useQueryClient();

  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, CAN_VIEW_ANALYTICS) : null;
  // Mirrors Sidebar.tsx's "marketing_analytics" NavGroup gate — same group as the RFM dashboard,
  // second item ("/marketing-analytics/price-segments").
  const effectiveAccess = useRequireTab("/marketing-analytics/price-segments", "marketing_analytics", roleAccess === true);
  const access = roleAccess === null ? null : effectiveAccess;

  const { data: modulesData } = useModules(access === true);
  const moduleActive = !modulesData || modulesData.modules.includes("marketing_analytics");
  const canExportPii = canExportMarketingAnalyticsPii(me?.role, me?.permissions);

  const [mode, setMode] = useState<PriceSegmentsMode>("comparison");

  // Shared by Comparison + Frequency (mirrors the competitor's own single top period+store
  // control governing both tabs, analysis doc §4.1) — switching between just these two modes
  // never resets period/stores; All-time simply doesn't use them.
  const [period, setPeriod] = useState<PriceSegmentsPeriodPreset>("30");
  const [customFrom, setCustomFrom] = useState<string>(defaultCustomFrom);
  const [customTo, setCustomTo] = useState<string>(defaultCustomTo);
  const { selectedStoreIds: storeIds } = useStoreContext();

  // The one filter object every Comparison/Frequency query keys off of — any change here is a
  // brand-new React Query key, so a filter switch never shows a mix of old and new numbers.
  const filters: PriceSegmentsPeriodFilters = useMemo(
    () => ({
      period,
      from: period === "custom" ? customFrom : undefined,
      to: period === "custom" ? customTo : undefined,
      storeIds,
    }),
    [period, customFrom, customTo, storeIds],
  );

  const enabled = access === true && moduleActive;

  // ── Comparison ("Суми покупок") ────────────────────────────────────────────────────────────
  const [selectedAudience, setSelectedAudience] = useState<PriceAudienceKey | null>(null);
  const [audiencePage, setAudiencePage] = useState(1);
  const [audienceSortBy, setAudienceSortBy] = useState<PriceAudienceSortBy>("check");
  const [audienceSortDesc, setAudienceSortDesc] = useState(true);

  const {
    data: overview,
    isLoading: overviewLoading,
    isFetching: overviewFetching,
  } = usePriceSegmentsOverview(filters, enabled && mode === "comparison");

  const audienceTableParams = selectedAudience
    ? { audience: selectedAudience, page: audiencePage, pageSize: 10, sortBy: audienceSortBy, sortDescending: audienceSortDesc }
    : null;
  const {
    data: audienceTable,
    isLoading: audienceTableLoading,
    isFetching: audienceTableFetching,
  } = usePriceAudienceTable(audienceTableParams, filters, enabled && mode === "comparison");

  useEffect(() => {
    setAudiencePage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedAudience, filters, audienceSortBy, audienceSortDesc]);

  function handleSelectAudience(a: PriceAudienceKey) {
    // Clicking the already-open card again collapses it — same convention as RFM's SegmentGrid.
    setSelectedAudience((prev) => (prev === a ? null : a));
  }
  function handleAudienceSort(key: PriceAudienceSortBy) {
    if (key === audienceSortBy) setAudienceSortDesc((d) => !d);
    else {
      setAudienceSortBy(key);
      setAudienceSortDesc(true);
    }
  }

  // ── All-time ("Весь час") ───────────────────────────────────────────────────────────────────
  const [selectedSegment, setSelectedSegment] = useState<PriceSegmentKey | null>(null);
  const [allTimePage, setAllTimePage] = useState(1);
  const [allTimeSortBy, setAllTimeSortBy] = useState<AllTimeSortBy>("check");
  const [allTimeSortDesc, setAllTimeSortDesc] = useState(true);

  // Also loaded in the background while Frequency mode is active, purely to source real ₴ range
  // labels for that tab's price-segment filter (see FrequencyModeFilter's own doc comment) — same
  // live network-wide tier boundaries regardless of mode (design doc §1.2), so this is safe to
  // share rather than fetching the same boundaries twice under two different endpoints.
  const {
    data: allTimeOverview,
    isFetching: allTimeOverviewFetching,
  } = useAllTimeOverview(storeIds, selectedSegment, enabled && (mode === "allTime" || mode === "frequency"));

  const {
    data: allTimeTable,
    isLoading: allTimeTableLoading,
    isFetching: allTimeTableFetching,
  } = useAllTimeCustomerTable(
    { segment: selectedSegment, page: allTimePage, pageSize: 10, sortBy: allTimeSortBy, sortDescending: allTimeSortDesc },
    storeIds,
    enabled && mode === "allTime",
  );

  useEffect(() => {
    setAllTimePage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedSegment, storeIds, allTimeSortBy, allTimeSortDesc]);

  function handleAllTimeSort(key: AllTimeSortBy) {
    if (key === allTimeSortBy) setAllTimeSortDesc((d) => !d);
    else {
      setAllTimeSortBy(key);
      setAllTimeSortDesc(true);
    }
  }

  // ── Frequency & reactivation ────────────────────────────────────────────────────────────────
  const [freqAudience, setFreqAudience] = useState<FrequencyAudienceKey | null>(null);
  const [declineThreshold, setDeclineThreshold] = useState<number | undefined>(undefined);
  const [minSpend, setMinSpend] = useState<number | undefined>(undefined);
  const [maxSpend, setMaxSpend] = useState<number | undefined>(undefined);
  const [freqPriceSegment, setFreqPriceSegment] = useState<PriceSegmentKey | undefined>(undefined);
  const [freqPage, setFreqPage] = useState(1);
  const [freqSortBy, setFreqSortBy] = useState<FrequencySortBy>("ltv");
  const [freqSortDesc, setFreqSortDesc] = useState(true);

  const {
    data: freqOverview,
    isLoading: freqOverviewLoading,
    isFetching: freqOverviewFetching,
  } = useFrequencyOverview(filters, declineThreshold, enabled && mode === "frequency");

  const freqTableParams = freqAudience
    ? {
        audience: freqAudience,
        minSpend,
        maxSpend,
        priceSegmentFilter: freqPriceSegment,
        declineThresholdPercent: declineThreshold,
        page: freqPage,
        pageSize: 10,
        sortBy: freqSortBy,
        sortDescending: freqSortDesc,
      }
    : null;
  const {
    data: freqTable,
    isLoading: freqTableLoading,
    isFetching: freqTableFetching,
  } = useFrequencyAudienceTable(freqTableParams, filters, enabled && mode === "frequency");

  useEffect(() => {
    setFreqPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [freqAudience, filters, minSpend, maxSpend, freqPriceSegment, declineThreshold, freqSortBy, freqSortDesc]);

  function handleSelectFreqAudience(a: FrequencyAudienceKey) {
    setFreqAudience((prev) => (prev === a ? null : a));
  }
  function handleFreqSort(key: FrequencySortBy) {
    if (key === freqSortBy) setFreqSortDesc((d) => !d);
    else {
      setFreqSortBy(key);
      setFreqSortDesc(true);
    }
  }

  function handleRefresh() {
    queryClient.invalidateQueries({ queryKey: ["price-segments"] });
  }

  const tierOptions = allTimeOverview?.distribution.map((d) => ({ segment: d.segment, rangeLabelUa: d.rangeLabelUa }));

  const isRefreshing =
    mode === "comparison"
      ? overviewFetching || audienceTableFetching
      : mode === "frequency"
      ? freqOverviewFetching || freqTableFetching
      : allTimeOverviewFetching || allTimeTableFetching;

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

      {/* Same historical-data caveat as the RFM dashboard — every metric here is also derived
          solely from customer-linked (loyalty) POS sales. */}
      <div style={{ background: "#0F1F3D", border: "1px solid #1D4ED8", borderRadius: 10, padding: "12px 16px", display: "flex", gap: 10, alignItems: "flex-start" }}>
        <Info size={16} color="#60A5FA" style={{ flexShrink: 0, marginTop: 1 }} />
        <p style={{ color: "#93C5FD", fontSize: 12.5, lineHeight: 1.5, margin: 0 }}>{t("historicalDataBanner")}</p>
      </div>

      <ModeTabs mode={mode} onModeChange={setMode} />

      <ComparisonFilterBar
        period={period}
        onPeriodChange={setPeriod}
        customFrom={customFrom}
        customTo={customTo}
        onCustomRangeChange={(from, to) => {
          setCustomFrom(from);
          setCustomTo(to);
        }}
        onRefresh={handleRefresh}
        isRefreshing={isRefreshing}
        hidePeriod={mode === "allTime"}
      />

      {mode === "comparison" && (
        <>
          <section>
            <h2 style={sectionTitle}>{t("comparisonSection")}</h2>
            {overviewLoading ? (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
            ) : overview ? (
              <>
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(190px, 1fr))", gap: 12, marginBottom: 10 }}>
                  <KpiCard label={t("kpi.analyzed")} value={overview.analyzedCount.toLocaleString(intlLocale)} color="#60A5FA" />
                  <KpiCard label={t("kpi.raised")} value={overview.raisedCount.toLocaleString(intlLocale)} color="#4ADE80" />
                  <KpiCard label={t("kpi.declined")} value={overview.declinedCount.toLocaleString(intlLocale)} color="#F87171" />
                  <KpiCard label={t("kpi.stable")} value={overview.stableCount.toLocaleString(intlLocale)} />
                  <KpiCard
                    label={t("kpi.priceIndex")}
                    value={`${overview.priceIndexPercent > 0 ? "+" : ""}${overview.priceIndexPercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%`}
                    color={overview.priceIndexPercent > 0 ? "#FBBF24" : overview.priceIndexPercent < 0 ? "#60A5FA" : undefined}
                  />
                </div>
                {/* "Проаналізовано" vs current/previous active-buyer counts — three different
                    denominators (analysis doc §6.2/§25.1), never conflated. */}
                <p style={{ color: "#4B5563", fontSize: 11.5, lineHeight: 1.5, margin: "0 0 20px" }}>
                  {t("kpi.denominatorsFootnote", {
                    current: overview.currentPeriodBuyerCount.toLocaleString(intlLocale),
                    previous: overview.previousPeriodBuyerCount.toLocaleString(intlLocale),
                  })}
                </p>

                <PriceSegmentChart distribution={overview.distribution} />

                <div style={{ marginTop: 20 }}>
                  <PriceAudienceCards audiences={overview.audiences} selectedAudience={selectedAudience} onSelect={handleSelectAudience} />
                </div>
              </>
            ) : (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noData")}</div>
            )}
          </section>

          {selectedAudience && overview && (
            <PriceAudienceTable
              data={audienceTable}
              isLoading={audienceTableLoading}
              sortBy={audienceSortBy}
              sortDescending={audienceSortDesc}
              onSort={handleAudienceSort}
              page={audiencePage}
              onPageChange={setAudiencePage}
              audience={selectedAudience}
              filters={filters}
              periodFrom={overview.periodFrom}
              periodTo={overview.periodTo}
              canExportPii={canExportPii}
            />
          )}
        </>
      )}

      {mode === "frequency" && (
        <>
          <section>
            <h2 style={sectionTitle}>{t("frequencySection")}</h2>
            {freqOverviewLoading ? (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
            ) : freqOverview ? (
              <>
                <FrequencyKpiCards overview={freqOverview} />
                <div style={{ marginTop: 20 }}>
                  <FrequencyModeFilter
                    audiences={freqOverview.audiences}
                    selectedAudience={freqAudience}
                    onSelectAudience={handleSelectFreqAudience}
                    declineThresholdPercent={declineThreshold}
                    onDeclineThresholdChange={setDeclineThreshold}
                    minSpend={minSpend}
                    onMinSpendChange={setMinSpend}
                    maxSpend={maxSpend}
                    onMaxSpendChange={setMaxSpend}
                    priceSegmentFilter={freqPriceSegment}
                    onPriceSegmentFilterChange={setFreqPriceSegment}
                    tierOptions={tierOptions}
                  />
                </div>
              </>
            ) : (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noData")}</div>
            )}
          </section>

          {freqAudience && freqOverview && (
            <FrequencyAudienceTable
              data={freqTable}
              isLoading={freqTableLoading}
              sortBy={freqSortBy}
              sortDescending={freqSortDesc}
              onSort={handleFreqSort}
              page={freqPage}
              onPageChange={setFreqPage}
              audience={freqAudience}
              filters={filters}
              periodFrom={freqOverview.periodFrom}
              periodTo={freqOverview.periodTo}
              declineThresholdPercent={declineThreshold}
              minSpend={minSpend}
              maxSpend={maxSpend}
              priceSegmentFilter={freqPriceSegment}
              canExportPii={canExportPii}
            />
          )}
        </>
      )}

      {mode === "allTime" && (
        <>
          <section>
            <h2 style={sectionTitle}>{t("allTimeSection")}</h2>
            {allTimeOverview ? (
              <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
                <AllTimeKpiCards overview={allTimeOverview} />
                <MedianCheckTrendChart monthlyTrend={allTimeOverview.monthlyTrend} />
                <SegmentDistributionChart distribution={allTimeOverview.distribution} selectedSegment={selectedSegment} onSelect={setSelectedSegment} />
                <SegmentRecommendationCard recommendation={allTimeOverview.recommendation} selectedSegment={selectedSegment} storeIds={storeIds} />
              </div>
            ) : (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
            )}
          </section>

          <AllTimeCustomerTable
            data={allTimeTable}
            isLoading={allTimeTableLoading}
            sortBy={allTimeSortBy}
            sortDescending={allTimeSortDesc}
            onSort={handleAllTimeSort}
            page={allTimePage}
            onPageChange={setAllTimePage}
            segment={selectedSegment}
            storeIds={storeIds}
            canExportPii={canExportPii}
          />
        </>
      )}
    </div>
  );
}
