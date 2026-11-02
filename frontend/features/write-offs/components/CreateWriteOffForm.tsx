"use client";

import { useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useStock } from "@/features/shelf/hooks/useStock";
import type { BatchStatus, ProductStockDto } from "@/features/shelf/types";
import { WRITE_OFF_REASON_VALUES } from "../types";
import type { ReimbursementType } from "../types";
import { useCreateWriteOff } from "../hooks/useWriteOffs";
import { Btn } from "@/components/ui/Btn";
import { canWriteOffBatch } from "@/features/shelf/lib/stockWriteOff";
import type { WriteOffDto } from "../types";

interface Props {
  onSuccess: (writeOff: WriteOffDto) => void;
  initialBatches?: ProductStockDto[];
  onCancel: () => void;
}

interface WriteOffRow {
  productStockId: string; // = ProductStockDto.id — keeps two batches of the same product distinct
  productId: string;
  productName: string;
  batchNumber: string | null;
  expiryDate: string;
  daysLeft: number;
  availableQty: number;
  quantity: string; // controlled input, parseFloat on submit
  unitPrice: string; // controlled input, auto-filled from batch.priceRetail but still editable
  unitPricePurchase: number; // display-only snapshot from batch.pricePurchase
  isReturnedToSupplier: boolean;
  reimbursementType: ReimbursementType | ""; // pre-filled from item default, editable once checked
  reimbursementValue: string; // controlled input, parseFloat on submit
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

const compactInputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0A0F1A",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 12,
  padding: "5px 8px",
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  marginBottom: 5,
};

const miniLabelStyle: React.CSSProperties = {
  display: "block",
  color: "#6B7280",
  fontSize: 10,
  marginBottom: 3,
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
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "10px 12px",
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

const CRITICAL_STATUSES: BatchStatus[] = ["critical", "expired"];

const checkboxLabelStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: 6,
  color: "#9CA3AF",
  fontSize: 12,
  cursor: "pointer",
  marginBottom: 8,
};

const errorBoxStyle: React.CSSProperties = {
  background: "#2D0F0F",
  border: "1px solid #7F1D1D",
  borderRadius: 8,
  color: "#F87171",
  fontSize: 12,
  padding: "8px 12px",
};

function rowFromBatch(batch: ProductStockDto, quantity = ""): WriteOffRow {
  return {
    productStockId: batch.id,
    productId: batch.productId,
    productName: batch.productName,
    batchNumber: batch.batchNumber,
    expiryDate: batch.expiryDate,
    daysLeft: batch.daysLeft,
    availableQty: batch.quantity,
    quantity,
    unitPrice: batch.priceRetail != null ? String(batch.priceRetail) : "",
    unitPricePurchase: batch.pricePurchase ?? 0,
    isReturnedToSupplier: false,
    reimbursementType: batch.defaultReimbursementType ?? "",
    reimbursementValue: batch.defaultReimbursementValue != null ? String(batch.defaultReimbursementValue) : "",
  };
}

export function CreateWriteOffForm({ onSuccess, onCancel, initialBatches = [] }: Props) {
  const t = useTranslations("Dashboard.writeOffs.createForm");
  const tReason = useTranslations("Dashboard.writeOffs.reason");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { data: locations = [] } = useLocations();
  const create = useCreateWriteOff();

  const [storeId, setStoreId] = useState(initialBatches[0]?.storeId ?? "");
  const [reason, setReason] = useState(initialBatches.length ? (initialBatches.every(batch => batch.daysLeft <= 0) ? "expired" : "other") : "");
  const [notes, setNotes] = useState("");
  const [batchFilterQuery, setBatchFilterQuery] = useState("");
  const [criticalOnly, setCriticalOnly] = useState(false);
  const [rows, setRows] = useState<WriteOffRow[]>(() => initialBatches
    .filter(batch => canWriteOffBatch(batch) && batch.storeId === initialBatches[0]?.storeId)
    .map(batch => rowFromBatch(batch, String(batch.quantity))));
  const [error, setError] = useState<string | null>(null);

  const { data: stockPage, isLoading: isLoadingBatches } = useStock(
    { store_id: storeId },
    !!storeId,
  );
  const stockBatches = useMemo(() => stockPage?.items ?? [], [stockPage]);

  const addedIds = useMemo(() => new Set(rows.map((r) => r.productStockId)), [rows]);

  const availableBatches = useMemo(
    () =>
      [...stockBatches]
        .filter((b) => b.storeId === storeId && canWriteOffBatch(b))
        .filter((b) => !addedIds.has(b.id))
        .filter((b) => !criticalOnly || CRITICAL_STATUSES.includes(b.status))
        .sort((a, b) => a.expiryDate.localeCompare(b.expiryDate)),
    [stockBatches, addedIds, criticalOnly, storeId],
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

  function setStore(id: string) {
    setStoreId(id);
    setRows([]);
    setBatchFilterQuery("");
    setError(null);
    setNotes("");
  }

  function addRow(batch: ProductStockDto) {
    if (isRowAdded(batch.id)) return;
    setRows((prev) => [
      ...prev,
      rowFromBatch(batch),
    ]);
  }

  function removeRow(productStockId: string) {
    setRows((prev) => prev.filter((r) => r.productStockId !== productStockId));
  }

  function setRowField(
    productStockId: string,
    field: "quantity" | "unitPrice" | "reimbursementType" | "reimbursementValue",
    value: string,
  ) {
    setRows((prev) =>
      prev.map((r) => (r.productStockId === productStockId ? { ...r, [field]: value } : r)),
    );
  }

  function setRowReturnedToSupplier(productStockId: string, value: boolean) {
    setRows((prev) =>
      prev.map((r) =>
        r.productStockId === productStockId ? { ...r, isReturnedToSupplier: value } : r,
      ),
    );
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (!storeId) {
      setError(t("errorNoStore"));
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

    if (!reason) { setError(t("errorNoReason")); return; }
    const explanation = notes.trim();
    if (reason !== "expired" && (explanation.length < 15 || explanation.length > 100)) {
      setError(t("errorExplanationLength"));
      return;
    }

    try {
      const writeOff = await create.mutateAsync({
        storeId,
        reason: reason || null,
        notes: reason === "expired" ? null : explanation,
        items: rows.map((r) => ({
          productStockId: r.productStockId,
          productId: r.productId,
          quantity: parseFloat(r.quantity),
          unitPrice: r.unitPrice ? parseFloat(r.unitPrice) : null,
          isReturnedToSupplier: r.isReturnedToSupplier,
          reimbursementType: r.isReturnedToSupplier && r.reimbursementType ? r.reimbursementType : null,
          reimbursementValue:
            r.isReturnedToSupplier && r.reimbursementValue ? parseFloat(r.reimbursementValue) : null,
        })),
      });
      onSuccess(writeOff);
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
          <label style={labelStyle}>{t("storeLabel")}</label>
          <select
            value={storeId}
            onChange={(e) => setStore(e.target.value)}
            disabled={initialBatches.length > 0}
            style={inputStyle}
          >
            <option value="">{t("storePlaceholder")}</option>
            {initialBatches[0] && !locations.some(location => location.id === storeId) && (
              <option value={storeId}>{initialBatches[0].storeName}</option>
            )}
            {locations.map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label style={labelStyle}>{t("reasonLabel")}</label>
          <select
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            style={inputStyle}
          >
            <option value="">{t("reasonPlaceholder")}</option>
            {WRITE_OFF_REASON_VALUES.map((r) => (
              <option key={r} value={r}>
                {tReason(r)}
              </option>
            ))}
          </select>
        </div>
      </div>

      {reason === "expired" && rows.length > 0 && rows.every(row => row.daysLeft <= 0) && (
        <p role="status" style={{ color: "#FBBF24", fontSize: 12, margin: 0 }}>{t("expiryDetected")}</p>
      )}
      {reason && reason !== "expired" && (
        <div>
          <label htmlFor="writeoff-explanation" style={labelStyle}>{t("explanationLabel")} *</label>
          <textarea id="writeoff-explanation" value={notes} onChange={event => setNotes(event.target.value)}
            required minLength={15} maxLength={100} rows={3} aria-describedby="writeoff-explanation-hint"
            placeholder={t("explanationPlaceholder")} style={{ ...inputStyle, resize: "vertical" }} />
          <p id="writeoff-explanation-hint" style={{ color: "#9CA3AF", fontSize: 12, margin: "6px 0 0" }}>
            {t("explanationHint")} · {notes.trim().length}/100
          </p>
        </div>
      )}

      {/* Batch picker */}
      <div>
        {!storeId ? (
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
            <label style={checkboxLabelStyle}>
              <input
                type="checkbox"
                checked={criticalOnly}
                onChange={(e) => setCriticalOnly(e.target.checked)}
              />
              {t("criticalOnlyLabel")}
            </label>
            <div style={batchListStyle}>
              {isLoadingBatches ? (
                <div style={emptyHintStyle}>{tCommon("loading")}</div>
              ) : availableBatches.length === 0 ? (
                <div style={emptyHintStyle}>{t("noBatchesAtStore")}</div>
              ) : filteredBatches.length === 0 ? (
                <div style={emptyHintStyle}>{t("batchSearchEmpty")}</div>
              ) : (
                filteredBatches.map((b) => (
                  <div
                    key={b.id}
                    onClick={() => addRow(b)}
                    style={{ ...batchRowStyle, cursor: "pointer" }}
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
                    <span style={badgeStyle}>{t("availableLabel", { qty: b.quantity })}</span>
                  </div>
                ))
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
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    gap: 10,
                  }}
                >
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
                      {r.productName}
                    </div>
                    <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
                      {r.batchNumber ? `${r.batchNumber} · ` : ""}
                      {new Date(r.expiryDate).toLocaleDateString(intlLocale)}
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => removeRow(r.productStockId)}
                    aria-label={t("removeItem")}
                    style={removeBtnStyle}
                  >
                    ×
                  </button>
                </div>
                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "1fr 1fr",
                    gap: 8,
                    marginTop: 8,
                  }}
                >
                  <div>
                    <label style={miniLabelStyle}>{t("quantityLabel")}</label>
                    <input
                      type="number"
                      min={0}
                      max={r.availableQty}
                      step="any"
                      value={r.quantity}
                      onChange={(e) => setRowField(r.productStockId, "quantity", e.target.value)}
                      placeholder={t("quantityPlaceholder")}
                      style={compactInputStyle}
                    />
                  </div>
                  <div>
                    <label style={miniLabelStyle}>{t("unitPriceLabel")}</label>
                    <input
                      type="number"
                      min={0}
                      step="any"
                      value={r.unitPrice}
                      onChange={(e) => setRowField(r.productStockId, "unitPrice", e.target.value)}
                      placeholder={t("unitPricePlaceholder")}
                      style={compactInputStyle}
                    />
                  </div>
                </div>

                {(() => {
                  const qty = parseFloat(r.quantity) || 0;
                  const retailLoss = (parseFloat(r.unitPrice) || 0) * qty;
                  const purchaseLoss = r.unitPricePurchase * qty;
                  const reimbursementValueNum = parseFloat(r.reimbursementValue) || 0;
                  const reimbursementAmount =
                    r.reimbursementType === "fixed"
                      ? reimbursementValueNum * qty
                      : r.reimbursementType === "percent"
                        ? purchaseLoss * (reimbursementValueNum / 100)
                        : 0;

                  return (
                    <>
                      <div
                        style={{
                          display: "grid",
                          gridTemplateColumns: "1fr 1fr",
                          gap: 8,
                          marginTop: 8,
                        }}
                      >
                        <div>
                          <span style={miniLabelStyle}>{t("retailLossLabel")}</span>
                          <div style={{ color: "#F87171", fontSize: 12, fontFamily: "monospace" }}>
                            {retailLoss.toFixed(2)} ₴
                          </div>
                        </div>
                        <div>
                          <span style={miniLabelStyle}>{t("purchaseLossLabel")}</span>
                          <div style={{ color: "#FBBF24", fontSize: 12, fontFamily: "monospace" }}>
                            {purchaseLoss.toFixed(2)} ₴
                          </div>
                          <div style={{ color: "#4B5563", fontSize: 10, marginTop: 1 }}>
                            {t("unitPricePurchaseLabel")}: {r.unitPricePurchase.toFixed(2)} ₴
                          </div>
                        </div>
                      </div>

                      <label
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: 6,
                          marginTop: 10,
                          color: "#9CA3AF",
                          fontSize: 12,
                          cursor: "pointer",
                        }}
                      >
                        <input
                          type="checkbox"
                          checked={r.isReturnedToSupplier}
                          onChange={(e) =>
                            setRowReturnedToSupplier(r.productStockId, e.target.checked)
                          }
                        />
                        {t("returnedToSupplierLabel")}
                      </label>

                      {r.isReturnedToSupplier && (
                        <div
                          style={{
                            display: "grid",
                            gridTemplateColumns: "1fr 1fr",
                            gap: 8,
                            marginTop: 8,
                          }}
                        >
                          <div>
                            <label style={miniLabelStyle}>{t("reimbursementTypeLabel")}</label>
                            <select
                              value={r.reimbursementType}
                              onChange={(e) =>
                                setRowField(r.productStockId, "reimbursementType", e.target.value)
                              }
                              style={compactInputStyle}
                            >
                              <option value="">—</option>
                              <option value="fixed">{t("reimbursementTypeFixed")}</option>
                              <option value="percent">{t("reimbursementTypePercent")}</option>
                            </select>
                          </div>
                          <div>
                            <label style={miniLabelStyle}>{t("reimbursementValueLabel")}</label>
                            <input
                              type="number"
                              min={0}
                              max={r.reimbursementType === "percent" ? 100 : undefined}
                              step="any"
                              value={r.reimbursementValue}
                              onChange={(e) =>
                                setRowField(r.productStockId, "reimbursementValue", e.target.value)
                              }
                              placeholder={t("reimbursementValuePlaceholder")}
                              style={compactInputStyle}
                            />
                          </div>
                          <div style={{ gridColumn: "1 / -1" }}>
                            <span style={miniLabelStyle}>{t("reimbursementAmountLabel")}</span>
                            <div style={{ color: "#4ADE80", fontSize: 12, fontFamily: "monospace" }}>
                              {reimbursementAmount.toFixed(2)} ₴
                            </div>
                          </div>
                        </div>
                      )}
                    </>
                  );
                })()}
              </div>
            ))}
          </div>
        )}
      </div>

      {error && <div role="alert" style={errorBoxStyle}>{error}</div>}

      <div style={{ display: "flex", gap: 10, justifyContent: "flex-end", marginTop: 4 }}>
        <Btn variant="ghost" type="button" onClick={onCancel}>
          {tCommon("cancel")}
        </Btn>
        <Btn type="submit" disabled={create.isPending || rows.length === 0 || !storeId}>
          {create.isPending ? t("saving") : t("submit")}
        </Btn>
      </div>
    </form>
  );
}
