"use client";

import { useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Plus } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useSupplierWarehouses } from "../hooks/useSupplierWarehouses";
import { useSupplierReceipts } from "../hooks/useSupplierInventory";
import { SupplierReceiptForm } from "./SupplierReceiptForm";
import type { SupplierStockReceipt, SupplierStockReceiptStatus } from "../types";

const selectStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "8px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  minWidth: 240,
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 4,
};

const STATUS_COLOR: Record<SupplierStockReceiptStatus, { bg: string; text: string }> = {
  draft: { bg: "#1E1B2E", text: "#A78BFA" },
  received: { bg: "#052E16", text: "#4ADE80" },
  cancelled: { bg: "#2D0F0F", text: "#F87171" },
};

function StatusPill({ status }: { status: SupplierStockReceiptStatus }) {
  const t = useTranslations("Dashboard.supplierCabinet.receiptsList.status");
  const c = STATUS_COLOR[status] ?? STATUS_COLOR.draft;
  return (
    <span
      style={{
        padding: "3px 9px",
        borderRadius: 20,
        background: c.bg,
        color: c.text,
        fontSize: 11,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      {t.has(status) ? t(status) : status}
    </span>
  );
}

type FormTarget =
  | { mode: "new"; warehouseId: string; warehouseName: string }
  | { mode: "resume"; receiptId: string; warehouseId: string; warehouseName: string };

export function SupplierReceiptsList() {
  const t = useTranslations("Dashboard.supplierCabinet.receiptsList");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { data: warehouses = [], isLoading: warehousesLoading } = useSupplierWarehouses();
  const activeWarehouses = useMemo(() => warehouses.filter((w) => w.isActive), [warehouses]);

  const [warehouseId, setWarehouseId] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<SupplierStockReceiptStatus | "">("");
  const [formTarget, setFormTarget] = useState<FormTarget | null>(null);

  const effectiveWarehouseId = warehouseId ?? activeWarehouses[0]?.id ?? null;
  const effectiveWarehouseName =
    activeWarehouses.find((w) => w.id === effectiveWarehouseId)?.name ?? "";

  const { data: receipts = [], isLoading, isError } = useSupplierReceipts(effectiveWarehouseId, {
    status: statusFilter || undefined,
  });

  const columns: TableColumn<SupplierStockReceipt>[] = [
    {
      key: "status",
      header: t("headerStatus"),
      align: "left",
      render: (r) => <StatusPill status={r.status} />,
    },
    {
      key: "warehouse",
      header: t("headerWarehouse"),
      cellStyle: { color: "#9CA3AF" },
      render: (r) => r.warehouseName || "—",
    },
    {
      key: "reference",
      header: t("headerReference"),
      cellStyle: { color: "#9CA3AF" },
      render: (r) => r.reference || "—",
    },
    {
      key: "date",
      header: t("headerDate"),
      cellStyle: { color: "#9CA3AF", whiteSpace: "nowrap" },
      render: (r) =>
        new Date(r.receivedAt ?? r.createdAt).toLocaleDateString(intlLocale),
    },
    {
      key: "items",
      header: t("headerItems"),
      render: (r) => r.items.length,
    },
  ];

  return (
    <div
      style={{
        background: "#111827",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "24px 28px",
        display: "flex",
        flexDirection: "column",
        gap: 16,
      }}
    >
      <div style={{ display: "flex", alignItems: "flex-end", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
        <div>
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>{t("title")}</h2>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 4 }}>{t("subtitle")}</p>
        </div>
        <div style={{ display: "flex", gap: 10, alignItems: "flex-end", flexWrap: "wrap" }}>
          <div>
            <label style={labelStyle}>{t("warehouseLabel")}</label>
            <select
              value={effectiveWarehouseId ?? ""}
              onChange={(e) => setWarehouseId(e.target.value || null)}
              style={selectStyle}
              disabled={warehousesLoading || activeWarehouses.length === 0}
            >
              {activeWarehouses.length === 0 && <option value="">{t("noWarehouses")}</option>}
              {activeWarehouses.map((w) => (
                <option key={w.id} value={w.id}>
                  {w.name}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label style={labelStyle}>{t("statusLabel")}</label>
            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value as SupplierStockReceiptStatus | "")}
              style={{ ...selectStyle, minWidth: 160 }}
            >
              <option value="">{t("statusAll")}</option>
              <option value="draft">{t("status.draft")}</option>
              <option value="received">{t("status.received")}</option>
              <option value="cancelled">{t("status.cancelled")}</option>
            </select>
          </div>
          <Btn
            icon={<Plus size={14} />}
            disabled={!effectiveWarehouseId}
            onClick={() =>
              effectiveWarehouseId &&
              setFormTarget({
                mode: "new",
                warehouseId: effectiveWarehouseId,
                warehouseName: effectiveWarehouseName,
              })
            }
          >
            {t("newButton")}
          </Btn>
        </div>
      </div>

      {activeWarehouses.length === 0 && !warehousesLoading ? (
        <div style={{ color: "#6B7280", fontSize: 13 }}>{t("noWarehousesHint")}</div>
      ) : isError ? (
        <div style={{ color: "#F87171", fontSize: 13 }}>{t("errorLoad")}</div>
      ) : (
        <Table
          columns={columns}
          rows={receipts}
          rowKey={(r) => r.id}
          isLoading={isLoading}
          emptyMessage={t("empty")}
          onRowClick={(r) =>
            setFormTarget({
              mode: "resume",
              receiptId: r.id,
              warehouseId: r.warehouseId,
              warehouseName: r.warehouseName,
            })
          }
        />
      )}

      {formTarget && (
        <SupplierReceiptForm
          warehouseId={formTarget.warehouseId}
          warehouseName={formTarget.warehouseName}
          receiptId={formTarget.mode === "resume" ? formTarget.receiptId : null}
          onClose={() => setFormTarget(null)}
        />
      )}
    </div>
  );
}
