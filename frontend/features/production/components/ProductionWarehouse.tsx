"use client";

import { useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
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

  function batchRow(b: ProductionStockBatchDto) {
    const days = b.daysToExpiry ?? daysUntil(b.expiryDate);
    return (
      <tr key={b.id} style={{ borderTop: "1px solid #1F2937" }}>
        <td style={{ padding: "6px 10px", color: "#E8EDF5" }}>{b.batchNumber ?? "—"}</td>
        <td style={{ padding: "6px 10px" }}>{fmtQty(b.quantity, intl)}</td>
        <td style={{ padding: "6px 10px", color: expiryColor(days), fontWeight: 600 }}>{fmtDate(b.expiryDate, intl)}</td>
        <td style={{ padding: "6px 10px", color: expiryColor(days) }}>{days != null ? t("daysLeft", { days }) : "—"}</td>
        <td style={{ padding: "6px 10px" }}>{b.status ? <Pill>{b.status}</Pill> : "—"}</td>
        <td style={{ padding: "6px 10px" }}>
          {b.sourceType === "production" && b.sourceId ? (
            <Link href={`/production/orders/${b.sourceId}`} style={{ color: "#60A5FA" }}>
              {t("sourceOrder")}
            </Link>
          ) : (
            <span style={mutedText}>{b.sourceType ?? "—"}</span>
          )}
        </td>
      </tr>
    );
  }

  return (
    <div>
      <PageHeader
        title={t("title")}
        subtitle={t("subtitle")}
        right={<ProductionLocationSelect value={locationId} onChange={setLocationId} allLabel={t("allLocations")} />}
      />
      <div style={{ display: "flex", gap: 6, marginBottom: 16 }}>
        {KINDS.map((k) => (
          <Btn key={k} size="sm" variant={kind === k ? "primary" : "ghost"} onClick={() => setKind(k)}>
            {t(`filter_${k}`)}
          </Btn>
        ))}
      </div>

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
              <table style={{ width: "100%", fontSize: 12, color: "#9CA3AF", textAlign: "left", borderCollapse: "collapse" }}>
                <thead>
                  <tr style={{ color: "#4B5563", fontSize: 11, textTransform: "uppercase" }}>
                    <th style={{ padding: "6px 10px", textAlign: "left" }}>{t("batchNumber")}</th>
                    <th style={{ padding: "6px 10px", textAlign: "left" }}>{t("colQty")}</th>
                    <th style={{ padding: "6px 10px", textAlign: "left" }}>{t("batchExpiry")}</th>
                    <th style={{ padding: "6px 10px", textAlign: "left" }}>{t("batchDaysLeft")}</th>
                    <th style={{ padding: "6px 10px", textAlign: "left" }}>{t("batchStatus")}</th>
                    <th style={{ padding: "6px 10px", textAlign: "left" }}>{t("batchSource")}</th>
                  </tr>
                </thead>
                <tbody>{r.batches.map(batchRow)}</tbody>
              </table>
            ) : (
              <div style={mutedText}>{t("noBatches")}</div>
            )
          }
          minWidth={720}
        />
      )}
    </div>
  );
}
