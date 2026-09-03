"use client";

// "Моя ефективність" — supplier self-stats (Phase 6c) + composite quality score (Phase 6d).
// Current values from GET /metrics, daily history + period-over-period deltas from
// GET /metrics-history. Trend charts reuse features/marketplace SupplierMetricTrendChart.

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { TrendingUp, TrendingDown, Minus } from "lucide-react";
import {
  SupplierMetricTrendChart,
  type MetricTrendPoint,
  type MetricTrendUnit,
} from "@/features/marketplace/components/SupplierMetricTrendChart";
import type { SupplierMetricsHistoryPoint } from "@/features/marketplace/types";
import { useCabinetMetrics, useSupplierMetricsHistory } from "../hooks/useSupplierCabinet";
import type { SupplierPeriodMetric } from "../types";

const DAYS_OPTIONS = [30, 90, 365] as const;

/** Direction that counts as an improvement for a given metric. */
type Good = "up" | "down";

function DeltaBadge({ delta, good }: { delta: SupplierPeriodMetric | null; good: Good }) {
  const t = useTranslations("Dashboard.supplierCabinet.performance");
  if (!delta || delta.percentChange == null) {
    return <span style={{ color: "#6B7280", fontSize: 11, fontFamily: "monospace" }}>—</span>;
  }
  const pc = delta.percentChange;
  const improving = pc === 0 ? null : good === "up" ? pc > 0 : pc < 0;
  const color = improving === null ? "#6B7280" : improving ? "#4ADE80" : "#F87171";
  const Icon = pc > 0 ? TrendingUp : pc < 0 ? TrendingDown : Minus;
  return (
    <span
      title={t("vsPreviousPeriod")}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 3,
        fontSize: 11,
        fontWeight: 600,
        color,
        fontFamily: "monospace",
        whiteSpace: "nowrap",
      }}
    >
      <Icon size={12} strokeWidth={2.5} />
      {`${pc > 0 ? "+" : ""}${pc.toFixed(1)}%`}
    </span>
  );
}

interface MetricRowProps {
  title: string;
  value: string;
  delta: SupplierPeriodMetric | null;
  good: Good;
  points: MetricTrendPoint[];
  unit: MetricTrendUnit;
  color?: string;
}

function MetricRow({ title, value, delta, good, points, unit, color }: MetricRowProps) {
  const t = useTranslations("Dashboard.supplierCabinet.performance");
  return (
    <section style={{ marginBottom: 28 }}>
      <div style={{ display: "flex", alignItems: "baseline", gap: 12, flexWrap: "wrap", marginBottom: 8 }}>
        <h3 style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, margin: 0 }}>{title}</h3>
        <span style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700 }}>{value}</span>
        <span style={{ display: "inline-flex", alignItems: "center", gap: 5 }}>
          <DeltaBadge delta={delta} good={good} />
          <span style={{ color: "#4B5563", fontSize: 11 }}>{t("vsPreviousPeriodShort")}</span>
        </span>
      </div>
      <SupplierMetricTrendChart points={points} unit={unit} label={title} color={color} />
    </section>
  );
}

export function SupplierPerformanceView() {
  const t = useTranslations("Dashboard.supplierCabinet.performance");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const [days, setDays] = useState<number>(90);

  const { data: metrics, isLoading: metricsLoading } = useCabinetMetrics();
  const { data: history, isLoading: historyLoading, isError } = useSupplierMetricsHistory(days);

  const points = history?.points ?? [];
  const deltas = history?.deltas ?? null;

  const series = (
    pick: (h: SupplierMetricsHistoryPoint) => number | null,
    transform?: (v: number) => number,
  ): MetricTrendPoint[] =>
    points.map((h) => {
      const raw = pick(h);
      return { date: h.date, value: raw != null && transform ? transform(raw) : raw };
    });

  const pct = (v: number) => v * 100;
  const score100 = (v: number) => Math.round(v * 100);

  const fmtPct = (v: number | null | undefined) =>
    v != null ? `${Math.round(v * 100)}%` : "—";
  const fmtDays = (v: number | null | undefined) =>
    v != null ? `${Number(v).toFixed(1)}${t("daySuffix")}` : "—";
  const fmtHours = (v: number | null | undefined) =>
    v != null ? `${Number(v).toFixed(1)}${t("hourSuffix")}` : "—";
  const fmtStar = (v: number | null | undefined) =>
    v != null ? Number(v).toFixed(1) : "—";

  const composite100 =
    metrics?.compositeScore != null ? Math.round(metrics.compositeScore * 100) : null;

  // KI-043 pattern: with fewer than two snapshots the trend lines can't render — show one
  // "accumulating" note instead of six empty charts. The composite header card stays visible.
  const sparse = !historyLoading && points.length < 2;

  return (
    <div style={{ maxWidth: 900, display: "flex", flexDirection: "column", gap: 8 }}>
      {/* Days selector */}
      <div style={{ display: "flex", gap: 8, marginBottom: 12 }}>
        {DAYS_OPTIONS.map((d) => (
          <button
            key={d}
            type="button"
            onClick={() => setDays(d)}
            style={{
              background: days === d ? "#1D3461" : "transparent",
              border: `1px solid ${days === d ? "#3B82F6" : "#374151"}`,
              borderRadius: 8,
              color: days === d ? "#93C5FD" : "#9CA3AF",
              fontSize: 12,
              fontWeight: 600,
              padding: "7px 12px",
              cursor: "pointer",
            }}
          >
            {t("daysOption", { n: d })}
          </button>
        ))}
      </div>

      {/* Composite score header card */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: "22px 26px",
          display: "flex",
          alignItems: "center",
          gap: 20,
          flexWrap: "wrap",
        }}
      >
        <div>
          <div style={{ color: "#4B5563", fontSize: 12, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>
            {t("compositeTitle")}
          </div>
          <div style={{ display: "flex", alignItems: "baseline", gap: 8, marginTop: 6 }}>
            <span style={{ color: "#E8EDF5", fontSize: 40, fontWeight: 800, lineHeight: 1 }}>
              {metricsLoading ? "…" : composite100 ?? "—"}
            </span>
            <span style={{ color: "#6B7280", fontSize: 14 }}>{t("outOf100")}</span>
            <DeltaBadge delta={deltas?.compositeScore ?? null} good="up" />
          </div>
        </div>
        <p style={{ color: "#6B7280", fontSize: 13, margin: 0, maxWidth: 460 }}>
          {t("compositeExplain")}
        </p>
      </div>

      {isError ? (
        <div style={{ color: "#F87171", fontSize: 13, marginTop: 16 }}>{t("errorLoad")}</div>
      ) : sparse ? (
        <div
          style={{
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 10,
            padding: "40px 20px",
            color: "#4B5563",
            fontSize: 13,
            textAlign: "center",
            marginTop: 16,
          }}
        >
          {t("sparseHistory")}
        </div>
      ) : (
        <div style={{ marginTop: 20 }}>
          <MetricRow
            title={t("compositeTitle")}
            value={composite100 != null ? `${composite100} / 100` : "—"}
            delta={deltas?.compositeScore ?? null}
            good="up"
            points={series((h) => h.compositeScore, score100)}
            unit="score"
            color="#818CF8"
          />
          <MetricRow
            title={t("rating")}
            value={fmtStar(metrics?.rating)}
            delta={deltas?.rating ?? null}
            good="up"
            points={series((h) => h.rating)}
            unit="star"
            color="#F59E0B"
          />
          <MetricRow
            title={t("onTimeDelivery")}
            value={fmtPct(metrics?.onTimeDeliveryRate)}
            delta={deltas?.onTimeDeliveryRate ?? null}
            good="up"
            points={series((h) => h.onTimeDeliveryRate, pct)}
            unit="percent"
            color="#34D399"
          />
          <MetricRow
            title={t("orderAccuracy")}
            value={fmtPct(metrics?.orderAccuracy)}
            delta={deltas?.orderAccuracy ?? null}
            good="up"
            points={series((h) => h.orderAccuracy, pct)}
            unit="percent"
            color="#34D399"
          />
          <MetricRow
            title={t("avgDeliveryDays")}
            value={fmtDays(metrics?.avgDeliveryDays)}
            delta={deltas?.avgDeliveryDays ?? null}
            good="down"
            points={series((h) => h.avgDeliveryDays)}
            unit="day"
          />
          <MetricRow
            title={t("responseTime")}
            value={fmtHours(metrics?.responseTimeHours)}
            delta={deltas?.responseTimeHours ?? null}
            good="down"
            points={series((h) => h.responseTimeHours)}
            unit="hour"
          />
        </div>
      )}

      {metrics?.updatedAt && (
        <div style={{ color: "#4B5563", fontSize: 12, marginTop: 8 }}>
          {t("updatedAt", {
            date: new Date(metrics.updatedAt).toLocaleString(intlLocale, {
              day: "2-digit",
              month: "2-digit",
              year: "numeric",
              hour: "2-digit",
              minute: "2-digit",
            }),
          })}
        </div>
      )}
    </div>
  );
}
