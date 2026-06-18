"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { useProductionOrders } from "../hooks/useProduction";
import { ProductionOrderForm } from "./ProductionOrderForm";
import type { ProductionOrderStatus } from "../types";

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: "", label: "Всі статуси" },
  { value: "planned", label: "Заплановані" },
  { value: "in_progress", label: "В роботі" },
  { value: "done", label: "Завершені" },
  { value: "cancelled", label: "Скасовані" },
];

export function ProductionOrderTable() {
  const router = useRouter();
  const [statusFilter, setStatusFilter] = useState("");
  const [createOpen, setCreateOpen] = useState(false);

  const { data: orders = [], isLoading, isError } = useProductionOrders(
    statusFilter || undefined
  );

  if (isLoading) {
    return (
      <div style={{ padding: "48px 32px", color: "#6B7280", fontSize: 14 }}>
        Завантаження ордерів…
      </div>
    );
  }
  if (isError) {
    return (
      <div style={{ padding: "48px 32px", color: "#F87171", fontSize: 14 }}>
        Не вдалося завантажити ордери.
      </div>
    );
  }

  return (
    <div>
      {/* Toolbar */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          marginBottom: 20,
          gap: 16,
          flexWrap: "wrap",
        }}
      >
        <h1 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>
          Виробничі ордери
        </h1>
        <div style={{ display: "flex", gap: 10, alignItems: "center" }}>
          {/* Status filter */}
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            style={{
              padding: "7px 12px",
              background: "#0D1117",
              border: "1px solid #374151",
              borderRadius: 8,
              color: "#9CA3AF",
              fontSize: 13,
              cursor: "pointer",
            }}
          >
            {STATUS_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>

          <button
            onClick={() => setCreateOpen(true)}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 6,
              padding: "8px 16px",
              borderRadius: 8,
              border: "none",
              background: "#3B82F6",
              color: "#fff",
              fontSize: 13,
              fontWeight: 600,
              cursor: "pointer",
            }}
          >
            <Plus size={15} />
            Ордер
          </button>
        </div>
      </div>

      {/* Table */}
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 10,
          overflow: "hidden",
        }}
      >
        {orders.length === 0 ? (
          <div
            style={{
              padding: "48px 32px",
              textAlign: "center",
              color: "#4B5563",
              fontSize: 14,
            }}
          >
            Ордерів поки немає. Створіть перший виробничий ордер.
          </div>
        ) : (
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                {["Рецепт", "Локація", "Плановий вихід", "Статус", "Створено", "Завершено"].map(
                  (h) => (
                    <th
                      key={h}
                      style={{
                        padding: "10px 16px",
                        textAlign: "left",
                        color: "#4B5563",
                        fontSize: 11,
                        fontWeight: 600,
                        textTransform: "uppercase",
                        letterSpacing: "0.04em",
                        borderBottom: "1px solid #1F2937",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {h}
                    </th>
                  )
                )}
              </tr>
            </thead>
            <tbody>
              {orders.map((order) => (
                <tr
                  key={order.id}
                  onClick={() =>
                    router.push(`/production/orders/${order.id}`)
                  }
                  style={{
                    borderBottom: "1px solid #1F2937",
                    cursor: "pointer",
                  }}
                  onMouseEnter={(e) => {
                    (e.currentTarget as HTMLElement).style.background =
                      "#1a2030";
                  }}
                  onMouseLeave={(e) => {
                    (e.currentTarget as HTMLElement).style.background =
                      "transparent";
                  }}
                >
                  <td style={tdStyle}>
                    <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
                      {order.recipeName}
                    </span>
                  </td>
                  <td style={tdStyle}>
                    <span style={{ color: "#9CA3AF", fontSize: 13 }}>
                      {order.locationName}
                    </span>
                  </td>
                  <td style={tdStyle}>
                    <span style={{ color: "#9CA3AF", fontSize: 13 }}>
                      {order.plannedQty}
                    </span>
                  </td>
                  <td style={tdStyle}>
                    <OrderStatusBadge status={order.status} />
                  </td>
                  <td style={tdStyle}>
                    <span style={{ color: "#6B7280", fontSize: 12 }}>
                      {formatDate(order.createdAt)}
                    </span>
                  </td>
                  <td style={tdStyle}>
                    <span style={{ color: "#6B7280", fontSize: 12 }}>
                      {order.completedAt ? formatDate(order.completedAt) : "—"}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {createOpen && <ProductionOrderForm onClose={() => setCreateOpen(false)} />}
    </div>
  );
}

// ── Helpers ────────────────────────────────────────────────────────────────────

export function OrderStatusBadge({ status }: { status: ProductionOrderStatus }) {
  const cfg: Record<ProductionOrderStatus, { bg: string; color: string; label: string }> = {
    planned: { bg: "#1F2937", color: "#9CA3AF", label: "Заплановано" },
    in_progress: { bg: "#1E3A5F", color: "#60A5FA", label: "В роботі" },
    done: { bg: "#064E3B", color: "#34D399", label: "Готово" },
    cancelled: { bg: "#1F1010", color: "#F87171", label: "Скасовано" },
  };
  const { bg, color, label } = cfg[status] ?? cfg.planned;
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 10px",
        borderRadius: 12,
        fontSize: 12,
        fontWeight: 600,
        background: bg,
        color,
      }}
    >
      {label}
    </span>
  );
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

const tdStyle: React.CSSProperties = {
  padding: "12px 16px",
  verticalAlign: "middle",
};
