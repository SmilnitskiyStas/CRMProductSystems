"use client";

import { useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useStock } from "@/features/shelf/hooks/useStock";
import type { ProductStockDto } from "@/features/shelf/types";
import { useCreateTransfer } from "../hooks/useTransfers";
import { Btn } from "@/components/ui/Btn";

interface Props {
  onSuccess: () => void;
  onCancel: () => void;
}

interface TransferRow {
  productStockId: string; // = ProductStockDto.id — keeps two batches of the same product distinct
  productId: string;
  productName: string;
  batchNumber: string | null;
  expiryDate: string;
  availableQty: number;
  quantity: string; // controlled input, parsed on submit
}

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px",
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  marginBottom: 5,
};

const emptyHintStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 12,
  padding: "10px 4px",
};

const batchListStyle: React.CSSProperties = {
  maxHeight: 220,
  overflowY: "auto",
  border: "1px solid #1F2937",
  borderRadius: 8,
};

const batchRowStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  justifyContent: "space-between",
  gap: 10,
  padding: "9px 12px",
  borderBottom: "1px solid #1F2937",
};

const badgeStyle: React.CSSProperties = {
  flexShrink: 0,
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 20,
  color: "#6B7280",
  fontSize: 11,
  fontWeight: 600,
  padding: "3px 8px",
  whiteSpace: "nowrap",
};

const selectedRowStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: 10,
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "8px 10px",
};

const removeBtnStyle: React.CSSProperties = {
  background: "transparent",
  border: "none",
  color: "#6B7280",
  fontSize: 18,
  lineHeight: 1,
  cursor: "pointer",
  padding: 4,
  flexShrink: 0,
};

const errorBoxStyle: React.CSSProperties = {
  background: "#2D0F0F",
  border: "1px solid #7F1D1D",
  borderRadius: 8,
  color: "#F87171",
  fontSize: 12,
  padding: "8px 12px",
};

export function CreateTransferForm({ onSuccess, onCancel }: Props) {
  const t = useTranslations("Dashboard.transfers.createForm");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { data: locations = [] } = useLocations();
  const create = useCreateTransfer();

  const [fromStoreId, setFromStoreId] = useState("");
  const [toStoreId, setToStoreId] = useState("");
  const [notes, setNotes] = useState("");
  const [batchFilterQuery, setBatchFilterQuery] = useState("");
  const [rows, setRows] = useState<TransferRow[]>([]);
  const [error, setError] = useState<string | null>(null);

  const { data: stockBatches = [], isLoading: isLoadingBatches } = useStock(
    { store_id: fromStoreId },
    !!fromStoreId,
  );

  const toStoreOptions = useMemo(
    () => locations.filter((l) => l.id !== fromStoreId),
    [locations, fromStoreId],
  );

  const availableBatches = useMemo(
    () =>
      [...stockBatches]
        .filter((b) => b.quantity > 0)
        .sort((a, b) => a.expiryDate.localeCompare(b.expiryDate)),
    [stockBatches],
  );

  const filteredBatches = useMemo(() => {
    if (!batchFilterQuery) return availableBatches;
    const q = batchFilterQuery.toLowerCase();
    return availableBatches.filter(
      (b) =>
        b.productName.toLowerCase().includes(q) ||
        (b.productBarcode ?? "").toLowerCase().includes(q) ||
        (b.batchNumber ?? "").toLowerCase().includes(q),
    );
  }, [availableBatches, batchFilterQuery]);

  function isRowAdded(batchId: string) {
    return rows.some((r) => r.productStockId === batchId);
  }

  function setFromStore(id: string) {
    setFromStoreId(id);
    setToStoreId((prev) => (prev === id ? "" : prev));
    setRows([]);
    setBatchFilterQuery("");
    setError(null);
  }

  function addRow(batch: ProductStockDto) {
    if (isRowAdded(batch.id)) return;
    setRows((prev) => [
      ...prev,
      {
        productStockId: batch.id,
        productId: batch.productId,
        productName: batch.productName,
        batchNumber: batch.batchNumber,
        expiryDate: batch.expiryDate,
        availableQty: batch.quantity,
        quantity: "",
      },
    ]);
  }

  function removeRow(productStockId: string) {
    setRows((prev) => prev.filter((r) => r.productStockId !== productStockId));
  }

  function setRowQuantity(productStockId: string, value: string) {
    setRows((prev) =>
      prev.map((r) => (r.productStockId === productStockId ? { ...r, quantity: value } : r)),
    );
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (!toStoreId) {
      setError(t("errorNoDestination"));
      return;
    }
    if (rows.length === 0) {
      setError(t("errorNoItems"));
      return;
    }
    for (const row of rows) {
      const qty = parseFloat(row.quantity);
      if (isNaN(qty) || qty <= 0) {
        setError(t("errorQuantityRequired", { product: row.productName }));
        return;
      }
      if (qty > row.availableQty) {
        setError(
          t("errorQuantityExceeds", { product: row.productName, available: row.availableQty }),
        );
        return;
      }
    }

    try {
      await create.mutateAsync({
        fromStoreId,
        toStoreId,
        transferType: "store_to_store",
        notes: notes.trim() || undefined,
        items: rows.map((r) => ({
          productStockId: r.productStockId,
          quantity: parseFloat(r.quantity),
        })),
      });
      onSuccess();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t("errorSaveFailed"));
    }
  }

  return (
    <form
      onSubmit={handleSubmit}
      noValidate
      style={{ display: "flex", flexDirection: "column", gap: 14 }}
    >
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
        <div>
          <label style={labelStyle}>{t("fromStoreLabel")}</label>
          <select
            value={fromStoreId}
            onChange={(e) => setFromStore(e.target.value)}
            style={inputStyle}
          >
            <option value="">{t("fromStorePlaceholder")}</option>
            {locations.map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label style={labelStyle}>{t("toStoreLabel")}</label>
          <select
            value={toStoreId}
            onChange={(e) => setToStoreId(e.target.value)}
            style={inputStyle}
          >
            <option value="">{t("toStorePlaceholder")}</option>
            {toStoreOptions.map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Batch picker */}
      <div>
        {!fromStoreId ? (
          <div style={emptyHintStyle}>{t("sourceStoreHint")}</div>
        ) : (
          <>
            <input
              type="text"
              value={batchFilterQuery}
              onChange={(e) => setBatchFilterQuery(e.target.value)}
              placeholder={t("batchSearchPlaceholder")}
              style={{ ...inputStyle, marginBottom: 8 }}
            />
            <div style={batchListStyle}>
              {isLoadingBatches ? (
                <div style={emptyHintStyle}>{tCommon("loading")}</div>
              ) : availableBatches.length === 0 ? (
                <div style={emptyHintStyle}>{t("noBatchesAtStore")}</div>
              ) : filteredBatches.length === 0 ? (
                <div style={emptyHintStyle}>{t("batchSearchEmpty")}</div>
              ) : (
                filteredBatches.map((b) => {
                  const added = isRowAdded(b.id);
                  return (
                    <div
                      key={b.id}
                      onClick={() => addRow(b)}
                      style={{
                        ...batchRowStyle,
                        opacity: added ? 0.5 : 1,
                        cursor: added ? "default" : "pointer",
                      }}
                    >
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div
                          style={{
                            color: "#E8EDF5",
                            fontSize: 13,
                            fontWeight: 500,
                            overflow: "hidden",
                            textOverflow: "ellipsis",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {b.productName}
                        </div>
                        <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
                          {b.batchNumber ? `${b.batchNumber} · ` : ""}
                          {new Date(b.expiryDate).toLocaleDateString(intlLocale)}
                        </div>
                      </div>
                      <span style={badgeStyle}>
                        {added ? t("alreadyAdded") : t("availableLabel", { qty: b.quantity })}
                      </span>
                    </div>
                  );
                })
              )}
            </div>
          </>
        )}
      </div>

      {/* Selected rows */}
      <div>
        <label style={labelStyle}>{t("itemsSectionTitle", { count: rows.length })}</label>
        {rows.length === 0 ? (
          <div style={emptyHintStyle}>{t("noRowsYet")}</div>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            {rows.map((r) => (
              <div key={r.productStockId} style={selectedRowStyle}>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
                    {r.productName}
                  </div>
                  <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
                    {r.batchNumber ? `${r.batchNumber} · ` : ""}
                    {new Date(r.expiryDate).toLocaleDateString(intlLocale)}
                  </div>
                </div>
                <input
                  type="number"
                  min={0}
                  max={r.availableQty}
                  step="any"
                  value={r.quantity}
                  onChange={(e) => setRowQuantity(r.productStockId, e.target.value)}
                  placeholder={t("quantityPlaceholder")}
                  style={{ ...inputStyle, width: 100 }}
                />
                <button
                  type="button"
                  onClick={() => removeRow(r.productStockId)}
                  aria-label={t("removeItem")}
                  style={removeBtnStyle}
                >
                  ×
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      <div>
        <label style={labelStyle}>{t("notesLabel")}</label>
        <textarea
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          placeholder={t("notesPlaceholder")}
          style={{ ...inputStyle, minHeight: 60, resize: "vertical", fontFamily: "inherit" }}
        />
      </div>

      {error && <div style={errorBoxStyle}>{error}</div>}

      <div style={{ display: "flex", gap: 10, justifyContent: "flex-end", marginTop: 4 }}>
        <Btn variant="ghost" type="button" onClick={onCancel}>
          {tCommon("cancel")}
        </Btn>
        <Btn type="submit" disabled={create.isPending || rows.length === 0 || !toStoreId}>
          {create.isPending ? t("saving") : t("submit")}
        </Btn>
      </div>
    </form>
  );
}
