"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { useProductionOrders } from "../hooks/useProduction";
import { ProductionOrderForm } from "./ProductionOrderForm";
import type { ProductionOrderListItemDto, ProductionOrderStatus } from "../types";
import { Table, type TableColumn } from "@/components/ui/Table";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { StatusPill } from "@/components/ui/StatusPill";
import { filterInputStyle } from "@/components/ui/fieldStyles";
import { formatDateOnly } from "./ProductionRequestStatusBadge";

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

  if (isError) {
    return (
      <PageContainer style={{ color: "#F87171", fontSize: 13 }}>
        {t("loadError")}
      </PageContainer>
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
      key: "stores",
      header: t("headerStores"),
      cellStyle: { color: "#9CA3AF", fontSize: 12 },
      render: (order) =>
        order.requestCount
          ? t("storesCell", { requests: order.requestCount, stores: order.storeCount ?? 0 })
          : "—",
    },
    {
      key: "neededBy",
      header: t("headerNeededBy"),
      cellStyle: { color: "#9CA3AF", fontSize: 12 },
      render: (order) => formatDateOnly(order.neededBy, intlLocale),
    },
    {
      key: "plannedDate",
      header: t("headerPlannedDate"),
      cellStyle: { color: "#9CA3AF", fontSize: 12 },
      render: (order) => formatDateOnly(order.plannedDate, intlLocale),
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
    <PageContainer>
      <PageHeader
        title={t("title")}
        actions={
          <Btn icon={<Plus size={15} />} onClick={() => setCreateOpen(true)}>
            {t("addOrder")}
          </Btn>
        }
      />

      {/* Filters */}
      <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          style={{ ...filterInputStyle, cursor: "pointer" }}
        >
          {STATUS_OPTIONS.map((opt) => (
            <option key={opt.value} value={opt.value}>
              {opt.label}
            </option>
          ))}
        </select>
      </div>

      {/* Table */}
      <Table
        columns={columns}
        rows={orders}
        rowKey={(order) => order.id}
        onRowClick={(order) => router.push(`/production/orders/${order.id}`)}
        isLoading={isLoading}
        emptyMessage={isLoading ? t("loading") : t("empty")}
      />

      {createOpen && <ProductionOrderForm onClose={() => setCreateOpen(false)} />}
    </PageContainer>
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
  return <StatusPill bg={bg} text={color}>{t(status)}</StatusPill>;
}

function formatDate(iso: string, intlLocale: string) {
  return new Date(iso).toLocaleDateString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}
