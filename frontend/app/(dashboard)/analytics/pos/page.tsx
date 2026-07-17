"use client";

import { useState, useMemo } from "react";
import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_VIEW_ANALYTICS, hasRole } from "@/lib/roles";
import {
  usePosSummary,
  usePosSummaryCompare,
  usePosRevenueTrend,
  usePosRevenueTrendCompare,
  usePosTopProducts,
  usePosCashiers,
} from "@/features/analytics/hooks/usePosAnalytics";
import { PosSummaryCards } from "@/features/analytics/components/PosSummaryCards";
import { PosRevenueTrendChart } from "@/features/analytics/components/PosRevenueTrendChart";
import { PosTopProductsTable } from "@/features/analytics/components/PosTopProductsTable";
import { PosCashierStatsTable } from "@/features/analytics/components/PosCashierStatsTable";
import { PosPaymentPieChart } from "@/features/analytics/components/PosPaymentPieChart";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { DateRangePicker, toDateInputValue, parseDateInputValue, type SimpleDateRange } from "@/components/ui/DateRangePicker";

// ── helpers ──────────────────────────────────────────────────────────────────

function toDateStr(d: Date): string {
  return d.toISOString().slice(0, 10);
}

function defaultFrom(): string {
  const d = new Date();
  d.setDate(d.getDate() - 30);
  return toDateStr(d);
}

function defaultTo(): string {
  return toDateStr(new Date());
}

// ── Store selector (simple) ───────────────────────────────────────────────────

interface StoreOption {
  id: string;
  name: string;
}

function useStores() {
  return useQuery<StoreOption[]>({
    queryKey: ["locations"],
    queryFn: () => api.get<StoreOption[]>("/api/locations"),
    staleTime: 5 * 60 * 1000,
  });
}

// ── style tokens (match rest of analytics) ────────────────────────────────────

const labelStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 12,
  marginBottom: 4,
  display: "block",
};

const inputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 10px",
  outline: "none",
};

const selectStyle: React.CSSProperties = {
  ...inputStyle,
  minWidth: 160,
  cursor: "pointer",
};

const toggleBtn = (active: boolean): React.CSSProperties => ({
  padding: "7px 16px",
  fontSize: 12,
  fontWeight: 500,
  borderRadius: 6,
  border: "1px solid #1F2937",
  cursor: "pointer",
  background: active ? "#1D3461" : "#0D1117",
  color: active ? "#93C5FD" : "#6B7280",
  transition: "background 0.1s, color 0.1s",
});

const sectionTitle: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 15,
  fontWeight: 700,
  margin: 0,
  marginBottom: 12,
};

// ── page ─────────────────────────────────────────────────────────────────────

export default function PosAnalyticsPage() {
  const t = useTranslations("Dashboard.analytics.pos.page");
  const tCommon = useTranslations("Common");
  const { data: me } = useMe();
  const access = me ? hasRole(me.role, CAN_VIEW_ANALYTICS) : null;

  const [from, setFrom] = useState<string>(defaultFrom);
  const [to, setTo] = useState<string>(defaultTo);
  const [storeId, setStoreId] = useState<string>("");
  const [groupBy, setGroupBy] = useState<"day" | "week">("day");
  const [compareEnabled, setCompareEnabled] = useState(false);
  const [compareRange, setCompareRange] = useState<SimpleDateRange | undefined>(undefined);

  const range: SimpleDateRange = useMemo(
    () => ({ from: parseDateInputValue(from), to: parseDateInputValue(to) }),
    [from, to],
  );

  const { data: stores } = useStores();

  const params = useMemo(
    () => ({ from, to, store_id: storeId || undefined }),
    [from, to, storeId],
  );

  const compareFrom = compareRange ? toDateInputValue(compareRange.from) : undefined;
  const compareTo = compareRange ? toDateInputValue(compareRange.to) : undefined;

  const enabled = access === true;
  const compareActive = enabled && compareEnabled && !!compareRange;

  const { data: summary, isLoading: summaryLoading } = usePosSummary(
    { from, to, store_id: storeId || undefined },
    enabled && !compareEnabled,
  );
  const { data: summaryCompare, isLoading: summaryCompareLoading } = usePosSummaryCompare(
    { from, to, store_id: storeId || undefined, compareFrom, compareTo },
    compareActive,
  );
  const { data: trend, isLoading: trendLoading } = usePosRevenueTrend(
    { ...params, group_by: groupBy },
    enabled && !compareEnabled,
  );
  const { data: trendCompare, isLoading: trendCompareLoading } = usePosRevenueTrendCompare(
    { ...params, group_by: groupBy, compareFrom, compareTo },
    compareActive,
  );
  const { data: topProducts, isLoading: topLoading } = usePosTopProducts(
    { ...params, limit: "10" },
    enabled,
  );
  const { data: cashiers, isLoading: cashiersLoading } = usePosCashiers(
    { from, to, store_id: storeId || undefined },
    enabled,
  );

  // Unified view of summary/trend regardless of compare mode.
  const effectiveSummary = compareEnabled ? summaryCompare?.current : summary;
  const effectiveSummaryLoading = compareEnabled ? summaryCompareLoading : summaryLoading;
  const effectiveTrend = compareEnabled ? (trendCompare ? { points: trendCompare.current, groupBy: trendCompare.groupBy } : undefined) : trend;
  const effectiveTrendLoading = compareEnabled ? trendCompareLoading : trendLoading;

  if (access === null) return null;
  if (!access) return <AccessDenied title={t("title")} />;

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 28, width: "100%" }}>
      {/* Header */}
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
          {t("subtitle")}
        </p>
      </div>

      {/* Filters */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 10,
          padding: "16px 20px",
          display: "flex",
          flexWrap: "wrap",
          gap: 16,
          alignItems: "flex-end",
        }}
      >
        <DateRangePicker
          range={range}
          onRangeChange={(r) => { setFrom(toDateInputValue(r.from)); setTo(toDateInputValue(r.to)); }}
          compareEnabled={compareEnabled}
          onCompareToggle={setCompareEnabled}
          compareRange={compareRange}
          onCompareRangeChange={setCompareRange}
        />
        <div>
          <label style={labelStyle}>{t("storeLabel")}</label>
          <select
            value={storeId}
            onChange={(e) => setStoreId(e.target.value)}
            style={selectStyle}
          >
            <option value="">{t("allStores")}</option>
            {stores?.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label style={labelStyle}>{t("trendLabel")}</label>
          <div style={{ display: "flex", gap: 4 }}>
            <button
              style={toggleBtn(groupBy === "day")}
              onClick={() => setGroupBy("day")}
            >
              {t("trendDay")}
            </button>
            <button
              style={toggleBtn(groupBy === "week")}
              onClick={() => setGroupBy("week")}
            >
              {t("trendWeek")}
            </button>
          </div>
        </div>
      </div>

      {/* KPI summary cards */}
      <section>
        <h2 style={sectionTitle}>{t("summarySection")}</h2>
        {effectiveSummaryLoading ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
        ) : effectiveSummary ? (
          <PosSummaryCards
            data={effectiveSummary}
            previous={compareEnabled ? summaryCompare?.comparison : undefined}
          />
        ) : (
          <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noData")}</div>
        )}
      </section>

      {/* Revenue trend */}
      <section>
        <h2 style={sectionTitle}>{t("revenueTrendSection")}</h2>
        {effectiveTrendLoading ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
        ) : effectiveTrend ? (
          <PosRevenueTrendChart
            data={effectiveTrend}
            from={from}
            comparison={
              compareEnabled && trendCompare
                ? { points: trendCompare.comparison, from: trendCompare.compareFrom }
                : undefined
            }
          />
        ) : (
          <div
            style={{
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 10,
              padding: "20px 16px",
              color: "#4B5563",
              fontSize: 13,
            }}
          >
            {t("noData")}
          </div>
        )}
      </section>

      {/* Top products + Cashiers side by side */}
      <section>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>
          <div>
            {topLoading ? (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
            ) : topProducts ? (
              <PosTopProductsTable data={topProducts} />
            ) : null}
          </div>
          <div>
            {cashiersLoading ? (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
            ) : cashiers ? (
              <PosCashierStatsTable data={cashiers} />
            ) : null}
          </div>
        </div>
      </section>

      {/* Payment pie */}
      {effectiveSummary && effectiveSummary.totalRevenue > 0 && (
        <section>
          <h2 style={sectionTitle}>{t("paymentMethodsSection")}</h2>
          <div style={{ maxWidth: 400 }}>
            <PosPaymentPieChart data={effectiveSummary} />
          </div>
        </section>
      )}
    </div>
  );
}
