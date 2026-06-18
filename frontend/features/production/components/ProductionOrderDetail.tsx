"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import {
  useProductionOrder,
  useUpdateProductionOrder,
  useCompleteProductionOrder,
  useCancelProductionOrder,
} from "../hooks/useProduction";
import { OrderStatusBadge } from "./ProductionOrderTable";
import { ApiError } from "@/lib/api";

interface Props {
  id: string;
}

interface CompleteSuccess {
  outputQty: number;
  outputItemName: string;
  batchNumber: string;
}

// Shape of the 422 InsufficientStock error from the API
interface InsufficientStockError {
  itemName: string;
  required: number;
  available: number;
}

export function ProductionOrderDetail({ id }: Props) {
  const router = useRouter();
  const { data: order, isLoading, isError } = useProductionOrder(id);
  const startOrder = useUpdateProductionOrder(id);
  const completeOrder = useCompleteProductionOrder(id);
  const cancelOrder = useCancelProductionOrder(id);

  const [successInfo, setSuccessInfo] = useState<CompleteSuccess | null>(null);
  const [insufficientStock, setInsufficientStock] =
    useState<InsufficientStockError | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

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
        Не вдалося завантажити ордер.
      </div>
    );
  }

  const status = order.status;
  const isReadOnly = status === "done" || status === "cancelled";

  function clearFeedback() {
    setSuccessInfo(null);
    setInsufficientStock(null);
    setActionError(null);
  }

  function handleStart() {
    clearFeedback();
    startOrder.mutate({ status: "in_progress" });
  }

  function handleComplete() {
    clearFeedback();
    completeOrder.mutate(undefined, {
      onSuccess: (result) => {
        setSuccessInfo({
          outputQty: result.outputQty,
          outputItemName: result.outputItemName,
          // batch number not in result — use stock batch id short form
          batchNumber: result.newStockBatchId.substring(0, 8).toUpperCase(),
        });
      },
      onError: (err) => {
        if (err instanceof ApiError && err.status === 422) {
          // Try to parse structured error body: "Недостатньо {itemName}: потрібно {required}, є {available}"
          const msg = err.message;
          const match = msg.match(
            /Недостатньо (.+?):\s*потрібно ([\d.]+),\s*є ([\d.]+)/i
          );
          if (match) {
            setInsufficientStock({
              itemName: match[1],
              required: parseFloat(match[2]),
              available: parseFloat(match[3]),
            });
          } else {
            // Fallback: try to extract item name from simpler format
            setInsufficientStock({
              itemName: msg,
              required: 0,
              available: 0,
            });
          }
        } else {
          setActionError(err instanceof Error ? err.message : "Помилка завершення");
        }
      },
    });
  }

  function handleCancel() {
    if (!confirm("Скасувати цей виробничий ордер?")) return;
    clearFeedback();
    cancelOrder.mutate(undefined, {
      onError: (err) => {
        setActionError(err instanceof Error ? err.message : "Помилка скасування");
      },
    });
  }

  const anyPending =
    startOrder.isPending || completeOrder.isPending || cancelOrder.isPending;

  return (
    <div>
      {/* Back navigation */}
      <div
        style={{
          padding: "16px 32px 0",
          borderBottom: "1px solid #1F2937",
        }}
      >
        <button
          onClick={() => router.push("/production/orders")}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            background: "transparent",
            border: "none",
            color: "#6B7280",
            fontSize: 13,
            cursor: "pointer",
            padding: "6px 0",
          }}
        >
          <ArrowLeft size={14} />
          Назад до ордерів
        </button>
      </div>

      <div style={{ padding: "28px 32px", maxWidth: 900 }}>
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "flex-start",
            justifyContent: "space-between",
            marginBottom: 24,
            gap: 16,
            flexWrap: "wrap",
          }}
        >
          <div>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: 12,
                marginBottom: 8,
              }}
            >
              <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
                {order.recipeName}
              </h1>
              <OrderStatusBadge status={order.status} />
            </div>
            <div
              style={{
                color: "#9CA3AF",
                fontSize: 13,
                display: "flex",
                gap: 20,
                flexWrap: "wrap",
              }}
            >
              <span>📍 {order.locationName}</span>
              <span>📦 Плановий вихід: {order.plannedQty}</span>
              <span>📅 {formatDateTime(order.createdAt)}</span>
              {order.startedAt && (
                <span>▶ Розпочато: {formatDateTime(order.startedAt)}</span>
              )}
              {order.completedAt && (
                <span>✅ Завершено: {formatDateTime(order.completedAt)}</span>
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

          {/* Status action buttons */}
          {!isReadOnly && (
            <div style={{ display: "flex", gap: 10, flexShrink: 0 }}>
              {status === "planned" && (
                <button
                  onClick={handleStart}
                  disabled={anyPending}
                  style={btnStyle("#1E3A5F", "#60A5FA")}
                >
                  {startOrder.isPending ? "Запуск…" : "▶ Розпочати"}
                </button>
              )}

              {status === "in_progress" && (
                <button
                  onClick={handleComplete}
                  disabled={anyPending}
                  style={btnStyle("#064E3B", "#34D399")}
                >
                  {completeOrder.isPending ? "Завершення…" : "✓ Завершити виробництво"}
                </button>
              )}

              {(status === "planned" || status === "in_progress") && (
                <button
                  onClick={handleCancel}
                  disabled={anyPending}
                  style={btnStyle("#1F1010", "#F87171")}
                >
                  {cancelOrder.isPending ? "Скасування…" : "✕ Скасувати"}
                </button>
              )}
            </div>
          )}
        </div>

        {/* Success toast */}
        {successInfo && (
          <div
            style={{
              marginBottom: 20,
              padding: "14px 18px",
              background: "#064E3B",
              border: "1px solid #065F46",
              borderRadius: 10,
              color: "#34D399",
              fontSize: 13,
            }}
          >
            <div style={{ fontWeight: 700, marginBottom: 4 }}>
              ✓ Виробництво завершено
            </div>
            <div>
              Готово: {successInfo.outputQty} — «{successInfo.outputItemName}»
            </div>
            <div style={{ color: "#6EE7B7", fontSize: 12, marginTop: 4 }}>
              Партія {successInfo.batchNumber} додана до залишків
            </div>
          </div>
        )}

        {/* 422 Insufficient stock error */}
        {insufficientStock && (
          <div
            style={{
              marginBottom: 20,
              padding: "14px 18px",
              background: "#1F1010",
              border: "1px solid #7F1D1D",
              borderRadius: 10,
              color: "#F87171",
              fontSize: 13,
            }}
          >
            <div style={{ fontWeight: 700, marginBottom: 4 }}>
              Недостатньо запасів
            </div>
            {insufficientStock.required > 0 ? (
              <div>
                {insufficientStock.itemName} — потрібно{" "}
                {insufficientStock.required}, є {insufficientStock.available}
              </div>
            ) : (
              <div>{insufficientStock.itemName}</div>
            )}
          </div>
        )}

        {/* General action error */}
        {actionError && (
          <div
            style={{
              marginBottom: 20,
              padding: "14px 18px",
              background: "#1F1010",
              border: "1px solid #7F1D1D",
              borderRadius: 10,
              color: "#F87171",
              fontSize: 13,
            }}
          >
            <strong>Помилка:</strong> {actionError}
          </div>
        )}

        {/* Consumptions table (visible after completion) */}
        {order.consumptions.length > 0 && (
          <div
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              overflow: "hidden",
            }}
          >
            <div
              style={{
                padding: "14px 16px",
                borderBottom: "1px solid #1F2937",
              }}
            >
              <h2 style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, margin: 0 }}>
                Списані інгредієнти
              </h2>
            </div>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  {["Товар", "Кількість", "Партія", "Час списання"].map((h) => (
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
                      }}
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {order.consumptions.map((c, idx) => (
                  <tr key={idx} style={{ borderBottom: "1px solid #1F2937" }}>
                    <td style={tdStyle}>
                      <span style={{ color: "#E8EDF5", fontSize: 13 }}>
                        {c.itemName}
                      </span>
                    </td>
                    <td style={tdStyle}>
                      <span style={{ color: "#9CA3AF", fontSize: 13 }}>
                        {c.qtyConsumed}
                      </span>
                    </td>
                    <td style={tdStyle}>
                      <span
                        style={{
                          color: "#6B7280",
                          fontSize: 12,
                          fontFamily: "monospace",
                        }}
                      >
                        {c.batchNumber ?? "—"}
                      </span>
                    </td>
                    <td style={tdStyle}>
                      <span style={{ color: "#6B7280", fontSize: 12 }}>
                        {formatDateTime(c.consumedAt)}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Empty state for pending orders */}
        {order.consumptions.length === 0 && !isReadOnly && (
          <div
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              padding: "32px 16px",
              textAlign: "center",
              color: "#4B5563",
              fontSize: 13,
            }}
          >
            Списання з'являться після завершення виробництва.
          </div>
        )}
      </div>
    </div>
  );
}

// ── Helpers ────────────────────────────────────────────────────────────────────

function formatDateTime(iso: string) {
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function btnStyle(bg: string, color: string): React.CSSProperties {
  return {
    padding: "9px 18px",
    borderRadius: 8,
    border: "none",
    background: bg,
    color,
    fontSize: 13,
    fontWeight: 600,
    cursor: "pointer",
    whiteSpace: "nowrap",
  };
}

const tdStyle: React.CSSProperties = {
  padding: "12px 16px",
  verticalAlign: "middle",
};
