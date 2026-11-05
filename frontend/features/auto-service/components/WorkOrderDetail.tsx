"use client";

import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { useWorkOrder, useCompleteWorkOrder, useRemoveWorkOrderLine } from "../hooks/useAutoService";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import { StatusBadge } from "./WorkOrderCard";
import { WorkOrderLineForm } from "./WorkOrderLineForm";
import type { WorkOrderStatus } from "../types";

const EDITABLE_STATUSES: WorkOrderStatus[] = ["new", "in_progress", "waiting_parts"];
const COMPLETABLE_STATUSES: WorkOrderStatus[] = ["new", "in_progress", "waiting_parts"];

interface Props {
  id: string;
}

export function WorkOrderDetail({ id }: Props) {
  const t = useTranslations("Dashboard.autoService.detail");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: order, isLoading, isError } = useWorkOrder(id);
  const completeWorkOrder = useCompleteWorkOrder(id);
  const removeLineQuery = useRemoveWorkOrderLine(id);
  const [showAddLine, setShowAddLine] = useState(false);
  const [completeError, setCompleteError] = useState<string | null>(null);
  const [completedMsg, setCompletedMsg] = useState(false);

  if (isLoading) {
    return (
      <div style={{ padding: "48px 32px", color: "#6B7280", fontSize: 14 }}>
        {tCommon("loading")}
      </div>
    );
  }
  if (isError || !order) {
    return (
      <div style={{ padding: "48px 32px", color: "#F87171", fontSize: 14 }}>
        {t("loadError")}
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
        const msg = err instanceof Error ? err.message : t("errorGeneric");
        setCompleteError(msg);
      },
    });
  }

  const lineColumns: TableColumn<(typeof order.lines)[number]>[] = [
    {
      key: "type",
      header: t("headerType"),
      render: (line) => <span style={{ fontSize: 16 }}>{line.type === "service" ? "🔧" : "⚙️"}</span>,
    },
    {
      key: "name",
      header: t("headerName"),
      render: (line) => <span style={{ color: "#E8EDF5", fontSize: 13 }}>{line.name}</span>,
    },
    { key: "qty", header: t("headerQty"), render: (line) => line.qty },
    {
      key: "price",
      header: t("headerPrice"),
      render: (line) => `${line.unitPrice.toFixed(2)} ${t("priceUnit")}`,
    },
    {
      key: "discount",
      header: t("headerDiscount"),
      render: (line) => (
        <span style={{ color: line.discount > 0 ? "#FBBF24" : "#374151" }}>
          {line.discount > 0 ? `${line.discount}%` : "—"}
        </span>
      ),
    },
    {
      key: "total",
      header: t("headerTotal"),
      render: (line) => (
        <span style={{ color: "#E8EDF5", fontWeight: 700 }}>
          {line.totalPrice.toLocaleString(intlLocale, { style: "currency", currency: "UAH" })}
        </span>
      ),
    },
    {
      key: "actions",
      header: "",
      render: (line) =>
        canEdit ? (
          <button
            onClick={() => removeLineQuery.mutate(line.id)}
            disabled={removeLineQuery.isPending}
            style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", padding: "4px 8px" }}
            title={t("removeLineTitle")}
          >
            <Trash2 size={14} />
          </button>
        ) : null,
    },
  ];

  const createdDate = new Date(order.createdAt).toLocaleDateString(intlLocale, {
    day: "2-digit",
    month: "long",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });

  return (
    <div style={{ maxWidth: 900, display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
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
                {"✅ "}{t("completedAtPrefix")}{" "}
                {new Date(order.completedAt).toLocaleDateString(intlLocale, {
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
          <Btn
            variant="success"
            onClick={handleComplete}
            disabled={completeWorkOrder.isPending}
            style={{ flexShrink: 0 }}
          >
            {completeWorkOrder.isPending ? t("completing") : t("complete")}
          </Btn>
        )}
      </div>

      {/* Complete feedback */}
      {completedMsg && (
        <div style={{ padding: "10px 14px", background: "#064E3B", borderRadius: 8, color: "#34D399", fontSize: 13 }}>
          {t("completedMsg")}
        </div>
      )}
      {completeError && (
        <div style={{ padding: "10px 14px", background: "#1F1010", borderRadius: 8, color: "#F87171", fontSize: 13 }}>
          <strong>{t("completeErrorPrefix")}</strong> {completeError}
          {completeError.includes("422") || completeError.toLowerCase().includes("stock") ? (
            <div style={{ marginTop: 6, color: "#FCA5A5" }}>
              {t("insufficientPartsHint")}
            </div>
          ) : null}
        </div>
      )}

      {/* Lines table */}
      <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, margin: 0 }}>
            {t("linesTitle")}
          </h2>
          {canEdit && (
            <Btn variant="ghost" size="sm" icon={<Plus size={14} />} onClick={() => setShowAddLine(true)}>
              {t("addLine")}
            </Btn>
          )}
        </div>

        <Table
          columns={lineColumns}
          rows={order.lines}
          rowKey={(line) => line.id}
          emptyMessage={t("noLines")}
        />

        {/* Total */}
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 12, alignItems: "center" }}>
          <span style={{ color: "#9CA3AF", fontSize: 13 }}>{t("totalLabel")}</span>
          <span style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700 }}>
            {order.totalAmount.toLocaleString(intlLocale, {
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
