"use client";

import { useTranslations, useLocale } from "next-intl";
import { AlertTriangle } from "lucide-react";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useProductionRequestSummary } from "../hooks/useProductionRequests";
import type { ProductionRequestSummaryItemDto } from "../types";
import { formatDateOnly } from "./ProductionRequestStatusBadge";

/** Aggregated open requests by item (production module only). */
export function ProductionRequestSummary({ productionLocationId }: { productionLocationId?: string }) {
  const t = useTranslations("Dashboard.production.requests.summary");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data = [], isLoading, isError } = useProductionRequestSummary(
    true,
    productionLocationId || undefined
  );

  if (isError) return <div style={{ color: "#F87171", fontSize: 14 }}>{t("loadError")}</div>;

  const columns: TableColumn<ProductionRequestSummaryItemDto>[] = [
    {
      key: "item",
      header: t("colItem"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (r) => (
        <span style={{ display: "inline-flex", alignItems: "center", gap: 8 }}>
          {r.itemName}
          {!r.hasRecipe && (
            <span
              title={t("noRecipe")}
              style={{ display: "inline-flex", alignItems: "center", gap: 4, color: "#FBBF24", fontSize: 11 }}
            >
              <AlertTriangle size={13} /> {t("noRecipe")}
            </span>
          )}
        </span>
      ),
    },
    { key: "count", header: t("colRequests"), render: (r) => r.requestCount },
    { key: "req", header: t("colRequested"), render: (r) => `${r.requestedQty} ${r.unit}` },
    {
      key: "produce",
      header: t("colToProduce"),
      cellStyle: { fontWeight: 600, color: "#34D399" },
      render: (r) => `${r.qtyToProduce} ${r.unit}`,
    },
    {
      key: "earliest",
      header: t("colEarliest"),
      cellStyle: { color: "#9CA3AF", fontSize: 12 },
      render: (r) => formatDateOnly(r.earliestNeededBy, intlLocale),
    },
  ];

  return (
    <Table
      columns={columns}
      rows={data}
      rowKey={(r) => r.itemId}
      isLoading={isLoading}
      emptyMessage={t("empty")}
    />
  );
}
