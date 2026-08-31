"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import type { SupplierMetricsDto } from "../types";
import { StarRating } from "./StarRating";
import { DeliveryByRegionPanel } from "./DeliveryByRegionPanel";

interface Props {
  metrics: SupplierMetricsDto | null;
}

interface MetricItemProps {
  label: string;
  value: React.ReactNode;
  sublabel?: React.ReactNode;
  footer?: React.ReactNode;
}

function MetricItem({ label, value, sublabel, footer }: MetricItemProps) {
  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "16px 20px",
        display: "flex",
        flexDirection: "column",
        gap: 6,
      }}
    >
      <div style={{ color: "#4B5563", fontSize: 12 }}>{label}</div>
      <div style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700 }}>{value}</div>
      {sublabel != null && (
        <div style={{ color: "#4B5563", fontSize: 11 }}>{sublabel}</div>
      )}
      {footer}
    </div>
  );
}

export function SupplierMetrics({ metrics }: Props) {
  const t = useTranslations("Dashboard.marketplace.metrics");
  const [regionsOpen, setRegionsOpen] = useState(false);

  const fmt = (v: number | null | undefined, suffix = "") =>
    v != null ? `${v}${suffix}` : "—";

  const deliveryByRegion = metrics?.deliveryByRegion ?? null;
  const hasRegionBreakdown = (deliveryByRegion?.length ?? 0) > 0;
  const showRegionToggle =
    metrics != null && (hasRegionBreakdown || metrics.deliverySampleSize != null);

  return (
    <div>
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fill, minmax(150px, 1fr))",
          gap: 12,
        }}
      >
        <MetricItem
          label={t("rating")}
          value={
            metrics?.rating != null ? (
              <span style={{ display: "flex", alignItems: "center", gap: 6 }}>
                {Number(metrics.rating).toFixed(1)}
                <StarRating value={metrics.rating} size={14} />
              </span>
            ) : (
              "—"
            )
          }
        />
        <MetricItem
          label={t("avgDeliveryDays")}
          value={fmt(metrics?.avgDeliveryDays, t("daySuffix"))}
          sublabel={
            metrics?.deliverySampleSize != null
              ? t("basedOnOrders", { n: metrics.deliverySampleSize })
              : undefined
          }
          footer={
            showRegionToggle ? (
              <button
                type="button"
                onClick={() => setRegionsOpen((v) => !v)}
                style={{
                  background: "transparent",
                  border: "none",
                  padding: 0,
                  color: "#3B82F6",
                  fontSize: 11,
                  cursor: "pointer",
                  textAlign: "left",
                }}
              >
                {regionsOpen ? t("regionsToggleHide") : t("regionsToggleShow")}
              </button>
            ) : undefined
          }
        />
        <MetricItem
          label={t("orderAccuracy")}
          value={
            metrics?.orderAccuracy != null
              ? `${(metrics.orderAccuracy * 100).toFixed(0)}%`
              : "—"
          }
        />
        <MetricItem label={t("qualityScore")} value={fmt(metrics?.qualityScore)} />
        <MetricItem
          label={t("responseTime")}
          value={
            metrics?.responseTimeHours != null
              ? `${metrics.responseTimeHours}${t("hourSuffix")}`
              : t("responseTimeInsufficient")
          }
          sublabel={
            metrics?.responseSampleSize != null
              ? t("basedOnInquiries", { n: metrics.responseSampleSize })
              : undefined
          }
        />
        <MetricItem
          label={t("cancellationRate")}
          value={
            metrics?.cancellationRate != null
              ? `${(metrics.cancellationRate * 100).toFixed(0)}%`
              : "—"
          }
        />
      </div>

      {regionsOpen && showRegionToggle && (
        <DeliveryByRegionPanel stats={deliveryByRegion} />
      )}
    </div>
  );
}
