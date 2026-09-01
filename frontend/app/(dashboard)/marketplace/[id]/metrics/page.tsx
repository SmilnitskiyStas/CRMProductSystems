"use client";

import { useEffect } from "react";
import Link from "next/link";
import { ChevronLeft } from "lucide-react";
import { useParams } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import {
  useSupplier,
  useSupplierMetricsHistory,
} from "@/features/marketplace/hooks/useMarketplace";
import {
  SupplierMetricTrendChart,
  type MetricTrendPoint,
  type MetricTrendUnit,
} from "@/features/marketplace/components/SupplierMetricTrendChart";
import { DeliveryByRegionPanel } from "@/features/marketplace/components/DeliveryByRegionPanel";
import { DeliveryRegionComparison } from "@/features/marketplace/components/DeliveryRegionComparison";
import { SupplierCoveragePanel } from "@/features/marketplace/components/SupplierCoveragePanel";
import { StarRating } from "@/features/marketplace/components/StarRating";
import type { SupplierMetricsHistoryPoint } from "@/features/marketplace/types";

interface SectionProps {
  id: string;
  title: string;
  value: React.ReactNode;
  sampleNote?: string;
  explanation: string;
  children: React.ReactNode;
}

function MetricSection({ id, title, value, sampleNote, explanation, children }: SectionProps) {
  return (
    <section id={id} style={{ marginBottom: 40, scrollMarginTop: 20 }}>
      <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, margin: "0 0 8px" }}>
        {title}
      </h2>
      <div style={{ color: "#E8EDF5", fontSize: 24, fontWeight: 700, lineHeight: 1.2 }}>
        {value}
      </div>
      {sampleNote && (
        <div style={{ color: "#4B5563", fontSize: 12, marginTop: 4 }}>{sampleNote}</div>
      )}
      <p style={{ color: "#6B7280", fontSize: 13, margin: "10px 0 14px", maxWidth: 640 }}>
        {explanation}
      </p>
      {children}
    </section>
  );
}

export default function SupplierMetricsDetailPage() {
  const t = useTranslations("Dashboard.marketplace.metricsPage");
  const tMetrics = useTranslations("Dashboard.marketplace.metrics");
  const tSupplierPage = useTranslations("Dashboard.marketplace.supplierPage");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { id } = useParams<{ id: string }>();

  const { data: supplier, isLoading, isError } = useSupplier(id);
  const { data: history = [] } = useSupplierMetricsHistory(id, 90);

  // The hash target element only exists after the supplier query resolves, so the
  // browser's own on-load hash scroll can miss it — re-run it once content is in.
  useEffect(() => {
    if (!supplier) return;
    const hash = window.location.hash.slice(1);
    if (!hash) return;
    const el = document.getElementById(hash);
    if (el) el.scrollIntoView({ behavior: "smooth", block: "start" });
  }, [supplier]);

  if (isLoading) {
    return (
      <div style={{ padding: "28px 32px" }}>
        <div style={{ height: 40, background: "#111827", borderRadius: 8, marginBottom: 20, maxWidth: 320 }} />
        {[...Array(3)].map((_, i) => (
          <div
            key={i}
            style={{ height: 260, background: "#111827", borderRadius: 10, marginBottom: 24 }}
          />
        ))}
      </div>
    );
  }

  if (isError || !supplier) {
    return (
      <div style={{ padding: "28px 32px" }}>
        <div style={{ color: "#F87171", fontSize: 14 }}>{tSupplierPage("errorLoad")}</div>
        <Link
          href="/marketplace"
          style={{
            color: "#3B82F6",
            fontSize: 13,
            display: "inline-flex",
            alignItems: "center",
            gap: 4,
            marginTop: 16,
          }}
        >
          <ChevronLeft size={14} /> {tSupplierPage("backToMarketplace")}
        </Link>
      </div>
    );
  }

  const metrics = supplier.metrics;
  const computedAt = metrics?.aggregatesComputedAt ?? null;
  const served = supplier.deliveryCoverage?.served ?? [];

  const series = (
    pick: (h: SupplierMetricsHistoryPoint) => number | null,
    transform?: (v: number) => number,
  ): MetricTrendPoint[] =>
    history.map((h) => {
      const raw = pick(h);
      return {
        date: h.date,
        value: raw != null && transform ? transform(raw) : raw,
      };
    });

  const pct = (v: number) => v * 100;

  const chart = (
    points: MetricTrendPoint[],
    unit: MetricTrendUnit,
    label: string,
    color?: string,
  ) => <SupplierMetricTrendChart points={points} unit={unit} label={label} color={color} />;

  return (
    <div style={{ padding: "28px 32px", maxWidth: 900 }}>
      {/* Back link */}
      <Link
        href={`/marketplace/${id}`}
        style={{
          display: "inline-flex",
          alignItems: "center",
          gap: 4,
          color: "#4B5563",
          fontSize: 13,
          textDecoration: "none",
          marginBottom: 16,
        }}
      >
        <ChevronLeft size={14} />
        {t("backToProfile")}
      </Link>

      {/* Header */}
      <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: "0 0 6px" }}>
        {t("title")}
      </h1>
      <div style={{ color: "#9CA3AF", fontSize: 14, marginBottom: 4 }}>
        {supplier.supplierName}
      </div>
      <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 28 }}>
        {computedAt
          ? t("updatedAt", {
              date: new Date(computedAt).toLocaleString(intlLocale, {
                day: "2-digit",
                month: "2-digit",
                year: "numeric",
                hour: "2-digit",
                minute: "2-digit",
              }),
            })
          : t("notComputed")}
      </div>

      {/* Rating */}
      <MetricSection
        id="rating"
        title={tMetrics("rating")}
        explanation={t("explainRating")}
        value={
          metrics?.rating != null ? (
            <span style={{ display: "inline-flex", alignItems: "center", gap: 8 }}>
              {Number(metrics.rating).toFixed(1)}
              <StarRating value={metrics.rating} size={16} />
            </span>
          ) : (
            "—"
          )
        }
      >
        {chart(series((h) => h.rating), "star", tMetrics("rating"), "#F59E0B")}
      </MetricSection>

      {/* Average delivery time */}
      <MetricSection
        id="delivery"
        title={tMetrics("avgDeliveryDays")}
        explanation={t("explainDelivery")}
        value={
          metrics?.avgDeliveryDays != null
            ? `${metrics.avgDeliveryDays}${tMetrics("daySuffix")}`
            : "—"
        }
        sampleNote={
          metrics?.deliverySampleSize != null
            ? tMetrics("basedOnOrders", { n: metrics.deliverySampleSize })
            : undefined
        }
      >
        {chart(series((h) => h.avgDeliveryDays), "day", tMetrics("avgDeliveryDays"))}
        <h3 style={{ color: "#9CA3AF", fontSize: 13, fontWeight: 600, margin: "20px 0 0" }}>
          {t("byRegionTitle")}
        </h3>
        {served.length > 0 ? (
          <DeliveryRegionComparison stats={metrics?.deliveryByRegion} served={served} />
        ) : (
          <DeliveryByRegionPanel stats={metrics?.deliveryByRegion} />
        )}
      </MetricSection>

      {/* Order accuracy */}
      <MetricSection
        id="accuracy"
        title={tMetrics("orderAccuracy")}
        explanation={t("explainAccuracy")}
        value={
          metrics?.orderAccuracy != null
            ? `${(metrics.orderAccuracy * 100).toFixed(0)}%`
            : "—"
        }
      >
        {chart(series((h) => h.orderAccuracy, pct), "percent", tMetrics("orderAccuracy"), "#34D399")}
      </MetricSection>

      {/* Product quality */}
      <MetricSection
        id="quality"
        title={tMetrics("qualityScore")}
        explanation={t("explainQuality")}
        value={metrics?.qualityScore != null ? String(metrics.qualityScore) : "—"}
      >
        {chart(series((h) => h.qualityScore), "score", tMetrics("qualityScore"), "#A78BFA")}
      </MetricSection>

      {/* Response time */}
      <MetricSection
        id="response"
        title={tMetrics("responseTime")}
        explanation={t("explainResponse")}
        value={
          metrics?.responseTimeHours != null
            ? `${metrics.responseTimeHours}${tMetrics("hourSuffix")}`
            : tMetrics("responseTimeInsufficient")
        }
        sampleNote={
          metrics?.responseSampleSize != null
            ? tMetrics("basedOnInquiries", { n: metrics.responseSampleSize })
            : undefined
        }
      >
        {chart(series((h) => h.responseTimeHours), "hour", tMetrics("responseTime"))}
      </MetricSection>

      {/* Cancellations */}
      <MetricSection
        id="cancellation"
        title={tMetrics("cancellationRate")}
        explanation={t("explainCancellation")}
        value={
          metrics?.cancellationRate != null
            ? `${(metrics.cancellationRate * 100).toFixed(0)}%`
            : "—"
        }
      >
        {chart(
          series((h) => h.cancellationRate, pct),
          "percent",
          tMetrics("cancellationRate"),
          "#F87171",
        )}
      </MetricSection>

      {/* Delivery coverage */}
      <section id="coverage" style={{ scrollMarginTop: 20 }}>
        <p style={{ color: "#6B7280", fontSize: 13, margin: "0 0 14px", maxWidth: 640 }}>
          {t("explainCoverage")}
        </p>
        <SupplierCoveragePanel coverage={supplier.deliveryCoverage} />
      </section>
    </div>
  );
}
