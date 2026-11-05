"use client";

import { useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { Tabs } from "@/components/ui/Tabs";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useProductionStock } from "../hooks/useProductionInsights";
import type { ProductionStockItemDto, ProductionStockBatchDto, StockKind } from "../insights-types";
import {
  PageHeader,
  ProductionLocationSelect,
  StateBox,
  Pill,
  mutedText,
  fmtDate,
  fmtQty,
  daysUntil,
  expiryColor,
  useIntlLocale,
} from "./InsightsShared";

const KINDS: StockKind[] = ["all", "raw", "finished"];

export function ProductionWarehouse() {
  const t = useTranslations("Dashboard.production.warehouse");
  const intl = useIntlLocale();
  const [kind, setKind] = useState<StockKind>("all");
  const [locationId, setLocationId] = useState("");
  const [expanded, setExpanded] = useState<string | null>(null);
  const { data, isLoading, isError } = useProductionStock({ kind, locationId: locationId || undefined });

  const columns: TableColumn<ProductionStockItemDto>[] = [
    { key: "name", header: t("colItem"), render: (r) => <b style={{ color: "#E8EDF5" }}>{r.itemName ?? "—"}</b> },
    {
      key: "kind",
      header: t("colKind"),
      render: (r) =>
        r.kind === "raw" ? (
          <Pill color="#FBBF24" bg="#3a2a05">{t("kindRaw")}</Pill>
        ) : r.kind === "finished" ? (
          <Pill color="#4ADE80" bg="#0b2a1e">{t("kindFinished")}</Pill>
        ) : (
          <Pill>{t("kindOther")}</Pill>
        ),
    },
    { key: "qty", header: t("colQty"), render: (r) => `${fmtQty(r.totalQty, intl)} ${r.unit ?? ""}` },
    {
      key: "expiry",
      header: t("colNearestExpiry"),
      render: (r) => (
        <span style={{ color: expiryColor(daysUntil(r.nearestExpiry)), fontWeight: 600 }}>{fmtDate(r.nearestExpiry, intl)}</span>
      ),
    },
    { key: "loc", header: t("colLocation"), render: (r) => r.locationName ?? "—" },
    { key: "batches", header: t("colBatches"), render: (r) => r.batches?.length ?? 0 },
  ];

  const batchColumns: TableColumn<ProductionStockBatchDto>[] = [
    { key: "batch", header: t("batchNumber"), render: (b) => <span style={{ color: "#E8EDF5" }}>{b.batchNumber ?? "—"}</span> },
    { key: "qty", header: t("colQty"), render: (b) => fmtQty(b.quantity, intl) },
    {
      key: "expiry",
      header: t("batchExpiry"),
      render: (b) => (
        <span style={{ color: expiryColor(b.daysToExpiry ?? daysUntil(b.expiryDate)), fontWeight: 600 }}>{fmtDate(b.expiryDate, intl)}</span>
      ),
    },
    {
      key: "days",
      header: t("batchDaysLeft"),
      render: (b) => {
        const days = b.daysToExpiry ?? daysUntil(b.expiryDate);
        return <span style={{ color: expiryColor(days) }}>{days != null ? t("daysLeft", { days }) : "—"}</span>;
      },
    },
    { key: "status", header: t("batchStatus"), render: (b) => (b.status ? <Pill>{b.status}</Pill> : "—") },
    {
      key: "source",
      header: t("batchSource"),
      render: (b) =>
        b.sourceType === "production" && b.sourceId ? (
          <Link href={`/production/orders/${b.sourceId}`} style={{ color: "#60A5FA" }}>
            {t("sourceOrder")}
          </Link>
        ) : (
          <span style={mutedText}>{b.sourceType ?? "—"}</span>
        ),
    },
  ];

  return (
    <PageContainer>
      <PageHeader
        title={t("title")}
        subtitle={t("subtitle")}
        right={<ProductionLocationSelect value={locationId} onChange={setLocationId} allLabel={t("allLocations")} />}
      />
      <Tabs
        items={KINDS.map((k) => ({ key: k, label: t(`filter_${k}`) }))}
        value={kind}
        onChange={setKind}
      />

      {isLoading ? (
        <StateBox kind="loading">{t("loading")}</StateBox>
      ) : isError ? (
        <StateBox kind="error">{t("loadError")}</StateBox>
      ) : (
        <Table
          columns={columns}
          rows={data ?? []}
          rowKey={(r) => r.itemId}
          emptyMessage={t("empty")}
          onRowClick={(r) => setExpanded((cur) => (cur === r.itemId ? null : r.itemId))}
          expandedRowKey={expanded}
          renderExpanded={(r) =>
            r.batches?.length ? (
              <Table columns={batchColumns} rows={r.batches} rowKey={(b) => b.id} minWidth={560} />
            ) : (
              <div style={mutedText}>{t("noBatches")}</div>
            )
          }
          minWidth={720}
        />
      )}
    </PageContainer>
  );
}
