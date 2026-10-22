"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { PageHeader } from "@/components/ui/PageHeader";
import { useWorkOrders, useVehicles } from "../hooks/useAutoService";
import { WorkOrderCard } from "./WorkOrderCard";
import { CreateWorkOrderModal } from "./CreateWorkOrderModal";
import type { WorkOrderStatus } from "../types";

const COLUMNS: WorkOrderStatus[] = [
  "new",
  "in_progress",
  "waiting_parts",
  "done",
  "invoiced",
];

export function WorkOrderKanban() {
  const t = useTranslations("Dashboard.autoService.kanban");
  const tStatus = useTranslations("Dashboard.autoService.workOrderStatus");
  const { data: orders, isLoading, isError } = useWorkOrders();
  const [showCreate, setShowCreate] = useState(false);

  const byStatus = (status: WorkOrderStatus) =>
    (orders ?? []).filter((o) => o.status === status);

  if (isLoading) {
    return (
      <div
        style={{
          display: "flex",
          gap: 16,
          overflowX: "auto",
          paddingBottom: 16,
        }}
      >
        {COLUMNS.map((col) => (
          <div
            key={col}
            style={{
              minWidth: 240,
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 10,
              padding: "12px 10px",
              height: 400,
            }}
          />
        ))}
      </div>
    );
  }

  if (isError) {
    return (
      <div style={{ color: "#F87171", fontSize: 13, padding: "32px 0", textAlign: "center" }}>
        {t("loadError")}
      </div>
    );
  }

  return (
    <>
      <PageHeader
        title={t("pageTitle")}
        subtitle={t("pageSubtitle")}
        actions={
          <Btn onClick={() => setShowCreate(true)} icon={<Plus size={16} />}>
            {t("newWorkOrder")}
          </Btn>
        }
      />

      {/* Kanban board */}
      <div
        style={{
          display: "flex",
          gap: 14,
          overflowX: "auto",
          paddingBottom: 16,
          alignItems: "flex-start",
        }}
      >
        {COLUMNS.map((status) => {
          const columnOrders = byStatus(status);
          return (
            <div
              key={status}
              style={{
                minWidth: 240,
                maxWidth: 280,
                flex: "0 0 260px",
                background: "#0D1117",
                border: "1px solid #1F2937",
                borderRadius: 10,
                padding: "10px",
                display: "flex",
                flexDirection: "column",
                gap: 8,
              }}
            >
              {/* Column header */}
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  padding: "4px 4px 8px",
                  borderBottom: "1px solid #1F2937",
                }}
              >
                <span
                  style={{
                    color: "#9CA3AF",
                    fontSize: 12,
                    fontWeight: 600,
                    textTransform: "uppercase",
                    letterSpacing: "0.05em",
                  }}
                >
                  {tStatus(status)}
                </span>
                <span
                  style={{
                    background: "#1F2937",
                    color: "#6B7280",
                    borderRadius: 12,
                    padding: "1px 8px",
                    fontSize: 11,
                    fontWeight: 600,
                  }}
                >
                  {columnOrders.length}
                </span>
              </div>

              {/* Cards */}
              <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                {columnOrders.length === 0 ? (
                  <div
                    style={{
                      padding: "20px 0",
                      textAlign: "center",
                      color: "#374151",
                      fontSize: 12,
                    }}
                  >
                    {t("noOrders")}
                  </div>
                ) : (
                  columnOrders.map((order) => (
                    <WorkOrderCard key={order.id} order={order} />
                  ))
                )}
              </div>
            </div>
          );
        })}
      </div>

      {/* Create Modal */}
      {showCreate && <CreateWorkOrderModal onClose={() => setShowCreate(false)} />}
    </>
  );
}
