"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { useProductionOrders } from "../hooks/useProduction";
import { ProductionOrderForm } from "./ProductionOrderForm";
import type { ProductionOrderListItemDto, ProductionOrderStatus } from "../types";
import { Table, type TableColumn } from "@/components/ui/Table";

export function ProductionOrderTable() {
  const t = useTranslations("Dashboard.production.orderTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const router = useRouter();
  const [statusFilter, setStatusFilter] = useState("");
  const [createOpen, setCreateOpen] = useState(false);

  const STATUS_OPTIONS: { value: string; label: string }[] = [
    { value: "", label: t("statusFilterAll") },
    { value: "planned", label: t("statusFilterPlanned") },
    { value: "in_progress", label: t("statusFilterInProgress") },
    { value: "done", label: t("statusFilterDone") },
    { value: "cancelled", label: t("statusFilterCancelled") },
  ];

  const { data: orders = [], isLoading, isError } = useProductionOrders(
    statusFilter || undefined
  );

  if (isLoading) {
    return (
      <div style={{ padding: "48px 32px", color: "#6B7280", fontSize: 14 }}>
        {t("loading")}
      </div>
    );
  }
  if (isError) {
    return (
      <div style={{ padding: "48px 32px", color: "#F87171", fontSize: 14 }}>
        {t("loadError")}
      </div>
    );
  }

  const columns: TableColumn<ProductionOrderListItemDto>[] = [
    {
      key: "recipe",
      header: t("headerRecipe"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (order) => order.recipeName,
    },
    {
      key: "location",
      header: t("headerLocation"),
      render: (order) => order.locationName,
    },
    {
      key: "plannedQty",
      header: t("headerPlannedQty"),
      render: (order) => order.plannedQty,
    },
    {
      key: "status",
      header: t("headerStatus"),
      render: (order) => <OrderStatusBadge status={order.status} />,
    },
    {
      key: "createdAt",
      header: t("headerCreatedAt"),
      cellStyle: { color: "#6B7280", fontSize: 12 },
      render: (order) => formatDate(order.createdAt, intlLocale),
    },
    {
      key: "completedAt",
      header: t("headerCompletedAt"),
      cellStyle: { color: "#6B7280", fontSize: 12 },
      render: (order) => (order.completedAt ? formatDate(order.completedAt, intlLocale) : "—"),
    },
  ];

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
          {t("title")}
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
            {t("addOrder")}
          </button>
        </div>
      </div>

      {/* Table */}
      <Table
        columns={columns}
        rows={orders}
        rowKey={(order) => order.id}
        onRowClick={(order) => router.push(`/production/orders/${order.id}`)}
        emptyMessage={t("empty")}
      />

      {createOpen && <ProductionOrderForm onClose={() => setCreateOpen(false)} />}
    </div>
  );
}

// ── Helpers ────────────────────────────────────────────────────────────────────

export function OrderStatusBadge({ status }: { status: ProductionOrderStatus }) {
  const t = useTranslations("Dashboard.production.orderStatusBadge");
  const cfg: Record<ProductionOrderStatus, { bg: string; color: string }> = {
    planned: { bg: "#1F2937", color: "#9CA3AF" },
    in_progress: { bg: "#1E3A5F", color: "#60A5FA" },
    done: { bg: "#064E3B", color: "#34D399" },
    cancelled: { bg: "#1F1010", color: "#F87171" },
  };
  const { bg, color } = cfg[status] ?? cfg.planned;
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
      {t(status)}
    </span>
  );
}

function formatDate(iso: string, intlLocale: string) {
  return new Date(iso).toLocaleDateString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}
