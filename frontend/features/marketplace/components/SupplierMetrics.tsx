"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import type { SupplierMetricsDto } from "../types";
import { StarRating } from "./StarRating";

interface Props {
  metrics: SupplierMetricsDto | null;
  supplierId: string;
}

interface MetricItemProps {
  href: string;
  label: string;
  value: React.ReactNode;
  sublabel?: React.ReactNode;
}

function MetricItem({ href, label, value, sublabel }: MetricItemProps) {
  return (
    <Link href={href} style={{ textDecoration: "none" }}>
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 10,
          padding: "16px 20px",
          display: "flex",
          flexDirection: "column",
          gap: 6,
          cursor: "pointer",
          transition: "border-color 0.15s",
          height: "100%",
        }}
        onMouseEnter={(e) => {
          (e.currentTarget as HTMLElement).style.borderColor = "#3B82F6";
        }}
        onMouseLeave={(e) => {
          (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
        }}
      >
        <div style={{ color: "#4B5563", fontSize: 12 }}>{label}</div>
        <div style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700 }}>{value}</div>
        {sublabel != null && (
          <div style={{ color: "#4B5563", fontSize: 11 }}>{sublabel}</div>
        )}
      </div>
    </Link>
  );
}

/**
 * Supplier performance tiles on the profile page. Each tile deep-links to the
 * matching section of the metrics detail page (TASK-672); the per-region delivery
 * drill-down and its trend charts now live there, not inline here.
 */
export function SupplierMetrics({ metrics, supplierId }: Props) {
  const t = useTranslations("Dashboard.marketplace.metrics");
  const tPage = useTranslations("Dashboard.marketplace.metricsPage");

  const fmt = (v: number | null | undefined, suffix = "") =>
    v != null ? `${v}${suffix}` : "—";

  const base = `/marketplace/${supplierId}/metrics`;

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
          href={`${base}#rating`}
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
          href={`${base}#delivery`}
          label={t("avgDeliveryDays")}
          value={fmt(metrics?.avgDeliveryDays, t("daySuffix"))}
          sublabel={
            metrics?.deliverySampleSize != null
              ? t("basedOnOrders", { n: metrics.deliverySampleSize })
              : undefined
          }
        />
        <MetricItem
          href={`${base}#accuracy`}
          label={t("orderAccuracy")}
          value={
            metrics?.orderAccuracy != null
              ? `${(metrics.orderAccuracy * 100).toFixed(0)}%`
              : "—"
          }
        />
        <MetricItem
          href={`${base}#quality`}
          label={t("qualityScore")}
          value={fmt(metrics?.qualityScore)}
        />
        <MetricItem
          href={`${base}#response`}
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
          href={`${base}#cancellation`}
          label={t("cancellationRate")}
          value={
            metrics?.cancellationRate != null
              ? `${(metrics.cancellationRate * 100).toFixed(0)}%`
              : "—"
          }
        />
      </div>

      <Link
        href={base}
        style={{
          display: "inline-block",
          marginTop: 12,
          color: "#3B82F6",
          fontSize: 12,
          textDecoration: "none",
        }}
      >
        {tPage("detailsLink")}
      </Link>
    </div>
  );
}
