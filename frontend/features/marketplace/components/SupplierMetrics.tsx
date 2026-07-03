import type { SupplierMetricsDto } from "../types";
import { StarRating } from "./StarRating";

interface Props {
  metrics: SupplierMetricsDto | null;
}

interface MetricItemProps {
  label: string;
  value: React.ReactNode;
}

function MetricItem({ label, value }: MetricItemProps) {
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
    </div>
  );
}

export function SupplierMetrics({ metrics }: Props) {
  const fmt = (v: number | null | undefined, suffix = "") =>
    v != null ? `${v}${suffix}` : "—";

  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "repeat(auto-fill, minmax(150px, 1fr))",
        gap: 12,
      }}
    >
      <MetricItem
        label="Рейтинг"
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
        label="Середній термін доставки"
        value={fmt(metrics?.avgDeliveryDays, " дн.")}
      />
      <MetricItem
        label="Точність замовлень"
        value={
          metrics?.orderAccuracy != null
            ? `${(metrics.orderAccuracy * 100).toFixed(0)}%`
            : "—"
        }
      />
      <MetricItem
        label="Якість товарів"
        value={fmt(metrics?.qualityScore)}
      />
      <MetricItem
        label="Час відповіді"
        value={fmt(metrics?.responseTimeHours, " год.")}
      />
      <MetricItem
        label="Відмови"
        value={
          metrics?.cancellationRate != null
            ? `${(metrics.cancellationRate * 100).toFixed(0)}%`
            : "—"
        }
      />
    </div>
  );
}
