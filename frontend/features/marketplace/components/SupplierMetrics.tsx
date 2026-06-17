import type { SupplierProfileDto } from "../types";
import { StarRating } from "./StarRating";

interface Props {
  supplier: SupplierProfileDto;
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

export function SupplierMetrics({ supplier }: Props) {
  const fmt = (v: number | null, suffix = "") =>
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
          supplier.rating != null ? (
            <span style={{ display: "flex", alignItems: "center", gap: 6 }}>
              {supplier.rating.toFixed(1)}
              <StarRating value={supplier.rating} size={14} />
            </span>
          ) : (
            "—"
          )
        }
      />
      <MetricItem
        label="Середній термін доставки"
        value={fmt(supplier.avgDeliveryDays, " дн.")}
      />
      <MetricItem
        label="Точність замовлень"
        value={
          supplier.orderAccuracy != null
            ? `${(supplier.orderAccuracy * 100).toFixed(0)}%`
            : "—"
        }
      />
      <MetricItem
        label="Якість товарів"
        value={fmt(supplier.qualityScore)}
      />
      <MetricItem
        label="Час відповіді"
        value={fmt(supplier.responseTimeHours, " год.")}
      />
      <MetricItem
        label="Відмови"
        value={
          supplier.cancellationRate != null
            ? `${(supplier.cancellationRate * 100).toFixed(0)}%`
            : "—"
        }
      />
    </div>
  );
}
