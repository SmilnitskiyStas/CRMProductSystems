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
import { useStoreContext } from "@/lib/useStoreContext";
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

// ── Shared table style tokens ──────────────────────────────────────────────────
const ROW_BORDER = "1px solid #1F2937";

/** Base cell padding + border */
const baseTd: React.CSSProperties = {
  padding: "10px 16px",
  fontSize: 13,
  borderBottom: ROW_BORDER,
  borderRight: "1px solid #1F2937",
  textAlign: "center",
};

/** Text column */
const tdText: React.CSSProperties = {
  ...baseTd,
  color: "#E8EDF5",
  fontWeight: 500,
};

/** Secondary text (store name in zone table etc.) */
const tdMuted: React.CSSProperties = {
  ...baseTd,
  color: "#6B7280",
};

/** Numeric column — monospace */
const tdNum: React.CSSProperties = {
  ...baseTd,
  color: "#9CA3AF",
  fontFamily: "monospace",
};

/** Header */
function thStyle(_align: "left" | "right" = "left"): React.CSSProperties {
  return {
    padding: "10px 16px",
    color: "#4B5563",
    fontSize: 11,
    fontWeight: 600,
    textTransform: "uppercase",
    letterSpacing: "0.05em",
    borderBottom: "1px solid #374151",
    borderRight: "1px solid #374151",
    textAlign: "center",
    background: "#0A0F1A",
  };
}

const sectionTitle: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 15,
  fontWeight: 700,
  margin: 0,
  marginBottom: 12,
};

const tableWrapper: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 10,
  overflow: "hidden",
};

function rowHoverStyle(isHovered: boolean): React.CSSProperties {
  return {
    cursor: "pointer",
    background: isHovered ? "#0F1825" : "transparent",
    transition: "background 0.1s",
  };
}

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
  const { selectedStoreId } = useStoreContext();

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

  const enabled = access === true;

  const [range, setRange] = useState<SimpleDateRange>(defaultRange);
  const [compareEnabled, setCompareEnabled] = useState(false);
  const [compareRange, setCompareRange] = useState<SimpleDateRange | undefined>(undefined);

  const from = useMemo(() => toDateInputValue(range.from), [range]);
  const to = useMemo(() => toDateInputValue(range.to), [range]);
  const compareFrom = compareRange ? toDateInputValue(compareRange.from) : undefined;
  const compareTo = compareRange ? toDateInputValue(compareRange.to) : undefined;
  const compareActive = enabled && compareEnabled && !!compareRange;

  const { data: expiry, isLoading: expiryLoading } = useExpirySummary(
    { store_id: selectedStoreId ?? undefined },
    enabled,
  );
  const { data: writeoffsFlat, isLoading: writeoffsLoading } = useWriteOffAnalytics(
    { from, to, store_id: selectedStoreId ?? undefined },
    enabled && !compareEnabled,
  );
  const { data: writeoffsCompare, isLoading: writeoffsCompareLoading } = useWriteOffAnalyticsCompare(
    { from, to, compareFrom, compareTo, store_id: selectedStoreId ?? undefined },
    compareActive,
  );
  const { data: zones } = useZoneAnalytics(selectedStoreId ?? undefined, enabled);
  const { data: categories } = useCategoryAnalytics(selectedStoreId ?? undefined, enabled);
  const { data: lossesFlat, isLoading: lossesLoading } = useLosses(
    { from, to, store_id: selectedStoreId ?? undefined },
    enabled && !compareEnabled,
  );
  const { data: lossesCompare, isLoading: lossesCompareLoading } = useLossesCompare(
    { from, to, compareFrom, compareTo, store_id: selectedStoreId ?? undefined },
    compareActive,
  );
  // No compare-mode variant on this endpoint (TASK-489) — always the page's CURRENT from/to,
  // same "never compare" rule this initiative already applies to CategoryDetailPanel/
  // LossesProductBreakdownPanel's own queries, and ungated by compareEnabled entirely (matches
  // useExpirySummary above, the other hook on this page with no compare variant at all).
  const { data: lossesTrend, isLoading: lossesTrendLoading } = useLossesTrend(
    { from, to, store_id: selectedStoreId ?? undefined },
    enabled,
  );

  // Unified view of write-offs/losses regardless of compare mode.
  const writeoffs = compareEnabled ? writeoffsCompare?.current : writeoffsFlat;
  const writeoffsLoadingEffective = compareEnabled ? writeoffsCompareLoading : writeoffsLoading;
  const writeoffsPrevious = compareEnabled ? writeoffsCompare?.comparison : undefined;

  const losses = compareEnabled ? lossesCompare?.current : lossesFlat;
  const lossesLoadingEffective = compareEnabled ? lossesCompareLoading : lossesLoading;
  const lossesPrevious = compareEnabled ? lossesCompare?.comparison : undefined;

  // Row hover states
  const [hoveredRow, setHoveredRow] = useState<string | null>(null);
  const [hoveredReasonRow, setHoveredReasonRow] = useState<string | null>(null);
  const [hoveredZoneRow, setHoveredZoneRow] = useState<string | null>(null);
  const [hoveredCategoryRow, setHoveredCategoryRow] = useState<string | null>(null);
  const [hoveredLossRow, setHoveredLossRow] = useState<string | null>(null);

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
              <div style={{ ...tableWrapper, marginTop: 16 }}>
                <table style={{ width: "100%", borderCollapse: "collapse" }}>
                  <thead>
                    <tr>
                      <th style={thStyle("left")}>{t("headers.store")}</th>
                      <th style={thStyle("right")}>{tStatus("safe")}</th>
                      <th style={thStyle("right")}>{t("headers.warningShort")}</th>
                      <th style={thStyle("right")}>{tStatus("critical")}</th>
                      <th style={thStyle("right")}>{tStatus("expired")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {expiry.stores.map((s) => (
                      <tr
                        key={s.storeId}
                        onClick={() => router.push(`/stock?store_id=${s.storeId}`)}
                        onMouseEnter={() => setHoveredRow(s.storeId)}
                        onMouseLeave={() => setHoveredRow(null)}
                        style={rowHoverStyle(hoveredRow === s.storeId)}
                      >
                        <td style={tdText}>{s.storeName}</td>
                        <td style={{ ...tdNum, color: "#4ADE80" }}>
                          <span
                            onClick={(e) => { e.stopPropagation(); router.push(`/stock?store_id=${s.storeId}&status=safe`); }}
                            style={{ cursor: "pointer" }}
                          >
                            {s.safe}
                          </span>
                        </td>
                        <td style={{ ...tdNum, color: "#FBBF24" }}>
                          <span
                            onClick={(e) => { e.stopPropagation(); router.push(`/stock?store_id=${s.storeId}&status=warning`); }}
                            style={{ cursor: "pointer" }}
                          >
                            {s.warning}
                          </span>
                        </td>
                        <td style={{ ...tdNum, color: "#F87171" }}>
                          <span
                            onClick={(e) => { e.stopPropagation(); router.push(`/stock?store_id=${s.storeId}&status=critical`); }}
                            style={{ cursor: "pointer" }}
                          >
                            {s.critical}
                          </span>
                        </td>
                        <td style={{ ...tdNum, color: "#DC2626" }}>
                          <span
                            onClick={(e) => { e.stopPropagation(); router.push(`/stock?store_id=${s.storeId}&status=expired`); }}
                            style={{ cursor: "pointer" }}
                          >
                            {s.expired}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
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
                  storeId={selectedStoreId ?? undefined}
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
            <div style={{ ...tableWrapper, marginTop: 16 }}>
              <table style={{ width: "100%", borderCollapse: "collapse" }}>
                <thead>
                  <tr>
                    <th style={thStyle("left")}>{t("headers.reason")}</th>
                    <th style={thStyle("right")}>{t("writeOffs.headers.documentsCount")}</th>
                    <th style={thStyle("right")}>{t("headers.losses")}</th>
                  </tr>
                </thead>
                <tbody>
                  {writeoffs.byReason.map((r) => (
                    <tr
                      key={r.reason}
                      onClick={() => handleReasonClick(r.reason)}
                      onMouseEnter={() => setHoveredReasonRow(r.reason)}
                      onMouseLeave={() => setHoveredReasonRow(null)}
                      style={{
                        cursor: "pointer",
                        background:
                          hoveredReasonRow === r.reason || (selectedLossDimension?.type === "reason" && selectedLossDimension.value === r.reason)
                            ? "#0F1825"
                            : "transparent",
                        transition: "background 0.1s",
                      }}
                    >
                      <td style={tdText}>{tReason.has(r.reason) ? tReason(r.reason) : r.reason}</td>
                      <td style={tdNum}>{r.count}</td>
                      <td style={{ ...tdNum, color: "#F87171" }}>
                        {r.totalLoss.toLocaleString(intlLocale)} ₴
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {selectedLossDimension?.type === "reason" && (
            <LossesProductBreakdownPanel
              title={t("lossesProductPanelTitle", {
                value: tReason.has(selectedLossDimension.value) ? tReason(selectedLossDimension.value) : selectedLossDimension.value,
              })}
              totalLoss={writeoffs.byReason.find((r) => r.reason === selectedLossDimension.value)?.totalLoss ?? 0}
              reason={selectedLossDimension.value}
              storeId={selectedStoreId ?? undefined}
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
          <div style={tableWrapper}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th style={thStyle("left")}>{t("headers.zone")}</th>
                  <th style={thStyle("left")}>{t("headers.store")}</th>
                  <th style={thStyle("right")}>{tStatus("safe")}</th>
                  <th style={thStyle("right")}>{t("headers.warningShort")}</th>
                  <th style={thStyle("right")}>{tStatus("critical")}</th>
                  <th style={thStyle("right")}>{tStatus("expired")}</th>
                  <th style={thStyle("right")}>{t("headers.total")}</th>
                </tr>
              </thead>
              <tbody>
                {zones.map((z) => (
                  <tr
                    key={z.zoneId}
                    onClick={() => router.push(`/stock?zone_id=${z.zoneId}`)}
                    onMouseEnter={() => setHoveredZoneRow(z.zoneId)}
                    onMouseLeave={() => setHoveredZoneRow(null)}
                    style={rowHoverStyle(hoveredZoneRow === z.zoneId)}
                  >
                    <td style={tdText}>{z.zoneName}</td>
                    <td style={tdMuted}>{z.storeName}</td>
                    <td style={{ ...tdNum, color: "#4ADE80" }}>
                      <span
                        onClick={(e) => { e.stopPropagation(); router.push(`/stock?zone_id=${z.zoneId}&status=safe`); }}
                        style={{ cursor: "pointer" }}
                      >
                        {z.safe}
                      </span>
                    </td>
                    <td style={{ ...tdNum, color: "#FBBF24" }}>
                      <span
                        onClick={(e) => { e.stopPropagation(); router.push(`/stock?zone_id=${z.zoneId}&status=warning`); }}
                        style={{ cursor: "pointer" }}
                      >
                        {z.warning}
                      </span>
                    </td>
                    <td style={{ ...tdNum, color: "#F87171" }}>
                      <span
                        onClick={(e) => { e.stopPropagation(); router.push(`/stock?zone_id=${z.zoneId}&status=critical`); }}
                        style={{ cursor: "pointer" }}
                      >
                        {z.critical}
                      </span>
                    </td>
                    <td style={{ ...tdNum, color: "#DC2626" }}>
                      <span
                        onClick={(e) => { e.stopPropagation(); router.push(`/stock?zone_id=${z.zoneId}&status=expired`); }}
                        style={{ cursor: "pointer" }}
                      >
                        {z.expired}
                      </span>
                    </td>
                    <td style={tdNum}>{z.totalBatches}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* ── By category ───────────────────────────────────────────── */}
      {categories && categories.length > 0 && (
        <section>
          <h2 style={sectionTitle}>{t("byCategory.title")}</h2>
          <CategoryStatusChart data={categories} onCategoryClick={handleCategoryClick} selectedCategoryId={selectedCategoryId} />
          <div style={{ ...tableWrapper, marginTop: 16 }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th style={thStyle("left")}>{t("headers.category")}</th>
                  <th style={thStyle("right")}>{tStatus("safe")}</th>
                  <th style={thStyle("right")}>{t("headers.warningShort")}</th>
                  <th style={thStyle("right")}>{tStatus("critical")}</th>
                  <th style={thStyle("right")}>{tStatus("expired")}</th>
                  <th style={thStyle("right")}>{t("byCategory.headers.batches")}</th>
                  <th style={thStyle("right")}>{t("byCategory.headers.quantity")}</th>
                </tr>
              </thead>
              <tbody>
                {categories.map((c) => (
                  <tr
                    key={c.categoryId ?? "uncategorized"}
                    onClick={() => handleCategoryClick(c.categoryId)}
                    onMouseEnter={() => setHoveredCategoryRow(c.categoryId ?? "uncategorized")}
                    onMouseLeave={() => setHoveredCategoryRow(null)}
                    style={{
                      cursor: "pointer",
                      background:
                        hoveredCategoryRow === (c.categoryId ?? "uncategorized") || selectedCategoryId === c.categoryId
                          ? "#0F1825"
                          : "transparent",
                      transition: "background 0.1s",
                    }}
                  >
                    <td style={tdText}>{c.categoryName}</td>
                    <td style={{ ...tdNum, color: "#4ADE80" }}>{c.safe}</td>
                    <td style={{ ...tdNum, color: "#FBBF24" }}>{c.warning}</td>
                    <td style={{ ...tdNum, color: "#F87171" }}>{c.critical}</td>
                    <td style={{ ...tdNum, color: "#DC2626" }}>{c.expired}</td>
                    <td style={tdNum}>{c.totalBatches}</td>
                    <td style={tdNum}>{c.totalQuantity.toLocaleString(intlLocale)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {selectedCategoryId !== undefined && (
            <CategoryDetailPanel
              categoryId={selectedCategoryId}
              storeId={selectedStoreId ?? undefined}
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
          <div style={{ ...tableWrapper, marginTop: 16 }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th style={thStyle("left")}>{t("headers.store")}</th>
                  <th style={thStyle("right")}>{t("lossesByStore.headers.documents")}</th>
                  <th style={thStyle("right")}>{t("headers.losses")}</th>
                </tr>
              </thead>
              <tbody>
                {losses.byStore.map((s) => (
                  <tr
                    key={s.storeId}
                    onClick={() => handleStoreLossClick(s.storeId)}
                    onMouseEnter={() => setHoveredLossRow(s.storeId)}
                    onMouseLeave={() => setHoveredLossRow(null)}
                    style={{
                      cursor: "pointer",
                      background:
                        hoveredLossRow === s.storeId || (selectedLossDimension?.type === "store" && selectedLossDimension.value === s.storeId)
                          ? "#0F1825"
                          : "transparent",
                      transition: "background 0.1s",
                    }}
                  >
                    <td style={tdText}>{s.storeName}</td>
                    <td style={tdNum}>{s.writeOffCount}</td>
                    <td style={{ ...tdNum, color: "#F87171" }}>
                      {s.totalLoss.toLocaleString(intlLocale)} ₴
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {selectedLossDimension?.type === "store" && (
            <LossesProductBreakdownPanel
              title={t("lossesProductPanelTitle", {
                value: losses.byStore.find((s) => s.storeId === selectedLossDimension.value)?.storeName ?? selectedLossDimension.value,
              })}
              totalLoss={losses.byStore.find((s) => s.storeId === selectedLossDimension.value)?.totalLoss ?? 0}
              storeId={selectedLossDimension.value}
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
          (selectedStoreId, from the header's global store selector — see TASK-514) through the same
          way /analytics/pos threads its local store dropdown. */}
      {selectedProduct && (
        <ProductTrendPanel
          productId={selectedProduct.id}
          productName={selectedProduct.name}
          storeId={selectedStoreId ?? undefined}
          onClose={() => setSelectedProduct(null)}
        />
      )}
    </div>
  );
}
