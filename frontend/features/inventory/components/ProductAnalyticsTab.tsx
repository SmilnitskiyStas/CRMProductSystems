"use client";

import { useState, useMemo } from "react";
import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip,
  ResponsiveContainer, ReferenceLine, ReferenceArea,
} from "recharts";
import { Loader2, TrendingUp, ArrowDownToLine, Trash2, RefreshCw, Package, Wallet, Clock } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { useProductMovements } from "../hooks/useProductMovements";
import type { MovementDto } from "../api/movements";
import { useProductSalesTrend } from "@/features/analytics/hooks/usePosAnalytics";

// ── Line config ───────────────────────────────────────────────────────────────
// Labels come from i18n (`Dashboard.inventory.analytics.series`), built inside the
// component via `useTranslations` — mirrors the `buildNavGroups(t)` pattern in
// components/layout/Sidebar.tsx (i18n Block 1). Colors/dash/width stay static since
// they're not language-dependent.

type SeriesT = ReturnType<typeof useTranslations>;

/**
 * yAxisId is baked into each line descriptor (not inferred from `key` at render time) so the
 * quantity-scale lines (stock/movements) and the currency-scale lines (revenue/margin, TASK-484)
 * can never accidentally end up sharing — or missing — an axis. See the two-YAxis wiring in the
 * main chart below: every Line/ReferenceLine/ReferenceArea must carry an explicit yAxisId once a
 * second YAxis exists, since recharts silently drops (not errors on) an axis-id mismatch.
 */
function buildLines(t: SeriesT, opts?: { includeRevenue?: boolean; includeMargin?: boolean }) {
  const lines: Array<{ key: string; label: string; color: string; dash: string | undefined; width: number; yAxisId: "quantity" | "revenue" }> = [
    { key: "stock",      label: t("stock"),      color: "#38BDF8", dash: undefined, width: 2.5, yAxisId: "quantity" },
    { key: "receipt",    label: t("receipt"),    color: "#3B82F6", dash: "6 3",     width: 1.5, yAxisId: "quantity" },
    { key: "transfer",   label: t("transfer"),   color: "#A78BFA", dash: undefined, width: 1.5, yAxisId: "quantity" },
    { key: "write_off",  label: t("write_off"),  color: "#F87171", dash: undefined, width: 1.5, yAxisId: "quantity" },
    { key: "adjustment", label: t("adjustment"), color: "#4ADE80", dash: undefined, width: 1.5, yAxisId: "quantity" },
  ];
  if (opts?.includeRevenue) {
    lines.push({ key: "revenue", label: t("revenue"), color: "#FBBF24", dash: undefined, width: 2, yAxisId: "revenue" });
  }
  if (opts?.includeMargin) {
    lines.push({ key: "margin", label: t("margin"), color: "#34D399", dash: "4 2", width: 1.5, yAxisId: "revenue" });
  }
  return lines;
}

function buildMovementLabels(t: SeriesT): Record<string, string> {
  return {
    receipt:    t("receipt"),
    transfer:   t("transfer"),
    write_off:  t("write_off"),
    adjustment: t("adjustment"),
  };
}

// ── Zone config (background bands) ───────────────────────────────────────────

type ZoneKey = "critical" | "low" | "normal" | "excess";

const ZONES: { key: ZoneKey; color: string; fill: string }[] = [
  { key: "critical", color: "#EF4444", fill: "#EF444418" },
  { key: "low",       color: "#FACC15", fill: "#FACC1514" },
  { key: "normal",    color: "#22C55E", fill: "#22C55E0C" },
  { key: "excess",    color: "#3B82F6", fill: "#3B82F60A" },
];

function zoneForStock(stock: number, buffers: ProductBuffers): { key: ZoneKey; color: string } {
  if (stock <= buffers.safetyBuffer) return { key: "critical", color: "#EF4444" };
  if (stock <= buffers.minStock)     return { key: "low",      color: "#FACC15" };
  if (stock <= buffers.maxStock)     return { key: "normal",   color: "#22C55E" };
  return                                    { key: "excess",   color: "#3B82F6" };
}

// ── Buffer reference lines ────────────────────────────────────────────────────

const BUFFER_LINES: { key: "safetyBuffer" | "minStock" | "maxStock"; color: string; dash: string }[] = [
  { key: "safetyBuffer", color: "#F97316", dash: "4 2" },
  { key: "minStock",     color: "#FACC15", dash: "4 2" },
  { key: "maxStock",     color: "#34D399", dash: "4 2" },
];

export interface ProductBuffers {
  safetyBuffer: number;
  minStock: number;
  maxStock: number;
}

// ── Date ranges ───────────────────────────────────────────────────────────────

const RANGE_DAYS = [7, 14, 30, 90, 180, 365] as const;
const RANGE_KEY: Record<number, string> = {
  7: "d7", 14: "d14", 30: "d30", 90: "d90", 180: "d180", 365: "y1",
};

function daysAgoStr(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() - days);
  return d.toISOString().slice(0, 10);
}

function todayStr(): string {
  return new Date().toISOString().slice(0, 10);
}

// ── Chart data ────────────────────────────────────────────────────────────────

interface ChartPoint {
  date: string;
  stock: number | null;
  receipt: number;
  transfer: number;
  write_off: number;
  adjustment: number;
  /** Revenue/quantitySold/margin (TASK-484) are only populated when showRevenueSeries is true —
   * merged in separately below, never touched by groupByDay itself. */
  revenue?: number;
  quantitySold?: number;
  /** null = a real sale happened that day but margin is unknown (no Item.PricePurchase on file,
   * or the caller can't view margin) — distinct from 0 (a sale day with exactly zero margin) and
   * from "no key at all" (not a showRevenueSeries render). Rendered as a connectNulls gap, not
   * coerced to 0 — unlike revenue/quantitySold, where "no sales that day" legitimately is 0. */
  margin?: number | null;
}

/** Same green/red/gray-by-sign convention as CategoryDetailPanel.tsx's marginColor (interactive
 * analytics + margin plan) — kept as a local copy since these two components don't share a
 * "chart formatting" module today, mirroring how color tokens are inlined throughout this file. */
function marginColor(v: number | null | undefined): string {
  if (v == null || v === 0) return "#6B7280";
  return v > 0 ? "#4ADE80" : "#F87171";
}

/** Urgency color for daysOfStockRemaining (TASK-494) — reuses the exact critical/warning/safe
 * hex triple CategoryDetailPanel.tsx's own daysOfStockColor uses (#F87171/#FBBF24/#4ADE80), so
 * the same product reads with the same urgency tone in both places. Kept as a local copy rather
 * than a shared import — same "no shared chart/table formatting module today" reason
 * marginColor's own comment above already documents for this file. null (no store context, or
 * no ADU signal yet — see daysOfStockRemaining's doc comment on this component's props below)
 * renders neutral gray, matching marginColor's null/zero tone. */
function daysOfStockColor(v: number | null | undefined): string {
  if (v == null) return "#6B7280";
  if (v < 7) return "#F87171";
  if (v < 30) return "#FBBF24";
  return "#4ADE80";
}

function groupByDay(items: MovementDto[], from: string): ChartPoint[] {
  const map = new Map<string, ChartPoint>();

  const start = new Date(from);
  const today = new Date();
  for (let d = new Date(start); d <= today; d.setDate(d.getDate() + 1)) {
    const key = d.toISOString().slice(0, 10);
    map.set(key, { date: key, stock: null, receipt: 0, transfer: 0, write_off: 0, adjustment: 0 });
  }

  // Sort ascending so last quantityAfter per day is the end-of-day balance
  const sorted = [...items].sort((a, b) => a.createdAt.localeCompare(b.createdAt));

  for (const m of sorted) {
    const key = m.createdAt.slice(0, 10);
    const pt = map.get(key);
    if (!pt) continue;

    if (m.movementType === "receipt")    pt.receipt    += Math.abs(m.quantity);
    if (m.movementType === "transfer")   pt.transfer   += Math.abs(m.quantity);
    if (m.movementType === "write_off")  pt.write_off  += Math.abs(m.quantity);
    if (m.movementType === "adjustment") pt.adjustment += Math.abs(m.quantity);
    if (m.quantityAfter != null) pt.stock = m.quantityAfter;
  }

  // Forward-fill stock for days with no movements
  let lastStock: number | null = null;
  for (const pt of map.values()) {
    if (pt.stock !== null) lastStock = pt.stock;
    else if (lastStock !== null) pt.stock = lastStock;
  }

  return Array.from(map.values());
}

function formatDateTick(iso: string, intlLocale: string): string {
  return new Date(iso).toLocaleDateString(intlLocale, { day: "numeric", month: "short" });
}

// ── Summary cards ─────────────────────────────────────────────────────────────

function SummaryCard({
  icon, label, value, sub, color,
}: {
  icon: React.ReactNode; label: string; value: string | number; sub?: string; color: string;
}) {
  return (
    <div style={{
      background: "#0D1117", border: "1px solid #1F2937",
      borderRadius: 8, padding: "10px 14px",
      display: "flex", alignItems: "center", gap: 10,
    }}>
      <div style={{
        width: 32, height: 32, borderRadius: 7, flexShrink: 0,
        background: `${color}12`, border: `1px solid ${color}30`,
        display: "flex", alignItems: "center", justifyContent: "center",
      }}>
        {icon}
      </div>
      <div>
        <div style={{ color: "#4B5563", fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>
          {label}
        </div>
        <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, fontFamily: "monospace", marginTop: 1 }}>
          {value}
        </div>
        {sub && <div style={{ color: "#4B5563", fontSize: 10, marginTop: 1 }}>{sub}</div>}
      </div>
    </div>
  );
}

// ── Legend ────────────────────────────────────────────────────────────────────

function ChartLegend({
  showBuffers,
  hidden,
  onToggle,
  lines,
}: {
  showBuffers: boolean;
  hidden: Set<string>;
  onToggle: (key: string) => void;
  lines: ReturnType<typeof buildLines>;
}) {
  const t = useTranslations("Dashboard.inventory.analytics");
  const tFields = useTranslations("Dashboard.inventory.fields");

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8, marginBottom: 14 }}>
      {/* Clickable data lines */}
      <div style={{ display: "flex", flexWrap: "wrap", gap: "4px 12px" }}>
        {lines.map(({ key, label, color, dash, width }) => {
          const off = hidden.has(key);
          return (
            <button
              key={key}
              onClick={() => onToggle(key)}
              title={off ? t("legend.show", { label }) : t("legend.hide", { label })}
              style={{
                display: "flex", alignItems: "center", gap: 5,
                background: off ? "#0A0F1A" : "transparent",
                border: `1px solid ${off ? "#1F2937" : "transparent"}`,
                borderRadius: 6, padding: "3px 8px",
                cursor: "pointer", transition: "all 0.15s",
                opacity: off ? 0.45 : 1,
              }}
            >
              <svg width="24" height="10">
                <line
                  x1="0" y1="5" x2="24" y2="5"
                  stroke={off ? "#4B5563" : color}
                  strokeWidth={width}
                  strokeDasharray={dash ?? "none"}
                />
              </svg>
              <span style={{
                fontSize: 11,
                fontWeight: key === "stock" ? 600 : 400,
                color: off ? "#374151" : (key === "stock" ? "#9CA3AF" : "#6B7280"),
                textDecoration: off ? "line-through" : "none",
              }}>
                {label}
              </span>
            </button>
          );
        })}
      </div>

      {/* Buffer lines + zones (display-only, not toggleable) */}
      {showBuffers && (
        <div style={{ display: "flex", flexWrap: "wrap", gap: "6px 16px", paddingTop: 6, borderTop: "1px solid #1F2937" }}>
          {BUFFER_LINES.map(({ key, color, dash }) => (
            <div key={key} style={{ display: "flex", alignItems: "center", gap: 5 }}>
              <svg width="24" height="10">
                <line x1="0" y1="5" x2="24" y2="5" stroke={color} strokeWidth="1.5" strokeDasharray={dash} />
              </svg>
              <span style={{ color: "#6B7280", fontSize: 11 }}>{tFields(key)}</span>
            </div>
          ))}
          {ZONES.map(({ key, color, fill }) => (
            <div key={key} style={{ display: "flex", alignItems: "center", gap: 5 }}>
              <div style={{
                width: 14, height: 10, borderRadius: 2,
                background: fill, border: `1px solid ${color}50`,
              }} />
              <span style={{ color: "#6B7280", fontSize: 11 }}>{t(`zones.${key}`)}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Tooltip ───────────────────────────────────────────────────────────────────

function CustomTooltip({ active, payload, label, buffers }: {
  active?: boolean;
  payload?: { dataKey: string; value: number | null; color: string }[];
  label?: string;
  buffers?: ProductBuffers;
}) {
  const t = useTranslations("Dashboard.inventory.analytics");
  const tSeries = useTranslations("Dashboard.inventory.analytics.series");
  const tFields = useTranslations("Dashboard.inventory.fields");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const lines = buildLines(tSeries);

  if (!active || !payload?.length) return null;

  const stock = payload.find((p) => p.dataKey === "stock")?.value ?? null;
  // revenue/margin (TASK-484) get their own dedicated rows below — currency-formatted with a ₴
  // suffix, unlike the plain quantity movements this generic filter still handles.
  const revenueEntry = payload.find((p) => p.dataKey === "revenue");
  const marginEntry = payload.find((p) => p.dataKey === "margin");
  const movements = payload.filter(
    (p) => p.dataKey !== "stock" && p.dataKey !== "revenue" && p.dataKey !== "margin" && (p.value ?? 0) > 0,
  );

  // Determine zone
  const zone = buffers && stock != null ? zoneForStock(stock, buffers) : null;

  return (
    <div style={{
      background: "#111827", border: "1px solid #1F2937",
      borderRadius: 10, padding: "10px 14px", fontSize: 12, minWidth: 180,
    }}>
      <div style={{ color: "#6B7280", fontSize: 11, marginBottom: 8, fontWeight: 600 }}>
        {label ? new Date(label).toLocaleDateString(intlLocale, { day: "numeric", month: "long", year: "numeric" }) : ""}
      </div>

      {stock != null && (
        <div style={{ display: "flex", justifyContent: "space-between", gap: 16, marginBottom: 6, paddingBottom: 6, borderBottom: "1px solid #1F2937" }}>
          <span style={{ color: "#38BDF8", fontWeight: 600 }}>{t("series.stock")}</span>
          <span style={{ color: "#38BDF8", fontFamily: "monospace", fontWeight: 700 }}>{stock}</span>
        </div>
      )}

      {revenueEntry && (
        <div style={{ display: "flex", justifyContent: "space-between", gap: 16, marginBottom: 6, paddingBottom: 6, borderBottom: "1px solid #1F2937" }}>
          <span style={{ color: "#FBBF24", fontWeight: 600 }}>{tSeries("revenue")}</span>
          <span style={{ color: "#FBBF24", fontFamily: "monospace", fontWeight: 700 }}>
            {(revenueEntry.value ?? 0).toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴
          </span>
        </div>
      )}

      {marginEntry && (
        <div style={{ display: "flex", justifyContent: "space-between", gap: 16, marginBottom: 6, paddingBottom: 6, borderBottom: "1px solid #1F2937" }}>
          <span style={{ color: marginColor(marginEntry.value) }}>{tSeries("margin")}</span>
          <span style={{ color: marginColor(marginEntry.value), fontFamily: "monospace" }}>
            {marginEntry.value == null ? "—" : `${marginEntry.value.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`}
          </span>
        </div>
      )}

      {zone && (
        <div style={{ display: "flex", justifyContent: "space-between", gap: 16, marginBottom: 6, paddingBottom: 6, borderBottom: "1px solid #1F2937" }}>
          <span style={{ color: "#4B5563" }}>{t("tooltipZoneLabel")}</span>
          <span style={{ color: zone.color, fontWeight: 600, fontSize: 11 }}>{t(`zones.${zone.key}`)}</span>
        </div>
      )}

      {movements.map((p) => {
        const line = lines.find((l) => l.key === p.dataKey);
        return (
          <div key={p.dataKey} style={{ display: "flex", justifyContent: "space-between", gap: 16 }}>
            <span style={{ color: "#6B7280" }}>{line?.label ?? p.dataKey}</span>
            <span style={{ color: p.color, fontFamily: "monospace" }}>{p.value}</span>
          </div>
        );
      })}

      {buffers && stock != null && (
        <div style={{ marginTop: 8, paddingTop: 6, borderTop: "1px solid #1F2937", display: "flex", flexDirection: "column", gap: 3 }}>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 16 }}>
            <span style={{ color: "#4B5563", fontSize: 10 }}>{tFields("safetyBuffer")}</span>
            <span style={{ color: "#F97316", fontFamily: "monospace", fontSize: 10 }}>{buffers.safetyBuffer}</span>
          </div>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 16 }}>
            <span style={{ color: "#4B5563", fontSize: 10 }}>{tFields("minStock")}</span>
            <span style={{ color: "#FACC15", fontFamily: "monospace", fontSize: 10 }}>{buffers.minStock}</span>
          </div>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 16 }}>
            <span style={{ color: "#4B5563", fontSize: 10 }}>{tFields("maxStock")}</span>
            <span style={{ color: "#34D399", fontFamily: "monospace", fontSize: 10 }}>{buffers.maxStock}</span>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Main component ────────────────────────────────────────────────────────────

export function ProductAnalyticsTab({
  productId,
  chartHeight = 220,
  buffers,
  showRevenueSeries = false,
  canViewMargin,
  daysOfStockRemaining,
}: {
  productId: string;
  chartHeight?: number;
  buffers?: ProductBuffers;
  /** TASK-484 (interactive analytics + margin plan): adds a revenue line (and margin, when
   * canViewMargin) on a second right-hand YAxis, driven by useProductSalesTrend. Defaults false
   * so the existing /inventory/{id}?tab=analytics call sites (ProductsTable.tsx,
   * inventory/[id]/page.tsx — neither passes this) are completely unaffected. Only
   * ProductTrendPanel.tsx passes true, for its inline drill-down on /analytics/pos and /analytics
   * (renamed from PosProductTrendPanel in TASK-488 when the latter page started reusing it). */
  showRevenueSeries?: boolean;
  /** Purely a rendering gate — this component stays presentational w.r.t. authorization (same as
   * it doesn't call useMe()/roles.ts for `buffers` today); the caller (ProductTrendPanel)
   * resolves this via canViewAnalyticsMargin and passes the result down. The server independently
   * nulls marginAmount for unauthorized callers regardless (ADR-027), so this is defense in depth,
   * not the only gate. */
  canViewMargin?: boolean;
  /** TASK-494: optional pre-computed days-of-stock-remaining figure (currentStock / ADU
   * aduEffective, same division this component knows nothing about) — this component stays
   * presentational here too, same posture as canViewMargin above: it never fetches ADU/stock
   * data itself, the caller sources the value and passes it in (or doesn't). Three distinct
   * states, "absent vs empty" (matches CategoryProductRowDto.daysOfStockRemaining's own
   * null-semantics in frontend/features/analytics/types.ts):
   * - `undefined` (prop omitted entirely) — the caller has no store context to compute this in
   *   (e.g. ProductTrendPanel on /analytics, which has no page-wide store filter) — renders no
   *   card at all, not even one showing "—".
   * - `null` (prop explicitly passed as null) — the caller DOES have a store context but no
   *   usage signal yet (no ProductAdu row, or aduEffective is 0/null) — renders the card with a
   *   "—" value.
   * - a number — renders the card with that many days, color-coded by daysOfStockColor above. */
  daysOfStockRemaining?: number | null;
}) {
  const t = useTranslations("Dashboard.inventory.analytics");
  const tSeries = useTranslations("Dashboard.inventory.analytics.series");
  const tFields = useTranslations("Dashboard.inventory.fields");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const includeMargin = showRevenueSeries && !!canViewMargin;
  const lines = useMemo(
    () => buildLines(tSeries, { includeRevenue: showRevenueSeries, includeMargin }),
    [tSeries, showRevenueSeries, includeMargin],
  );
  const movementLabels = useMemo(() => buildMovementLabels(tSeries), [tSeries]);

  const [rangeDays, setRangeDays] = useState(30);
  const [hiddenLines, setHiddenLines] = useState<Set<string>>(new Set());

  function toggleLine(key: string) {
    setHiddenLines((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  const from = daysAgoStr(rangeDays);
  const to   = todayStr();

  const { data, isLoading } = useProductMovements(productId, {
    from,
    to,
    page_size: 500,
  });

  const movements = data?.items ?? [];

  const chartData = useMemo(
    () => groupByDay(movements, from),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [movements, from],
  );

  // TASK-484: always group_by "day" regardless of rangeDays — the movement chart's x-axis is
  // always day-level, and the two series must align on the same date keys.
  const { data: trendData } = useProductSalesTrend(
    productId,
    { from, to, group_by: "day" },
    showRevenueSeries,
  );
  const trendPoints = trendData?.points ?? [];

  // Merge onto the movement-derived chartData by matching date strings — a day with no sales
  // becomes 0 (not a gap, unlike stock's forward-fill), a day with sales but unknown margin
  // stays null (see ChartPoint's margin doc comment above).
  const mergedChartData = useMemo((): ChartPoint[] => {
    if (!showRevenueSeries) return chartData;
    const byDate = new Map(trendPoints.map((p) => [p.date, p]));
    return chartData.map((pt) => {
      const trend = byDate.get(pt.date);
      return {
        ...pt,
        revenue: trend?.revenue ?? 0,
        quantitySold: trend?.quantity ?? 0,
        margin: includeMargin ? (trend ? trend.marginAmount : 0) : undefined,
      };
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chartData, trendPoints, showRevenueSeries, includeMargin]);

  const revenueTotal = useMemo(
    () => trendPoints.reduce((s, p) => s + p.revenue, 0),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [trendPoints],
  );

  const totals = useMemo(() => ({
    receipt:    movements.filter((m) => m.movementType === "receipt").reduce((s, m) => s + m.quantity, 0),
    transfer:   movements.filter((m) => m.movementType === "transfer").reduce((s, m) => s + Math.abs(m.quantity), 0),
    write_off:  movements.filter((m) => m.movementType === "write_off").reduce((s, m) => s + Math.abs(m.quantity), 0),
    adjustment: movements.filter((m) => m.movementType === "adjustment").reduce((s, m) => s + Math.abs(m.quantity), 0),
  }), [movements]);

  // Current stock = last known quantityAfter in the period
  const currentStock = useMemo(() => {
    const sorted = [...movements].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
    return sorted.find((m) => m.quantityAfter != null)?.quantityAfter ?? null;
  }, [movements]);

  // Determine current zone
  const currentZone = useMemo(() => {
    if (!buffers || currentStock == null) return null;
    return zoneForStock(currentStock, buffers);
  }, [buffers, currentStock]);

  if (isLoading) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", padding: "48px 0" }}>
        <Loader2 size={28} color="#374151" style={{ animation: "spin 1s linear infinite" }} />
        <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
      </div>
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>

      {/* Date range selector */}
      <div style={{ display: "flex", alignItems: "center", gap: 6, flexWrap: "wrap" }}>
        {RANGE_DAYS.map((days) => (
          <button
            key={days}
            onClick={() => setRangeDays(days)}
            style={{
              padding: "4px 12px", borderRadius: 6, fontSize: 12, fontWeight: 600,
              cursor: "pointer",
              background: rangeDays === days ? "#1D3461" : "transparent",
              border: `1px solid ${rangeDays === days ? "#3B82F6" : "#1F2937"}`,
              color: rangeDays === days ? "#60A5FA" : "#6B7280",
              transition: "all 0.1s",
            }}
          >
            {t(`ranges.${RANGE_KEY[days]}`)}
          </button>
        ))}
        <span style={{ color: "#4B5563", fontSize: 11, marginLeft: 4 }}>
          {new Date(from).toLocaleDateString(intlLocale, { day: "numeric", month: "short" })}
          {" — "}
          {new Date(to).toLocaleDateString(intlLocale, { day: "numeric", month: "short", year: "numeric" })}
        </span>
      </div>

      {/* Summary cards */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))", gap: 8 }}>
        {currentStock != null && (
          <SummaryCard
            icon={<Package size={14} color="#38BDF8" />}
            label={t("currentStock")}
            value={currentStock}
            sub={currentZone ? t(`zones.${currentZone.key}`) : undefined}
            color="#38BDF8"
          />
        )}
        {daysOfStockRemaining !== undefined && (
          <SummaryCard
            icon={<Clock size={14} color={daysOfStockColor(daysOfStockRemaining)} />}
            label={t("daysOfStockRemaining")}
            value={
              daysOfStockRemaining == null
                ? "—"
                : t("daysOfStockValue", { days: daysOfStockRemaining.toLocaleString(intlLocale, { maximumFractionDigits: 1 }) })
            }
            color={daysOfStockColor(daysOfStockRemaining)}
          />
        )}
        {showRevenueSeries && (
          <SummaryCard
            icon={<Wallet size={14} color="#FBBF24" />}
            label={tSeries("revenue")}
            value={`${revenueTotal.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`}
            color="#FBBF24"
          />
        )}
        <SummaryCard icon={<ArrowDownToLine size={14} color="#3B82F6" />} label={tSeries("receipt")} value={totals.receipt}    color="#3B82F6" />
        <SummaryCard icon={<TrendingUp      size={14} color="#A78BFA" />} label={tSeries("transfer")} value={totals.transfer}   color="#A78BFA" />
        <SummaryCard icon={<Trash2          size={14} color="#F87171" />} label={tSeries("write_off")} value={totals.write_off}  color="#F87171" />
        <SummaryCard icon={<RefreshCw       size={14} color="#4ADE80" />} label={tSeries("adjustment")} value={totals.adjustment} color="#4ADE80" />
      </div>

      {/* Chart */}
      <div style={{
        background: "#0D1117", border: "1px solid #1F2937",
        borderRadius: 10, padding: "16px 16px 8px",
      }}>
        <div style={{ display: "flex", alignItems: "baseline", gap: 8, marginBottom: 4 }}>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{t("chartTitle")}</div>
          <div style={{ color: "#4B5563", fontSize: 11 }}>{t("eventsSummary", { count: movements.length, days: rangeDays })}</div>
        </div>

        <ChartLegend showBuffers={!!buffers} hidden={hiddenLines} onToggle={toggleLine} lines={lines} />

        {movements.length === 0 ? (
          <div style={{ textAlign: "center", padding: "32px 0", color: "#374151", fontSize: 13 }}>
            {t("noMovements")}
          </div>
        ) : (
          <ResponsiveContainer width="100%" height={chartHeight}>
            <LineChart data={mergedChartData} margin={{ left: 0, right: showRevenueSeries ? 0 : 20, top: 4, bottom: 4 }}>

              {/* Zone backgrounds — rendered first so they're behind everything. Explicit
                  yAxisId="quantity" is required the moment a second YAxis exists below (recharts
                  defaults to the first-declared axis by id match, not by "the only other one") */}
              {buffers && (
                <>
                  <ReferenceArea yAxisId="quantity" y1={0}                    y2={buffers.safetyBuffer} fill="#EF444418" />
                  <ReferenceArea yAxisId="quantity" y1={buffers.safetyBuffer} y2={buffers.minStock}     fill="#FACC1514" />
                  <ReferenceArea yAxisId="quantity" y1={buffers.minStock}     y2={buffers.maxStock}     fill="#22C55E0C" />
                  <ReferenceArea yAxisId="quantity" y1={buffers.maxStock}     y2={99999}                fill="#3B82F60A" />
                </>
              )}

              <CartesianGrid strokeDasharray="3 3" stroke="#1F2937" vertical={false} />
              <XAxis
                dataKey="date"
                tick={{ fill: "#6B7280", fontSize: 10 }}
                axisLine={false}
                tickLine={false}
                tickFormatter={(value: string) => formatDateTick(value, intlLocale)}
                interval="preserveStartEnd"
              />
              <YAxis
                yAxisId="quantity"
                tick={{ fill: "#4B5563", fontSize: 10 }}
                axisLine={false}
                tickLine={false}
                width={36}
              />
              {/* Second, currency-scale axis (TASK-484) — only mounted when revenue/margin lines
                  are actually rendered below, both of which explicitly declare yAxisId="revenue". */}
              {showRevenueSeries && (
                <YAxis
                  yAxisId="revenue"
                  orientation="right"
                  tick={{ fill: "#4B5563", fontSize: 10 }}
                  axisLine={false}
                  tickLine={false}
                  width={48}
                  tickFormatter={(v: number) => `${(v / 1000).toFixed(0)}${locale === "en" ? "k" : "к"}`}
                />
              )}
              <Tooltip
                content={<CustomTooltip buffers={buffers} />}
              />

              {/* Buffer reference lines */}
              {buffers && (
                <>
                  <ReferenceLine
                    yAxisId="quantity"
                    y={buffers.safetyBuffer}
                    stroke="#F97316" strokeWidth={1.5} strokeDasharray="4 2"
                    label={{ value: t("bufferShort.safetyBuffer", { value: buffers.safetyBuffer }), fill: "#F97316", fontSize: 10, position: "insideTopRight" }}
                  />
                  <ReferenceLine
                    yAxisId="quantity"
                    y={buffers.minStock}
                    stroke="#FACC15" strokeWidth={1.5} strokeDasharray="4 2"
                    label={{ value: t("bufferShort.minStock", { value: buffers.minStock }), fill: "#FACC15", fontSize: 10, position: "insideTopRight" }}
                  />
                  <ReferenceLine
                    yAxisId="quantity"
                    y={buffers.maxStock}
                    stroke="#34D399" strokeWidth={1.5} strokeDasharray="4 2"
                    label={{ value: t("bufferShort.maxStock", { value: buffers.maxStock }), fill: "#34D399", fontSize: 10, position: "insideTopRight" }}
                  />
                </>
              )}

              {/* Stock balance line — most prominent */}
              <Line
                yAxisId="quantity"
                type="monotone"
                dataKey="stock"
                stroke="#38BDF8"
                strokeWidth={2.5}
                dot={false}
                activeDot={{ r: 4, fill: "#38BDF8" }}
                connectNulls
                hide={hiddenLines.has("stock")}
              />

              {/* Movement lines + revenue/margin (TASK-484) — yAxisId comes from buildLines so
                  quantity-scale and currency-scale series can never end up on the wrong axis. */}
              {lines.filter((l) => l.key !== "stock").map(({ key, color, dash, width, yAxisId }) => (
                <Line
                  key={key}
                  yAxisId={yAxisId}
                  type="monotone"
                  dataKey={key}
                  stroke={color}
                  strokeWidth={width}
                  strokeDasharray={dash}
                  dot={false}
                  activeDot={{ r: 3, fill: color }}
                  hide={hiddenLines.has(key)}
                  connectNulls={key === "margin"}
                />
              ))}
            </LineChart>
          </ResponsiveContainer>
        )}
      </div>

      {/* Movement log */}
      {movements.length > 0 && (
        <div>
          <div style={{
            color: "#4B5563", fontSize: 11, fontWeight: 600,
            textTransform: "uppercase", letterSpacing: "0.05em",
            marginBottom: 8, paddingBottom: 6, borderBottom: "1px solid #1F2937",
          }}>
            {t("eventLog")}
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 4, maxHeight: 320, overflowY: "auto" }}>
            {movements.slice(0, 50).map((m) => {
              const line = lines.find((l) => l.key === m.movementType);
              const isPlus = m.movementType === "receipt" || m.movementType === "adjustment";
              return (
                <div
                  key={m.id}
                  style={{
                    display: "flex", alignItems: "center", gap: 10,
                    padding: "7px 12px",
                    background: "#0A0F1A", border: "1px solid #1F2937",
                    borderRadius: 7,
                  }}
                >
                  <div style={{
                    width: 8, height: 8, borderRadius: "50%", flexShrink: 0,
                    background: line?.color ?? "#6B7280",
                  }} />

                  <span style={{
                    fontSize: 11, padding: "1px 7px", borderRadius: 4, flexShrink: 0,
                    background: `${line?.color ?? "#6B7280"}15`,
                    color: line?.color ?? "#6B7280",
                    fontWeight: 600,
                  }}>
                    {movementLabels[m.movementType] ?? m.movementType}
                  </span>

                  <span style={{
                    color: "#9CA3AF", fontSize: 11, flex: 1, minWidth: 0,
                    overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
                  }}>
                    {m.fromStoreName && m.toStoreName
                      ? `${m.fromStoreName} → ${m.toStoreName}`
                      : m.fromStoreName ?? m.toStoreName ?? "—"}
                  </span>

                  {/* quantityBefore → quantityAfter */}
                  {m.quantityBefore != null && m.quantityAfter != null ? (
                    <span style={{ color: "#4B5563", fontSize: 11, flexShrink: 0, whiteSpace: "nowrap" }}>
                      <span style={{ fontFamily: "monospace" }}>{m.quantityBefore}</span>
                      <span style={{ margin: "0 4px" }}>→</span>
                      <span style={{ fontFamily: "monospace", color: "#38BDF8" }}>{m.quantityAfter}</span>
                    </span>
                  ) : m.quantityAfter != null ? (
                    <span style={{ color: "#4B5563", fontSize: 11, flexShrink: 0 }}>
                      {t("balancePrefix")} <span style={{ fontFamily: "monospace", color: "#38BDF8" }}>{m.quantityAfter}</span>
                    </span>
                  ) : null}

                  <span style={{
                    fontFamily: "monospace", fontSize: 12, flexShrink: 0,
                    color: isPlus ? "#4ADE80" : "#F87171",
                  }}>
                    {isPlus ? `+${m.quantity}` : `−${Math.abs(m.quantity)}`}
                  </span>

                  <span style={{ color: "#4B5563", fontSize: 10, flexShrink: 0 }}>
                    {new Date(m.createdAt).toLocaleDateString(intlLocale, { day: "2-digit", month: "2-digit" })}
                  </span>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
