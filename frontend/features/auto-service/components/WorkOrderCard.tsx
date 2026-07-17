"use client";

import { useRouter } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import type { WorkOrderSummaryDto, WorkOrderStatus } from "../types";
import { useUpdateWorkOrder } from "../hooks/useAutoService";

// ─── Status Config ────────────────────────────────────────────────────────────
// Status labels live in Dashboard.autoService.workOrderStatus (each component
// below resolves them locally via its own useTranslations call).

const STATUS_COLORS: Record<WorkOrderStatus, { bg: string; color: string }> = {
  new: { bg: "#1F2937", color: "#9CA3AF" },
  in_progress: { bg: "#1E3A5F", color: "#60A5FA" },
  waiting_parts: { bg: "#3D2E00", color: "#FBBF24" },
  done: { bg: "#064E3B", color: "#34D399" },
  invoiced: { bg: "#2E1065", color: "#A78BFA" },
};

const STATUS_SEQUENCE: WorkOrderStatus[] = [
  "new",
  "in_progress",
  "waiting_parts",
  "done",
  "invoiced",
];

// ─── Status Badge ─────────────────────────────────────────────────────────────

export function StatusBadge({ status }: { status: WorkOrderStatus }) {
  const t = useTranslations("Dashboard.autoService.workOrderStatus");
  const colors = STATUS_COLORS[status];
  return (
    <span
      style={{
        display: "inline-block",
        padding: "2px 8px",
        borderRadius: 12,
        fontSize: 11,
        fontWeight: 600,
        background: colors.bg,
        color: colors.color,
      }}
    >
      {t(status)}
    </span>
  );
}

// ─── WorkOrderCard ────────────────────────────────────────────────────────────

interface Props {
  order: WorkOrderSummaryDto;
}

export function WorkOrderCard({ order }: Props) {
  const router = useRouter();
  const t = useTranslations("Dashboard.autoService.card");
  const tStatus = useTranslations("Dashboard.autoService.workOrderStatus");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const updateWorkOrder = useUpdateWorkOrder(order.id);

  const currentIndex = STATUS_SEQUENCE.indexOf(order.status);
  const nextStatus =
    currentIndex < STATUS_SEQUENCE.length - 1
      ? STATUS_SEQUENCE[currentIndex + 1]
      : null;

  function handleMoveNext(e: React.MouseEvent) {
    e.stopPropagation();
    if (!nextStatus) return;
    updateWorkOrder.mutate({ status: nextStatus });
  }

  const createdDate = new Date(order.createdAt).toLocaleDateString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });

  return (
    <div
      onClick={() => router.push(`/auto-service/work-orders/${order.id}`)}
      style={{
        background: "#111827",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "12px 14px",
        cursor: "pointer",
        transition: "border-color 0.15s",
        display: "flex",
        flexDirection: "column",
        gap: 8,
      }}
      onMouseEnter={(e) => {
        (e.currentTarget as HTMLElement).style.borderColor = "#374151";
      }}
      onMouseLeave={(e) => {
        (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
      }}
    >
      {/* Vehicle */}
      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 6 }}>
        <div>
          <div style={{ color: "#E8EDF5", fontWeight: 600, fontSize: 13 }}>
            {order.vehicleBrand} {order.vehicleModel}
          </div>
          <div style={{ color: "#6B7280", fontSize: 12, marginTop: 2 }}>
            {order.licensePlate}
          </div>
        </div>
        <StatusBadge status={order.status} />
      </div>

      {/* Mechanic */}
      {order.mechanicName && (
        <div style={{ color: "#9CA3AF", fontSize: 12 }}>
          👤 {order.mechanicName}
        </div>
      )}

      {/* Meta row */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          borderTop: "1px solid #1F2937",
          paddingTop: 8,
          marginTop: 2,
        }}
      >
        <div style={{ color: "#6B7280", fontSize: 11 }}>
          {createdDate} · {t("lineCountShort", { count: order.lineCount })}
        </div>
        <div style={{ color: "#E8EDF5", fontWeight: 700, fontSize: 13 }}>
          {order.totalAmount.toLocaleString(intlLocale, {
            style: "currency",
            currency: "UAH",
            maximumFractionDigits: 0,
          })}
        </div>
      </div>

      {/* Move button */}
      {nextStatus && (
        <button
          onClick={handleMoveNext}
          disabled={updateWorkOrder.isPending}
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            gap: 4,
            padding: "5px 10px",
            borderRadius: 6,
            border: "1px solid #1F2937",
            background: "transparent",
            color: "#6B7280",
            fontSize: 11,
            cursor: "pointer",
            transition: "border-color 0.1s, color 0.1s",
          }}
          onMouseEnter={(e) => {
            (e.currentTarget as HTMLElement).style.borderColor = "#3B82F6";
            (e.currentTarget as HTMLElement).style.color = "#60A5FA";
          }}
          onMouseLeave={(e) => {
            (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
            (e.currentTarget as HTMLElement).style.color = "#6B7280";
          }}
        >
          {updateWorkOrder.isPending ? "…" : t("moveTo", { status: tStatus(nextStatus) })}
        </button>
      )}
    </div>
  );
}
