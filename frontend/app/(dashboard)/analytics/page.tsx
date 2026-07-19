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
} from "@/features/analytics/hooks/useAnalytics";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_VIEW_ANALYTICS, hasRole } from "@/lib/roles";
import { useRequireTab } from "@/lib/useRequireTab";
import { ExpiryDonut } from "@/features/analytics/components/ExpiryDonut";
import { LossesByReasonChart } from "@/features/analytics/components/LossesByReasonChart";
import { LossesByStoreChart } from "@/features/analytics/components/LossesByStoreChart";
import { CategoryStatusChart } from "@/features/analytics/components/CategoryStatusChart";
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

  // Sidebar tab visibility (TASK-391c): mirrors Sidebar.tsx's /analytics NavItem gate
  // (roles: CAN_VIEW_ANALYTICS) OR'd with the "analytics" tabs claim. Reuses the combined
  // value below for both the route-guard redirect and the AccessDenied render, so a
  // tabs-granted user never hits a dead sidebar link. While `me` is still loading,
  // roleAccess is null — keep `access` null too so the existing loading/redirect
  // behavior below is unaffected until useMe() resolves.
  const effectiveAccess = useRequireTab("analytics", roleAccess === true);
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

  const { data: expiry, isLoading: expiryLoading } = useExpirySummary(undefined, enabled);
  const { data: writeoffsFlat, isLoading: writeoffsLoading } = useWriteOffAnalytics(
    { from, to },
    enabled && !compareEnabled,
  );
  const { data: writeoffsCompare, isLoading: writeoffsCompareLoading } = useWriteOffAnalyticsCompare(
    { from, to, compareFrom, compareTo },
    compareActive,
  );
  const { data: zones } = useZoneAnalytics(undefined, enabled);
  const { data: categories } = useCategoryAnalytics(undefined, enabled);
  const { data: lossesFlat, isLoading: lossesLoading } = useLosses({ from, to }, enabled && !compareEnabled);
  const { data: lossesCompare, isLoading: lossesCompareLoading } = useLossesCompare(
    { from, to, compareFrom, compareTo },
    compareActive,
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

          <LossesByReasonChart data={writeoffs.byReason} />

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
                      onClick={() => router.push(`/write-offs?reason=${r.reason}`)}
                      onMouseEnter={() => setHoveredReasonRow(r.reason)}
                      onMouseLeave={() => setHoveredReasonRow(null)}
                      style={rowHoverStyle(hoveredReasonRow === r.reason)}
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
          <CategoryStatusChart data={categories} />
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
                    onClick={() => router.push(`/inventory?category=${encodeURIComponent(c.categoryName)}`)}
                    onMouseEnter={() => setHoveredCategoryRow(c.categoryId ?? "uncategorized")}
                    onMouseLeave={() => setHoveredCategoryRow(null)}
                    style={rowHoverStyle(hoveredCategoryRow === (c.categoryId ?? "uncategorized"))}
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
          <LossesByStoreChart data={losses.byStore} />
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
                    onClick={() => router.push(`/write-offs?store_id=${s.storeId}`)}
                    onMouseEnter={() => setHoveredLossRow(s.storeId)}
                    onMouseLeave={() => setHoveredLossRow(null)}
                    style={rowHoverStyle(hoveredLossRow === s.storeId)}
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
        </section>
      )}
    </div>
  );
}
