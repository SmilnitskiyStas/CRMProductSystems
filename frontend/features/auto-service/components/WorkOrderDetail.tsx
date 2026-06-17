"use client";

import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { useWorkOrder, useCompleteWorkOrder, useRemoveWorkOrderLine } from "../hooks/useAutoService";
import { StatusBadge, STATUS_LABELS } from "./WorkOrderCard";
import { WorkOrderLineForm } from "./WorkOrderLineForm";
import type { WorkOrderStatus } from "../types";

const EDITABLE_STATUSES: WorkOrderStatus[] = ["new", "in_progress", "waiting_parts"];
const COMPLETABLE_STATUSES: WorkOrderStatus[] = ["new", "in_progress", "waiting_parts"];

interface Props {
  id: string;
}

export function WorkOrderDetail({ id }: Props) {
  const { data: order, isLoading, isError } = useWorkOrder(id);
  const completeWorkOrder = useCompleteWorkOrder(id);
  const removeLineQuery = useRemoveWorkOrderLine(id);
  const [showAddLine, setShowAddLine] = useState(false);
  const [completeError, setCompleteError] = useState<string | null>(null);
  const [completedMsg, setCompletedMsg] = useState(false);

  if (isLoading) {
    return (
      <div style={{ padding: "48px 32px", color: "#6B7280", fontSize: 14 }}>
        Завантаження…
      </div>
    );
  }
  if (isError || !order) {
    return (
      <div style={{ padding: "48px 32px", color: "#F87171", fontSize: 14 }}>
        Не вдалося завантажити наряд.
      </div>
    );
  }

  const canEdit = EDITABLE_STATUSES.includes(order.status);
  const canComplete = COMPLETABLE_STATUSES.includes(order.status);

  function handleComplete() {
    setCompleteError(null);
    setCompletedMsg(false);
    completeWorkOrder.mutate(undefined, {
      onSuccess: () => setCompletedMsg(true),
      onError: (err) => {
        const msg = err instanceof Error ? err.message : "Помилка";
        setCompleteError(msg);
      },
    });
  }

  const createdDate = new Date(order.createdAt).toLocaleDateString("uk-UA", {
    day: "2-digit",
    month: "long",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });

  return (
    <div style={{ padding: "28px 32px", maxWidth: 900 }}>
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
          marginBottom: 28,
          gap: 16,
        }}
      >
        <div>
          <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 6 }}>
            <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
              {order.vehicleBrand} {order.vehicleModel}
            </h1>
            <StatusBadge status={order.status} />
          </div>
          <div style={{ color: "#9CA3AF", fontSize: 13, display: "flex", gap: 16, flexWrap: "wrap" }}>
            <span>🚗 {order.licensePlate}</span>
            {order.mechanicName && <span>👤 {order.mechanicName}</span>}
            <span>📅 {createdDate}</span>
            {order.completedAt && (
              <span>
                ✅ Завершено:{" "}
                {new Date(order.completedAt).toLocaleDateString("uk-UA", {
                  day: "2-digit",
                  month: "long",
                  year: "numeric",
                })}
              </span>
            )}
          </div>
          {order.notes && (
            <div
              style={{
                marginTop: 10,
                padding: "8px 12px",
                background: "#0D1117",
                border: "1px solid #1F2937",
                borderRadius: 6,
                color: "#9CA3AF",
                fontSize: 13,
              }}
            >
              {order.notes}
            </div>
          )}
        </div>

        {canComplete && (
          <button
            onClick={handleComplete}
            disabled={completeWorkOrder.isPending}
            style={{
              padding: "9px 20px",
              borderRadius: 8,
              border: "none",
              background: "#065F46",
              color: "#34D399",
              fontSize: 13,
              fontWeight: 600,
              cursor: "pointer",
              flexShrink: 0,
            }}
          >
            {completeWorkOrder.isPending ? "Завершення…" : "✓ Завершити"}
          </button>
        )}
      </div>

      {/* Complete feedback */}
      {completedMsg && (
        <div style={{ marginBottom: 16, padding: "10px 14px", background: "#064E3B", borderRadius: 8, color: "#34D399", fontSize: 13 }}>
          Наряд успішно завершено.
        </div>
      )}
      {completeError && (
        <div style={{ marginBottom: 16, padding: "10px 14px", background: "#1F1010", borderRadius: 8, color: "#F87171", fontSize: 13 }}>
          <strong>Помилка завершення:</strong> {completeError}
          {completeError.includes("422") || completeError.toLowerCase().includes("stock") ? (
            <div style={{ marginTop: 6, color: "#FCA5A5" }}>
              Недостатньо запчастин на складі. Перевірте залишки або замініть деталі.
            </div>
          ) : null}
        </div>
      )}

      {/* Lines table */}
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 10,
          overflow: "hidden",
        }}
      >
        {/* Table header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "14px 16px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          <h2 style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, margin: 0 }}>
            Рядки наряду
          </h2>
          {canEdit && (
            <button
              onClick={() => setShowAddLine(true)}
              style={{
                display: "flex",
                alignItems: "center",
                gap: 6,
                padding: "6px 12px",
                borderRadius: 6,
                border: "1px solid #1F2937",
                background: "transparent",
                color: "#9CA3AF",
                fontSize: 12,
                cursor: "pointer",
              }}
            >
              <Plus size={14} />
              Додати рядок
            </button>
          )}
        </div>

        {/* Lines */}
        {order.lines.length === 0 ? (
          <div style={{ padding: "32px 16px", textAlign: "center", color: "#4B5563", fontSize: 13 }}>
            Рядки відсутні. Додайте послугу або запчастину.
          </div>
        ) : (
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                {["Тип", "Назва", "Кількість", "Ціна", "Знижка", "Разом", ""].map((h) => (
                  <th
                    key={h}
                    style={{
                      padding: "10px 14px",
                      textAlign: "left",
                      color: "#4B5563",
                      fontSize: 11,
                      fontWeight: 600,
                      textTransform: "uppercase",
                      borderBottom: "1px solid #1F2937",
                    }}
                  >
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {order.lines.map((line) => (
                <tr key={line.id} style={{ borderBottom: "1px solid #1F2937" }}>
                  <td style={tdStyle}>
                    <span style={{ fontSize: 16 }}>
                      {line.type === "service" ? "🔧" : "⚙️"}
                    </span>
                  </td>
                  <td style={tdStyle}>
                    <span style={{ color: "#E8EDF5", fontSize: 13 }}>{line.name}</span>
                  </td>
                  <td style={tdStyle}>
                    <span style={{ color: "#9CA3AF", fontSize: 13 }}>{line.qty}</span>
                  </td>
                  <td style={tdStyle}>
                    <span style={{ color: "#9CA3AF", fontSize: 13 }}>
                      {line.unitPrice.toFixed(2)} грн
                    </span>
                  </td>
                  <td style={tdStyle}>
                    <span style={{ color: line.discount > 0 ? "#FBBF24" : "#374151", fontSize: 13 }}>
                      {line.discount > 0 ? `${line.discount}%` : "—"}
                    </span>
                  </td>
                  <td style={tdStyle}>
                    <span style={{ color: "#E8EDF5", fontWeight: 700, fontSize: 13 }}>
                      {line.totalPrice.toLocaleString("uk-UA", {
                        style: "currency",
                        currency: "UAH",
                      })}
                    </span>
                  </td>
                  <td style={{ ...tdStyle, textAlign: "right" }}>
                    {canEdit && (
                      <button
                        onClick={() => removeLineQuery.mutate(line.id)}
                        disabled={removeLineQuery.isPending}
                        style={{
                          background: "transparent",
                          border: "none",
                          color: "#6B7280",
                          cursor: "pointer",
                          padding: "4px 8px",
                        }}
                        title="Видалити рядок"
                      >
                        <Trash2 size={14} />
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {/* Total */}
        <div
          style={{
            display: "flex",
            justifyContent: "flex-end",
            padding: "14px 16px",
            borderTop: "1px solid #1F2937",
            gap: 12,
            alignItems: "center",
          }}
        >
          <span style={{ color: "#9CA3AF", fontSize: 13 }}>Загальна сума:</span>
          <span style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700 }}>
            {order.totalAmount.toLocaleString("uk-UA", {
              style: "currency",
              currency: "UAH",
            })}
          </span>
        </div>
      </div>

      {/* Add line modal */}
      {showAddLine && (
        <WorkOrderLineForm workOrderId={id} onClose={() => setShowAddLine(false)} />
      )}
    </div>
  );
}

const tdStyle: React.CSSProperties = {
  padding: "12px 14px",
  verticalAlign: "middle",
};
