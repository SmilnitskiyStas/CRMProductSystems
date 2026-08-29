"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import {
  useExpirySummary,
  useWriteOffAnalytics,
  useWriteOffAnalyticsCompare,
  useZoneAnalytics,
  useCategoryAnalytics,
  useLosses,
  useLossesCompare,
  useLossesTrend,
} from "@/features/analytics/hooks/useAnalytics";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_VIEW_ANALYTICS, hasRole } from "@/lib/roles";
import { useRequireTab } from "@/lib/useRequireTab";
import { usePrimaryStoreId, useStoreContext, useStoreScopeReady } from "@/lib/useStoreContext";
import { ExpiryDonut } from "@/features/analytics/components/ExpiryDonut";
import { LossesByReasonChart } from "@/features/analytics/components/LossesByReasonChart";
import { LossesByStoreChart } from "@/features/analytics/components/LossesByStoreChart";
import { LossesTrendChart } from "@/features/analytics/components/LossesTrendChart";
import { CategoryStatusChart } from "@/features/analytics/components/CategoryStatusChart";
import { CategoryDetailPanel } from "@/features/analytics/components/CategoryDetailPanel";
import { LossesProductBreakdownPanel } from "@/features/analytics/components/LossesProductBreakdownPanel";
import { ProductTrendPanel } from "@/features/analytics/components/ProductTrendPanel";
import { TrendIndicator } from "@/components/ui/TrendIndicator";
import { DateRangePicker, toDateInputValue, parseDateInputValue, type SimpleDateRange } from "@/components/ui/DateRangePicker";
import { Table, type TableColumn } from "@/components/ui/Table";
import type {
  ExpirySummaryStoreDto,
  WriteOffByReasonDto,
  ZoneAnalyticsDto,
  CategoryAnalyticsDto,
  LossByStoreDto,
} from "@/features/analytics/types";

function defaultRange(): SimpleDateRange {
  const to = parseDateInputValue(toDateInputValue(new Date()));
  const from = new Date(to);
  from.setUTCDate(from.getUTCDate() - 30);
  return { from, to };
}

function MetricCard({
  label,
  value,
  sub,
  color,
  onClick,
  trend,
}: {
  label: string;
  value: string | number;
  sub?: string;
  color?: string;
  onClick?: () => void;
  trend?: React.ReactNode;
}) {
  const [hovered, setHovered] = useState(false);

  return (
    <div
      onClick={onClick}
      onMouseEnter={() => onClick && setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        background: hovered ? "#111827" : "#0D1117",
        border: hovered && onClick ? "1px solid #3B82F650" : "1px solid #1F2937",
        borderRadius: 10,
        padding: "16px 20px",
        cursor: onClick ? "pointer" : undefined,
        position: "relative",
        transition: "background 0.1s, border-color 0.1s",
      }}
    >
      {onClick && (
        <span
          style={{
            position: "absolute",
            top: 10,
            right: 12,
            color: hovered ? "#60A5FA" : "#4B5563",
            fontSize: 14,
            transition: "color 0.1s",
          }}
        >
          ›
        </span>
      )}
      <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 6 }}>{label}</div>
      <div style={{ color: color ?? "#E8EDF5", fontSize: 22, fontWeight: 700, fontFamily: "monospace" }}>
        {value}
      </div>
      {trend && <div style={{ marginTop: 4 }}>{trend}</div>}
      {sub && <div style={{ color: "#4B5563", fontSize: 11, marginTop: 4 }}>{sub}</div>}
    </div>
  );
}

const sectionTitle: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 15,
  fontWeight: 700,
  margin: 0,
  marginBottom: 12,
};

export default function AnalyticsPage() {
  const router = useRouter();
  const t = useTranslations("Dashboard.analytics.page");
  const tStatus = useTranslations("Dashboard.analytics.status");
  const tReason = useTranslations("Dashboard.analytics.reason");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, CAN_VIEW_ANALYTICS) : null;
  // Report/read filters (TASK-611) use the full multi-store selection; primaryStoreId is kept
  // only for the ProductTrendPanel drill-down below, whose underlying ADU/stock lookup is
  // inherently single-store (out of scope for this widening — see TASK-610's backend log).
  const primaryStoreId = usePrimaryStoreId();
  const selectedStoreIds = useStoreContext((s) => s.selectedStoreIds);

  // Sidebar tab visibility (TASK-391c; authoritative/exclusive as of TASK-397; per-item
  // granularity as of TASK-399): mirrors Sidebar.tsx's /analytics NavItem gate (roles:
  // CAN_VIEW_ANALYTICS). A non-empty tabs claim is now authoritative in BOTH directions —
  // grants access without the role via either "/analytics" (item-level) or "analytics"
  // (whole-group, backward compat), and also blocks access a CAN_VIEW_ANALYTICS-ranked user
  // would otherwise have, when NEITHER key is in it — same override semantic as Sidebar's
  // tabsSet, no longer a plain OR. Reuses the combined value below for both the route-guard
  // redirect and the AccessDenied render, so a tabs-granted (or tabs-blocked) user's sidebar
  // link and page access always agree. While `me` is still loading, roleAccess is null — keep
  // `access` null too so the existing loading/redirect behavior below is unaffected until
  // useMe() resolves.
  const effectiveAccess = useRequireTab("/analytics", "analytics", roleAccess === true);
  const access = roleAccess === null ? null : effectiveAccess;

  const ready = useStoreScopeReady();
  const enabled = access === true && ready;

  const [range, setRange] = useState<SimpleDateRange>(defaultRange);
  const [compareEnabled, setCompareEnabled] = useState(false);
  const [compareRange, setCompareRange] = useState<SimpleDateRange | undefined>(undefined);

  const from = useMemo(() => toDateInputValue(range.from), [range]);
  const to = useMemo(() => toDateInputValue(range.to), [range]);
  const compareFrom = compareRange ? toDateInputValue(compareRange.from) : undefined;
  const compareTo = compareRange ? toDateInputValue(compareRange.to) : undefined;
  const compareActive = enabled && compareEnabled && !!compareRange;

  const { data: expiry, isLoading: expiryLoading } = useExpirySummary(
    { storeIds: selectedStoreIds },
    enabled,
  );
  const { data: writeoffsFlat, isLoading: writeoffsLoading } = useWriteOffAnalytics(
    { from, to, storeIds: selectedStoreIds },
    enabled && !compareEnabled,
  );
  const { data: writeoffsCompare, isLoading: writeoffsCompareLoading } = useWriteOffAnalyticsCompare(
    { from, to, compareFrom, compareTo, storeIds: selectedStoreIds },
    compareActive,
  );
  const { data: zones } = useZoneAnalytics(selectedStoreIds, enabled);
  const { data: categories } = useCategoryAnalytics(selectedStoreIds, enabled);
  const { data: lossesFlat, isLoading: lossesLoading } = useLosses(
    { from, to, storeIds: selectedStoreIds },
    enabled && !compareEnabled,
  );
  const { data: lossesCompare, isLoading: lossesCompareLoading } = useLossesCompare(
    { from, to, compareFrom, compareTo, storeIds: selectedStoreIds },
    compareActive,
  );
  // No compare-mode variant on this endpoint (TASK-489) — always the page's CURRENT from/to,
  // same "never compare" rule this initiative already applies to CategoryDetailPanel/
  // LossesProductBreakdownPanel's own queries, and ungated by compareEnabled entirely (matches
  // useExpirySummary above, the other hook on this page with no compare variant at all).
  const { data: lossesTrend, isLoading: lossesTrendLoading } = useLossesTrend(
    { from, to, storeIds: selectedStoreIds },
    enabled,
  );

  // Unified view of write-offs/losses regardless of compare mode.
  const writeoffs = compareEnabled ? writeoffsCompare?.current : writeoffsFlat;
  const writeoffsLoadingEffective = compareEnabled ? writeoffsCompareLoading : writeoffsLoading;
  const writeoffsPrevious = compareEnabled ? writeoffsCompare?.comparison : undefined;

  const losses = compareEnabled ? lossesCompare?.current : lossesFlat;
  const lossesLoadingEffective = compareEnabled ? lossesCompareLoading : lossesLoading;
  const lossesPrevious = compareEnabled ? lossesCompare?.comparison : undefined;

  // ── Drill-down panel selection (interactive analytics plan, TASK-483) ──────
  // `selectedCategoryId` is `string | null | undefined` rather than the plan's literal
  // `string | null` — a plain two-state type can't tell "nothing selected" apart from "the
  // uncategorized bucket is selected", since a category id is itself nullable (null =
  // uncategorized, the same convention CategoryStatusChart/the by-category table already use).
  // `undefined` = no panel open; `null` = uncategorized bucket open; a string = that category's
  // panel open. Documented here per CLAUDE.md's judgment-call guidance (mirrors TASK-485's own
  // precedent of a small, noted deviation from the brief when the literal type doesn't hold up).
  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null | undefined>(undefined);
  const [selectedLossDimension, setSelectedLossDimension] = useState<{ type: "reason" | "store"; value: string } | null>(null);
  // Product-row drill-down (TASK-488) from inside CategoryDetailPanel/LossesProductBreakdownPanel
  // above — same {id,name} shape and toggle-on-reselect convention as analytics/pos/page.tsx's own
  // selectedProduct (TASK-484). One shared piece of state for all three trigger panels (by-category,
  // losses-by-reason, losses-by-store) rather than one per panel: it's rendered in a single spot
  // below (not nested under any one of the three), so closing whichever panel triggered it does NOT
  // clear it — deliberate, ProductTrendPanel has its own independent close button, and nesting it
  // under one specific trigger would risk it re-surfacing a stale product when that trigger is
  // reopened later with different data selected.
  const [selectedProduct, setSelectedProduct] = useState<{ id: string; name: string } | null>(null);
  // Day-drill-down from LossesTrendChart (TASK-492) — same toggle-on-reselect convention as
  // every other selection on this page, own independent piece of state (not reused/shared with
  // selectedLossDimension, since a day and a reason/store are different drill axes that can be
  // open at the same time without conflict).
  const [selectedLossDay, setSelectedLossDay] = useState<string | null>(null);

  function handleCategoryClick(categoryId: string | null) {
    setSelectedCategoryId((prev) => (prev === categoryId ? undefined : categoryId));
  }

  function handleReasonClick(reason: string) {
    setSelectedLossDimension((prev) => (prev?.type === "reason" && prev.value === reason ? null : { type: "reason", value: reason }));
  }

  function handleStoreLossClick(storeId: string) {
    setSelectedLossDimension((prev) => (prev?.type === "store" && prev.value === storeId ? null : { type: "store", value: storeId }));
  }

  function handleProductClick(productId: string, productName: string) {
    // Same toggle-on-reselect convention as handleCategoryClick/handleReasonClick/handleStoreLossClick
    // above, and as analytics/pos/page.tsx's own handleProductClick (TASK-484).
    setSelectedProduct((prev) => (prev?.id === productId ? null : { id: productId, name: productName }));
  }

  function handleLossDayClick(date: string) {
    // Same toggle-on-reselect convention as the handlers above, and as analytics/pos/page.tsx's
    // own handleDayClick (TASK-485).
    setSelectedLossDay((prev) => (prev === date ? null : date));
  }

  // ── Table column defs (table-unification migration, Batch B) ──────────────
  // None of these 5 tables have their own sort/pagination — they never did — so no
  // sortKey/onSort/page props are wired below. Per-cell onClick+stopPropagation spans
  // (status counts that deep-link to a filtered /stock view independently of the row's
  // own onRowClick) are preserved unchanged inside render().
  const expiryByStoreColumns: TableColumn<ExpirySummaryStoreDto>[] = [
    {
      key: "store",
      header: t("headers.store"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (s) => s.storeName,
    },
    {
      key: "safe",
      header: tStatus("safe"),
      cellStyle: { color: "#4ADE80", fontFamily: "monospace" },
      render: (s) => (
        <span
          onClick={(e) => { e.stopPropagation(); router.push(`/stock?store_id=${s.storeId}&status=safe`); }}
          style={{ cursor: "pointer" }}
        >
          {s.safe}
        </span>
      ),
    },
    {
      key: "warning",
      header: t("headers.warningShort"),
      cellStyle: { color: "#FBBF24", fontFamily: "monospace" },
      render: (s) => (
        <span
          onClick={(e) => { e.stopPropagation(); router.push(`/stock?store_id=${s.storeId}&status=warning`); }}
          style={{ cursor: "pointer" }}
        >
          {s.warning}
        </span>
      ),
    },
    {
      key: "critical",
      header: tStatus("critical"),
      cellStyle: { color: "#F87171", fontFamily: "monospace" },
      render: (s) => (
        <span
          onClick={(e) => { e.stopPropagation(); router.push(`/stock?store_id=${s.storeId}&status=critical`); }}
          style={{ cursor: "pointer" }}
        >
          {s.critical}
        </span>
      ),
    },
    {
      key: "expired",
      header: tStatus("expired"),
      cellStyle: { color: "#DC2626", fontFamily: "monospace" },
      render: (s) => (
        <span
          onClick={(e) => { e.stopPropagation(); router.push(`/stock?store_id=${s.storeId}&status=expired`); }}
          style={{ cursor: "pointer" }}
        >
          {s.expired}
        </span>
      ),
    },
  ];

  const writeOffReasonColumns: TableColumn<WriteOffByReasonDto>[] = [
    {
      key: "reason",
      header: t("headers.reason"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (r) => (tReason.has(r.reason) ? tReason(r.reason) : r.reason),
    },
    {
      key: "count",
      header: t("writeOffs.headers.documentsCount"),
      cellStyle: { fontFamily: "monospace" },
      render: (r) => r.count,
    },
    {
      key: "losses",
      header: t("headers.losses"),
      cellStyle: { color: "#F87171", fontFamily: "monospace" },
      render: (r) => `${r.totalLoss.toLocaleString(intlLocale)} ₴`,
    },
  ];

  const zoneColumns: TableColumn<ZoneAnalyticsDto>[] = [
    {
      key: "zone",
      header: t("headers.zone"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (z) => z.zoneName,
    },
    {
      key: "storeName",
      header: t("headers.store"),
      cellStyle: { color: "#6B7280" },
      render: (z) => z.storeName,
    },
    {
      key: "safe",
      header: tStatus("safe"),
      cellStyle: { color: "#4ADE80", fontFamily: "monospace" },
      render: (z) => (
        <span
          onClick={(e) => { e.stopPropagation(); router.push(`/stock?zone_id=${z.zoneId}&status=safe`); }}
          style={{ cursor: "pointer" }}
        >
          {z.safe}
        </span>
      ),
    },
    {
      key: "warning",
      header: t("headers.warningShort"),
      cellStyle: { color: "#FBBF24", fontFamily: "monospace" },
      render: (z) => (
        <span
          onClick={(e) => { e.stopPropagation(); router.push(`/stock?zone_id=${z.zoneId}&status=warning`); }}
          style={{ cursor: "pointer" }}
        >
          {z.warning}
        </span>
      ),
    },
    {
      key: "critical",
      header: tStatus("critical"),
      cellStyle: { color: "#F87171", fontFamily: "monospace" },
      render: (z) => (
        <span
          onClick={(e) => { e.stopPropagation(); router.push(`/stock?zone_id=${z.zoneId}&status=critical`); }}
          style={{ cursor: "pointer" }}
        >
          {z.critical}
        </span>
      ),
    },
    {
      key: "expired",
      header: tStatus("expired"),
      cellStyle: { color: "#DC2626", fontFamily: "monospace" },
      render: (z) => (
        <span
          onClick={(e) => { e.stopPropagation(); router.push(`/stock?zone_id=${z.zoneId}&status=expired`); }}
          style={{ cursor: "pointer" }}
        >
          {z.expired}
        </span>
      ),
    },
    {
      key: "total",
      header: t("headers.total"),
      cellStyle: { fontFamily: "monospace" },
      render: (z) => z.totalBatches,
    },
  ];

  const categoryColumns: TableColumn<CategoryAnalyticsDto>[] = [
    {
      key: "category",
      header: t("headers.category"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (c) => c.categoryName,
    },
    {
      key: "safe",
      header: tStatus("safe"),
      cellStyle: { color: "#4ADE80", fontFamily: "monospace" },
      render: (c) => c.safe,
    },
    {
      key: "warning",
      header: t("headers.warningShort"),
      cellStyle: { color: "#FBBF24", fontFamily: "monospace" },
      render: (c) => c.warning,
    },
    {
      key: "critical",
      header: tStatus("critical"),
      cellStyle: { color: "#F87171", fontFamily: "monospace" },
      render: (c) => c.critical,
    },
    {
      key: "expired",
      header: tStatus("expired"),
      cellStyle: { color: "#DC2626", fontFamily: "monospace" },
      render: (c) => c.expired,
    },
    {
      key: "batches",
      header: t("byCategory.headers.batches"),
      cellStyle: { fontFamily: "monospace" },
      render: (c) => c.totalBatches,
    },
    {
      key: "quantity",
      header: t("byCategory.headers.quantity"),
      cellStyle: { fontFamily: "monospace" },
      render: (c) => c.totalQuantity.toLocaleString(intlLocale),
    },
  ];

  const lossesByStoreColumns: TableColumn<LossByStoreDto>[] = [
    {
      key: "store",
      header: t("headers.store"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (s) => s.storeName,
    },
    {
      key: "documents",
      header: t("lossesByStore.headers.documents"),
      cellStyle: { fontFamily: "monospace" },
      render: (s) => s.writeOffCount,
    },
    {
      key: "losses",
      header: t("headers.losses"),
      cellStyle: { color: "#F87171", fontFamily: "monospace" },
      render: (s) => `${s.totalLoss.toLocaleString(intlLocale)} ₴`,
    },
  ];

  if (access === null) return null;
  if (!access) return <AccessDenied title={t("title")} />;

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 28, width: "100%" }}>
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
          {t("subtitle")}
        </p>
      </div>

      {/* ── Date filter (applies to write-offs / losses below) ─────── */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 10,
          padding: "16px 20px",
        }}
      >
        <DateRangePicker
          range={range}
          onRangeChange={setRange}
          compareEnabled={compareEnabled}
          onCompareToggle={setCompareEnabled}
          compareRange={compareRange}
          onCompareRangeChange={setCompareRange}
        />
      </div>

      {/* ── Expiry summary ────────────────────────────────────────── */}
      <section>
        <h2 style={sectionTitle}>{t("expirySummary.title")}</h2>
        {expiryLoading ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
        ) : expiry ? (
          <>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(160px, 1fr))",
                gap: 12,
                marginBottom: 16,
              }}
            >
              <MetricCard label={tStatus("safe")} value={expiry.safe} color="#4ADE80" onClick={() => router.push("/stock?status=safe")} />
              <MetricCard label={tStatus("warning")} value={expiry.warning} color="#FBBF24" onClick={() => router.push("/stock?status=warning")} />
              <MetricCard label={tStatus("critical")} value={expiry.critical} color="#F87171" onClick={() => router.push("/stock?status=critical")} />
              <MetricCard label={tStatus("expired")} value={expiry.expired} color="#DC2626" onClick={() => router.push("/stock?status=expired")} />
              <MetricCard label={tStatus("needsVerification")} value={expiry.needsVerification} color="#A78BFA" onClick={() => router.push("/stock?status=needs_verification")} />
              <MetricCard label={t("expirySummary.totalBatches")} value={expiry.total} onClick={() => router.push("/stock")} />
            </div>

            <ExpiryDonut
              safe={expiry.safe}
              warning={expiry.warning}
              critical={expiry.critical}
              expired={expiry.expired}
              needsVerification={expiry.needsVerification}
              onSliceClick={(status) => router.push(`/stock?status=${status}`)}
            />

            {expiry.stores.length > 0 && (
              <div style={{ marginTop: 16 }}>
                <Table
                  columns={expiryByStoreColumns}
                  rows={expiry.stores}
                  rowKey={(s) => s.storeId}
                  onRowClick={(s) => router.push(`/stock?store_id=${s.storeId}`)}
                />
              </div>
            )}
          </>
        ) : null}
      </section>

      {/* ── Write-off analytics ───────────────────────────────────── */}
      {writeoffsLoadingEffective ? (
        <section>
          <h2 style={sectionTitle}>{t("writeOffs.title")}</h2>
          <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
        </section>
      ) : writeoffs && (
        <section>
          <h2 style={sectionTitle}>{t("writeOffs.title")}</h2>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: 12,
              marginBottom: 16,
            }}
          >
            <MetricCard
              label={t("writeOffs.totalDocuments")}
              value={writeoffs.totalDocuments}
              onClick={() => router.push("/write-offs")}
              trend={
                compareEnabled && writeoffsPrevious ? (
                  <TrendIndicator current={writeoffs.totalDocuments} previous={writeoffsPrevious.totalDocuments} size="sm" />
                ) : undefined
              }
            />
            <MetricCard
              label={t("metrics.totalLoss")}
              value={`${writeoffs.totalLoss.toLocaleString(intlLocale)} ₴`}
              color="#F87171"
              onClick={() => router.push("/write-offs")}
              trend={
                compareEnabled && writeoffsPrevious ? (
                  <TrendIndicator
                    current={writeoffs.totalLoss}
                    previous={writeoffsPrevious.totalLoss}
                    format="currency"
                    size="sm"
                  />
                ) : undefined
              }
            />
          </div>

          {/* ── Losses trend over time (TASK-489/492) ─────────────────────── */}
          {/* Own independent query (no compare-mode variant, always current from/to — see the
              useLossesTrend call above) so it gets its own loading gate rather than riding the
              outer writeoffsLoadingEffective one, which tracks a different query entirely. */}
          {lossesTrendLoading ? (
            <div style={{ color: "#4B5563", fontSize: 13, marginBottom: 16 }}>{tCommon("loading")}</div>
          ) : lossesTrend ? (
            <div style={{ marginBottom: 16 }}>
              <LossesTrendChart
                data={lossesTrend}
                onDayClick={handleLossDayClick}
                selectedDay={selectedLossDay}
              />
              {selectedLossDay && (
                <LossesProductBreakdownPanel
                  title={t("lossesProductPanelTitle", {
                    value: new Date(`${selectedLossDay}T00:00:00Z`).toLocaleDateString(intlLocale, {
                      day: "numeric",
                      month: "long",
                      year: "numeric",
                    }),
                  })}
                  totalLoss={lossesTrend.points.find((p) => p.date === selectedLossDay)?.totalLoss ?? 0}
                  storeIds={selectedStoreIds}
                  from={selectedLossDay}
                  to={selectedLossDay}
                  onClose={() => setSelectedLossDay(null)}
                  onProductClick={handleProductClick}
                />
              )}
            </div>
          ) : null}

          <LossesByReasonChart
            data={writeoffs.byReason}
            onReasonClick={handleReasonClick}
            selectedReason={selectedLossDimension?.type === "reason" ? selectedLossDimension.value : undefined}
          />

          {writeoffs.byReason.length > 0 && (
            <div style={{ marginTop: 16 }}>
              <Table
                columns={writeOffReasonColumns}
                rows={writeoffs.byReason}
                rowKey={(r) => r.reason}
                onRowClick={(r) => handleReasonClick(r.reason)}
                isRowSelected={(r) => selectedLossDimension?.type === "reason" && selectedLossDimension.value === r.reason}
              />
            </div>
          )}

          {selectedLossDimension?.type === "reason" && (
            <LossesProductBreakdownPanel
              title={t("lossesProductPanelTitle", {
                value: tReason.has(selectedLossDimension.value) ? tReason(selectedLossDimension.value) : selectedLossDimension.value,
              })}
              totalLoss={writeoffs.byReason.find((r) => r.reason === selectedLossDimension.value)?.totalLoss ?? 0}
              reason={selectedLossDimension.value}
              storeIds={selectedStoreIds}
              from={from}
              to={to}
              onClose={() => setSelectedLossDimension(null)}
              onProductClick={handleProductClick}
            />
          )}
        </section>
      )}

      {/* ── By zone ───────────────────────────────────────────────── */}
      {zones && zones.length > 0 && (
        <section>
          <h2 style={sectionTitle}>{t("byZone.title")}</h2>
          <Table
            columns={zoneColumns}
            rows={zones}
            rowKey={(z) => z.zoneId}
            onRowClick={(z) => router.push(`/stock?zone_id=${z.zoneId}`)}
          />
        </section>
      )}

      {/* ── By category ───────────────────────────────────────────── */}
      {categories && categories.length > 0 && (
        <section>
          <h2 style={sectionTitle}>{t("byCategory.title")}</h2>
          <CategoryStatusChart data={categories} onCategoryClick={handleCategoryClick} selectedCategoryId={selectedCategoryId} />
          <div style={{ marginTop: 16 }}>
            <Table
              columns={categoryColumns}
              rows={categories}
              rowKey={(c) => c.categoryId ?? "uncategorized"}
              onRowClick={(c) => handleCategoryClick(c.categoryId)}
              isRowSelected={(c) => selectedCategoryId === c.categoryId}
            />
          </div>

          {selectedCategoryId !== undefined && (
            <CategoryDetailPanel
              categoryId={selectedCategoryId}
              storeIds={selectedStoreIds}
              from={from}
              to={to}
              onClose={() => setSelectedCategoryId(undefined)}
              onProductClick={handleProductClick}
            />
          )}
        </section>
      )}

      {/* ── Losses by store ───────────────────────────────────────── */}
      {lossesLoadingEffective ? (
        <section>
          <h2 style={sectionTitle}>{t("lossesByStore.title")}</h2>
          <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
        </section>
      ) : losses && losses.byStore.length > 0 && (
        <section>
          <h2 style={sectionTitle}>{t("lossesByStore.title")}</h2>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: 12,
              marginBottom: 16,
            }}
          >
            <MetricCard
              label={t("metrics.totalLoss")}
              value={`${losses.totalLoss.toLocaleString(intlLocale)} ₴`}
              color="#F87171"
              onClick={() => router.push("/write-offs")}
              trend={
                compareEnabled && lossesPrevious ? (
                  <TrendIndicator current={losses.totalLoss} previous={lossesPrevious.totalLoss} format="currency" size="sm" />
                ) : undefined
              }
            />
            <MetricCard
              label={t("lossesByStore.totalWriteOffs")}
              value={losses.totalWriteOffs}
              onClick={() => router.push("/write-offs")}
              trend={
                compareEnabled && lossesPrevious ? (
                  <TrendIndicator current={losses.totalWriteOffs} previous={lossesPrevious.totalWriteOffs} size="sm" />
                ) : undefined
              }
            />
            <MetricCard
              label={t("lossesByStore.averagePerDocument")}
              value={`${losses.averageLossPerWriteOff.toLocaleString(intlLocale)} ₴`}
            />
          </div>
          <LossesByStoreChart
            data={losses.byStore}
            onStoreClick={handleStoreLossClick}
            selectedStoreId={selectedLossDimension?.type === "store" ? selectedLossDimension.value : undefined}
          />
          <div style={{ marginTop: 16 }}>
            <Table
              columns={lossesByStoreColumns}
              rows={losses.byStore}
              rowKey={(s) => s.storeId}
              onRowClick={(s) => handleStoreLossClick(s.storeId)}
              isRowSelected={(s) => selectedLossDimension?.type === "store" && selectedLossDimension.value === s.storeId}
            />
          </div>

          {selectedLossDimension?.type === "store" && (
            <LossesProductBreakdownPanel
              title={t("lossesProductPanelTitle", {
                value: losses.byStore.find((s) => s.storeId === selectedLossDimension.value)?.storeName ?? selectedLossDimension.value,
              })}
              totalLoss={losses.byStore.find((s) => s.storeId === selectedLossDimension.value)?.totalLoss ?? 0}
              storeIds={[selectedLossDimension.value]}
              from={from}
              to={to}
              onClose={() => setSelectedLossDimension(null)}
              onProductClick={handleProductClick}
            />
          )}
        </section>
      )}

      {/* ── Product trend (TASK-488) ──────────────────────────────── */}
      {/* Triggered by a product-row click inside CategoryDetailPanel or LossesProductBreakdownPanel
          above (by-category / losses-by-reason / losses-by-store — three trigger points, one shared
          panel). Reuses ProductTrendPanel unmodified — the exact same component PosTopProductsTable
          already opens on /analytics/pos (TASK-484), renamed from PosProductTrendPanel now that it's
          used outside /analytics/pos too. This page now threads its own page-wide store filter
          (primaryStoreId, from the header's global store selector — see TASK-514/TASK-515) through the same
          way /analytics/pos threads its local store dropdown. */}
      {selectedProduct && (
        <ProductTrendPanel
          productId={selectedProduct.id}
          productName={selectedProduct.name}
          storeId={primaryStoreId ?? undefined}
          onClose={() => setSelectedProduct(null)}
        />
      )}
    </div>
  );
}
