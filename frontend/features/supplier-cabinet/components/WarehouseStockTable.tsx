"use client";

import { useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { SlidersHorizontal } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import { STATUS_COLOR, type BatchStatus } from "@/features/shelf/types";
import { useSupplierWarehouses } from "../hooks/useSupplierWarehouses";
import { useWarehouseStock, useAdjustStockBatch } from "../hooks/useSupplierInventory";
import type { SupplierStock } from "../types";

const PAGE_SIZE = 50;

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

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

function StatusChip({ status }: { status: BatchStatus }) {
  const t = useTranslations("Dashboard.supplierCabinet.stockTable.status");
  const c = STATUS_COLOR[status] ?? STATUS_COLOR.safe;
  return (
    <span
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 5,
        padding: "3px 8px",
        borderRadius: 20,
        background: c.bg,
        color: c.text,
        fontSize: 11,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      <span style={{ width: 6, height: 6, borderRadius: "50%", background: c.dot, flexShrink: 0 }} />
      {t.has(status) ? t(status) : status}
    </span>
  );
}

interface AdjustModalProps {
  batch: SupplierStock;
  onClose: () => void;
}

function AdjustModal({ batch, onClose }: AdjustModalProps) {
  const t = useTranslations("Dashboard.supplierCabinet.stockTable.adjustModal");
  const adjust = useAdjustStockBatch();
  const [quantity, setQuantity] = useState(String(batch.quantity));
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const qty = parseFloat(quantity);
    if (isNaN(qty) || qty < 0) {
      setError(t("validationQuantity"));
      return;
    }
    setError(null);
    try {
      await adjust.mutateAsync({
        batchId: batch.id,
        body: { quantity: qty, reason: reason.trim() || null },
      });
      onClose();
    } catch (err) {
      const msg = (err as Error)?.message ?? t("errorDefault");
      setError(msg.includes("інша операція") ? t("concurrencyRetry") : msg);
    }
  }

  return (
    <>
      <div
        onClick={onClose}
        style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,0.6)", zIndex: 300, backdropFilter: "blur(2px)" }}
      />
      <div
        style={{
          position: "fixed",
          top: "50%",
          left: "50%",
          transform: "translate(-50%, -50%)",
          width: "min(440px, 95vw)",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 14,
          zIndex: 301,
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "18px 22px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>{t("title")}</h2>
          <button
            onClick={onClose}
            style={{
              background: "transparent",
              border: "1px solid #1F2937",
              borderRadius: 8,
              padding: "5px 9px",
              color: "#4B5563",
              fontSize: 16,
              cursor: "pointer",
            }}
          >
            ✕
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ padding: 22 }}>
          <p style={{ color: "#9CA3AF", fontSize: 12, margin: "0 0 14px" }}>
            {t("context", {
              item: batch.supplierItemName,
              expiry: batch.expiryDate,
              current: batch.quantity,
            })}
          </p>
          <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            <div>
              <label style={labelStyle}>{t("quantityLabel")}</label>
              <input
                type="number"
                step="any"
                min="0"
                value={quantity}
                onChange={(e) => setQuantity(e.target.value)}
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>{t("reasonLabel")}</label>
              <input
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                placeholder={t("reasonPlaceholder")}
                style={inputStyle}
              />
            </div>
          </div>

          <div style={{ display: "flex", gap: 10, marginTop: 22 }}>
            <Btn type="submit" disabled={adjust.isPending} style={{ flex: 1, justifyContent: "center" }}>
              {adjust.isPending ? t("saving") : t("save")}
            </Btn>
            <Btn type="button" variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Btn>
          </div>

          {error && <p style={{ color: "#F87171", fontSize: 12, marginTop: 10 }}>{error}</p>}
        </form>
      </div>
    </>
  );
}

export function WarehouseStockTable() {
  const t = useTranslations("Dashboard.supplierCabinet.stockTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { data: warehouses = [], isLoading: warehousesLoading } = useSupplierWarehouses();
  const activeWarehouses = useMemo(() => warehouses.filter((w) => w.isActive), [warehouses]);

  const [warehouseId, setWarehouseId] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [adjustTarget, setAdjustTarget] = useState<SupplierStock | null>(null);

  const effectiveWarehouseId = warehouseId ?? activeWarehouses[0]?.id ?? null;

  const { data, isLoading, isError } = useWarehouseStock(effectiveWarehouseId, {
    page,
    pageSize: PAGE_SIZE,
  });

  const rows = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;

  function num(v: number): string {
    return v.toLocaleString(intlLocale, { maximumFractionDigits: 3 });
  }

  function sourceLabel(sourceType: string | null): string {
    if (!sourceType) return "—";
    return t.has(`source.${sourceType}`) ? t(`source.${sourceType}`) : sourceType;
  }

  const columns: TableColumn<SupplierStock>[] = [
    {
      key: "item",
      header: t("headerItem"),
      align: "left",
      cellStyle: { fontWeight: 600, color: "#E8EDF5" },
      render: (b) => b.supplierItemName || "—",
    },
    {
      key: "expiry",
      header: t("headerExpiry"),
      cellStyle: { whiteSpace: "nowrap" },
      render: (b) => (
        <div>
          <div style={{ color: "#E8EDF5" }}>
            {new Date(b.expiryDate).toLocaleDateString(intlLocale)}
          </div>
          <div style={{ fontSize: 11, color: b.daysLeft < 0 ? "#F87171" : "#6B7280", marginTop: 2 }}>
            {b.daysLeft < 0
              ? t("daysOverdueHint", { days: Math.abs(b.daysLeft) })
              : t("daysLeftHint", { days: b.daysLeft })}
          </div>
        </div>
      ),
    },
    {
      key: "quantity",
      header: t("headerQuantity"),
      cellStyle: { color: "#E8EDF5", fontWeight: 600 },
      render: (b) => num(b.quantity),
    },
    {
      key: "quantityInitial",
      header: t("headerQuantityInitial"),
      cellStyle: { color: "#9CA3AF" },
      render: (b) => num(b.quantityInitial),
    },
    {
      key: "batch",
      header: t("headerBatch"),
      cellStyle: { color: "#9CA3AF" },
      render: (b) => b.batchNumber || "—",
    },
    {
      key: "status",
      header: t("headerStatus"),
      render: (b) => <StatusChip status={b.status} />,
    },
    {
      key: "source",
      header: t("headerSource"),
      cellStyle: { color: "#9CA3AF" },
      render: (b) => sourceLabel(b.sourceType),
    },
    {
      key: "actions",
      header: "",
      render: (b) => (
        <div onClick={(e) => e.stopPropagation()}>
          <Btn size="sm" variant="ghost" icon={<SlidersHorizontal size={13} />} onClick={() => setAdjustTarget(b)}>
            {t("adjustButton")}
          </Btn>
        </div>
      ),
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
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
        <div>
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>{t("title")}</h2>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 4 }}>{t("subtitle")}</p>
        </div>
        <div>
          <label style={{ ...labelStyle, marginBottom: 4 }}>{t("warehouseLabel")}</label>
          <select
            value={effectiveWarehouseId ?? ""}
            onChange={(e) => {
              setWarehouseId(e.target.value || null);
              setPage(1);
            }}
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
      </div>

      {activeWarehouses.length === 0 && !warehousesLoading ? (
        <div style={{ color: "#6B7280", fontSize: 13 }}>{t("noWarehousesHint")}</div>
      ) : isError ? (
        <div style={{ color: "#F87171", fontSize: 13 }}>{t("errorLoad")}</div>
      ) : (
        <Table
          columns={columns}
          rows={rows}
          rowKey={(b) => b.id}
          isLoading={isLoading}
          emptyMessage={t("empty")}
          page={page}
          totalPages={totalPages}
          totalCount={data?.totalCount ?? 0}
          onPageChange={setPage}
        />
      )}

      {adjustTarget && <AdjustModal batch={adjustTarget} onClose={() => setAdjustTarget(null)} />}
    </div>
  );
}
