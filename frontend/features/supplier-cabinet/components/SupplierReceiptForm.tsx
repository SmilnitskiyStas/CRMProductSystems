"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { toast } from "sonner";
import { Plus, X } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { useCabinetItems } from "../hooks/useSupplierCabinet";
import {
  useSupplierReceipt,
  useCreateReceipt,
  useUpdateReceipt,
  useAddReceiptLine,
  useRemoveReceiptLine,
  useFinalizeReceipt,
} from "../hooks/useSupplierInventory";
import type { SupplierStockReceiptStatus } from "../types";

interface Props {
  /** Used only when creating a new draft. */
  warehouseId: string;
  warehouseName: string;
  /** Resume / view an existing receipt. Null = create a new draft. */
  receiptId?: string | null;
  onClose: () => void;
}

interface PendingRow {
  rowId: string;
  supplierItemId: string;
  supplierItemName: string;
  quantity: string;
  expiryDate: string;
  batchNumber: string;
  unitCost: string;
  notes: string;
  rowError?: string;
}

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

const compactInputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0A0F1A",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 12,
  padding: "6px 8px",
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

const miniLabelStyle: React.CSSProperties = {
  display: "block",
  color: "#6B7280",
  fontSize: 10,
  marginBottom: 3,
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

export function SupplierReceiptForm({ warehouseId, warehouseName, receiptId = null, onClose }: Props) {
  const t = useTranslations("Dashboard.supplierCabinet.receiptForm");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const [draftId, setDraftId] = useState<string | null>(receiptId);
  const [reference, setReference] = useState("");
  const [notes, setNotes] = useState("");
  const [pendingRows, setPendingRows] = useState<PendingRow[]>([]);
  const [productQuery, setProductQuery] = useState("");
  const [error, setError] = useState<string | null>(null);
  const headerInitialised = useRef(false);

  const { data: receipt, isLoading: receiptLoading } = useSupplierReceipt(draftId);
  const { data: catalogItems = [] } = useCabinetItems();

  const createReceipt = useCreateReceipt();
  const updateReceipt = useUpdateReceipt();
  const addLine = useAddReceiptLine();
  const removeLine = useRemoveReceiptLine();
  const finalize = useFinalizeReceipt();

  const isDraft = !receipt || receipt.status === "draft";
  const readOnly = Boolean(receipt) && receipt!.status !== "draft";

  useEffect(() => {
    if (receipt && !headerInitialised.current) {
      setReference(receipt.reference ?? "");
      setNotes(receipt.notes ?? "");
      headerInitialised.current = true;
    }
  }, [receipt]);

  const filteredCatalog = useMemo(() => {
    const q = productQuery.trim().toLowerCase();
    const list = catalogItems.map((it) => ({
      id: it.id,
      name: it.customName || it.itemName || it.id,
    }));
    return (q ? list.filter((it) => it.name.toLowerCase().includes(q)) : list).slice(0, 40);
  }, [catalogItems, productQuery]);

  function addPendingRow(supplierItemId: string, supplierItemName: string) {
    setPendingRows((prev) => [
      ...prev,
      {
        rowId:
          typeof crypto !== "undefined" && crypto.randomUUID
            ? crypto.randomUUID()
            : `row-${Date.now()}-${Math.random()}`,
        supplierItemId,
        supplierItemName,
        quantity: "",
        expiryDate: "",
        batchNumber: "",
        unitCost: "",
        notes: "",
      },
    ]);
  }

  function setRowField(rowId: string, field: keyof PendingRow, value: string) {
    setPendingRows((prev) =>
      prev.map((r) => (r.rowId === rowId ? { ...r, [field]: value, rowError: undefined } : r)),
    );
  }

  function dropPendingRow(rowId: string) {
    setPendingRows((prev) => prev.filter((r) => r.rowId !== rowId));
  }

  async function handleCreateDraft() {
    setError(null);
    try {
      const created = await createReceipt.mutateAsync({
        warehouseId,
        body: { reference: reference.trim() || null, notes: notes.trim() || null },
      });
      headerInitialised.current = true;
      setDraftId(created.id);
    } catch (err) {
      setError((err as Error)?.message ?? t("errorDefault"));
    }
  }

  async function handleSaveHeader() {
    if (!draftId || !receipt) return;
    setError(null);
    try {
      await updateReceipt.mutateAsync({
        id: draftId,
        body: {
          warehouseId: receipt.warehouseId,
          reference: reference.trim() || null,
          notes: notes.trim() || null,
        },
      });
      toast.success(t("headerSaved"));
    } catch (err) {
      setError((err as Error)?.message ?? t("errorDefault"));
    }
  }

  async function handlePersistRow(row: PendingRow) {
    if (!draftId) return;
    const qty = parseFloat(row.quantity);
    if (isNaN(qty) || qty <= 0) {
      setPendingRows((prev) =>
        prev.map((r) => (r.rowId === row.rowId ? { ...r, rowError: t("rowQuantityRequired") } : r)),
      );
      return;
    }
    try {
      await addLine.mutateAsync({
        id: draftId,
        body: {
          supplierItemId: row.supplierItemId,
          expiryDate: row.expiryDate || null,
          quantity: qty,
          batchNumber: row.batchNumber.trim() || null,
          unitCost: row.unitCost ? parseFloat(row.unitCost) : null,
          notes: row.notes.trim() || null,
        },
      });
      dropPendingRow(row.rowId);
    } catch (err) {
      const msg = (err as Error)?.message ?? t("errorDefault");
      setPendingRows((prev) =>
        prev.map((r) => (r.rowId === row.rowId ? { ...r, rowError: msg } : r)),
      );
    }
  }

  async function handleRemoveSavedLine(lineId: string) {
    if (!draftId) return;
    setError(null);
    try {
      await removeLine.mutateAsync({ id: draftId, lineId });
    } catch (err) {
      setError((err as Error)?.message ?? t("errorDefault"));
    }
  }

  async function handleFinalize() {
    if (!draftId || !receipt) return;
    setError(null);
    if (pendingRows.length > 0) {
      setError(t("finalizeUnsavedRows"));
      return;
    }
    if (receipt.items.length === 0) {
      setError(t("finalizeNoItems"));
      return;
    }
    try {
      await finalize.mutateAsync(draftId);
      toast.success(t("finalizeSuccess"));
      onClose();
    } catch (err) {
      // 400 { error } — the message names how many lines are missing an expiry date.
      setError((err as Error)?.message ?? t("errorDefault"));
    }
  }

  const savedLines = receipt?.items ?? [];
  const num = (v: number) => v.toLocaleString(intlLocale, { maximumFractionDigits: 3 });

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
          width: "min(780px, 96vw)",
          maxHeight: "90vh",
          overflowY: "auto",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 14,
          zIndex: 301,
        }}
      >
        <div
          style={{
            position: "sticky",
            top: 0,
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "18px 22px",
            borderBottom: "1px solid #1F2937",
            background: "#0D1117",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
              {draftId ? t("editTitle") : t("createTitle")}
            </h2>
            {receipt && <StatusPill status={receipt.status} />}
          </div>
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

        <div style={{ padding: 22, display: "flex", flexDirection: "column", gap: 16 }}>
          <div style={{ color: "#6B7280", fontSize: 12 }}>
            {t("warehouseLabel", { name: receipt?.warehouseName || warehouseName })}
          </div>

          {/* Header: reference + notes */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <div>
              <label style={labelStyle}>{t("referenceLabel")}</label>
              <input
                value={reference}
                onChange={(e) => setReference(e.target.value)}
                placeholder={t("referencePlaceholder")}
                style={inputStyle}
                disabled={readOnly}
              />
            </div>
            <div>
              <label style={labelStyle}>{t("notesLabel")}</label>
              <input
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder={t("notesPlaceholder")}
                style={inputStyle}
                disabled={readOnly}
              />
            </div>
          </div>

          {!draftId && (
            <div style={{ display: "flex", gap: 10 }}>
              <Btn
                onClick={handleCreateDraft}
                disabled={createReceipt.isPending}
                style={{ justifyContent: "center" }}
              >
                {createReceipt.isPending ? t("saving") : t("createDraftButton")}
              </Btn>
              <Btn variant="ghost" onClick={onClose}>
                {t("cancel")}
              </Btn>
            </div>
          )}

          {draftId && isDraft && (
            <div>
              <Btn
                size="sm"
                variant="ghost"
                onClick={handleSaveHeader}
                disabled={updateReceipt.isPending}
              >
                {t("saveHeaderButton")}
              </Btn>
            </div>
          )}

          {draftId && receiptLoading && !receipt && (
            <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
          )}

          {receipt && (
            <>
              {/* Saved lines */}
              <div>
                <label style={labelStyle}>{t("linesSectionTitle", { count: savedLines.length })}</label>
                {savedLines.length === 0 ? (
                  <div style={{ color: "#4B5563", fontSize: 12, padding: "8px 0" }}>{t("noLinesYet")}</div>
                ) : (
                  <div
                    style={{
                      border: "1px solid #1F2937",
                      borderRadius: 8,
                      overflow: "hidden",
                    }}
                  >
                    {savedLines.map((line) => (
                      <div
                        key={line.id}
                        style={{
                          display: "grid",
                          gridTemplateColumns: "1.6fr 1fr 0.8fr 1fr 0.8fr auto",
                          gap: 8,
                          alignItems: "center",
                          padding: "9px 12px",
                          borderBottom: "1px solid #131C2B",
                          fontSize: 12,
                        }}
                      >
                        <span style={{ color: "#E8EDF5", fontWeight: 500 }}>{line.supplierItemName}</span>
                        <span style={{ color: line.expiryDate ? "#9CA3AF" : "#FBBF24" }}>
                          {line.expiryDate
                            ? new Date(line.expiryDate).toLocaleDateString(intlLocale)
                            : t("expiryMissing")}
                        </span>
                        <span style={{ color: "#9CA3AF" }}>{num(line.quantity)}</span>
                        <span style={{ color: "#6B7280" }}>{line.batchNumber || "—"}</span>
                        <span style={{ color: "#6B7280" }}>
                          {line.unitCost != null ? num(line.unitCost) : "—"}
                        </span>
                        <span style={{ display: "flex", gap: 4, justifyContent: "flex-end" }}>
                          {isDraft && (
                            <>
                              <button
                                type="button"
                                title={t("addBatchForItem")}
                                onClick={() => addPendingRow(line.supplierItemId, line.supplierItemName)}
                                style={{
                                  background: "transparent",
                                  border: "1px solid #1F2937",
                                  borderRadius: 6,
                                  color: "#6B7280",
                                  cursor: "pointer",
                                  padding: "3px 6px",
                                  display: "flex",
                                  alignItems: "center",
                                }}
                              >
                                <Plus size={13} />
                              </button>
                              <button
                                type="button"
                                title={t("removeLine")}
                                onClick={() => handleRemoveSavedLine(line.id)}
                                disabled={removeLine.isPending}
                                style={{
                                  background: "transparent",
                                  border: "1px solid #1F2937",
                                  borderRadius: 6,
                                  color: "#F87171",
                                  cursor: "pointer",
                                  padding: "3px 6px",
                                  display: "flex",
                                  alignItems: "center",
                                }}
                              >
                                <X size={13} />
                              </button>
                            </>
                          )}
                        </span>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              {isDraft && (
                <>
                  {/* Product picker */}
                  <div>
                    <label style={labelStyle}>{t("addProductLabel")}</label>
                    <input
                      value={productQuery}
                      onChange={(e) => setProductQuery(e.target.value)}
                      placeholder={t("productSearchPlaceholder")}
                      style={{ ...inputStyle, marginBottom: 8 }}
                    />
                    <div
                      style={{
                        maxHeight: 160,
                        overflowY: "auto",
                        border: "1px solid #1F2937",
                        borderRadius: 8,
                      }}
                    >
                      {filteredCatalog.length === 0 ? (
                        <div style={{ color: "#4B5563", fontSize: 12, padding: "10px 12px" }}>
                          {t("productSearchEmpty")}
                        </div>
                      ) : (
                        filteredCatalog.map((it) => (
                          <div
                            key={it.id}
                            onClick={() => addPendingRow(it.id, it.name)}
                            style={{
                              padding: "8px 12px",
                              borderBottom: "1px solid #131C2B",
                              color: "#E8EDF5",
                              fontSize: 13,
                              cursor: "pointer",
                            }}
                          >
                            {it.name}
                          </div>
                        ))
                      )}
                    </div>
                  </div>

                  {/* Pending (unsaved) rows */}
                  {pendingRows.length > 0 && (
                    <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
                      {pendingRows.map((row) => (
                        <div
                          key={row.rowId}
                          style={{
                            background: "#111827",
                            border: "1px solid #1F2937",
                            borderRadius: 8,
                            padding: "10px 12px",
                          }}
                        >
                          <div
                            style={{
                              display: "flex",
                              alignItems: "center",
                              justifyContent: "space-between",
                              gap: 10,
                              marginBottom: 8,
                            }}
                          >
                            <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
                              {row.supplierItemName}
                            </span>
                            <div style={{ display: "flex", gap: 6 }}>
                              <button
                                type="button"
                                onClick={() => addPendingRow(row.supplierItemId, row.supplierItemName)}
                                style={{
                                  background: "transparent",
                                  border: "1px solid #1F2937",
                                  borderRadius: 6,
                                  color: "#6B7280",
                                  fontSize: 11,
                                  cursor: "pointer",
                                  padding: "3px 8px",
                                }}
                              >
                                {t("addBatchButton")}
                              </button>
                              <button
                                type="button"
                                onClick={() => dropPendingRow(row.rowId)}
                                aria-label={t("removeLine")}
                                style={{
                                  background: "transparent",
                                  border: "none",
                                  color: "#6B7280",
                                  fontSize: 16,
                                  lineHeight: 1,
                                  cursor: "pointer",
                                  padding: 2,
                                }}
                              >
                                ×
                              </button>
                            </div>
                          </div>
                          <div
                            style={{
                              display: "grid",
                              gridTemplateColumns: "0.8fr 1fr 1fr 0.8fr 1.2fr",
                              gap: 8,
                            }}
                          >
                            <div>
                              <label style={miniLabelStyle}>{t("rowQuantity")}</label>
                              <input
                                type="number"
                                step="any"
                                value={row.quantity}
                                onChange={(e) => setRowField(row.rowId, "quantity", e.target.value)}
                                style={compactInputStyle}
                              />
                            </div>
                            <div>
                              <label style={miniLabelStyle}>{t("rowExpiry")}</label>
                              <input
                                type="date"
                                value={row.expiryDate}
                                onChange={(e) => setRowField(row.rowId, "expiryDate", e.target.value)}
                                style={compactInputStyle}
                              />
                            </div>
                            <div>
                              <label style={miniLabelStyle}>{t("rowBatch")}</label>
                              <input
                                type="text"
                                value={row.batchNumber}
                                onChange={(e) => setRowField(row.rowId, "batchNumber", e.target.value)}
                                style={compactInputStyle}
                              />
                            </div>
                            <div>
                              <label style={miniLabelStyle}>{t("rowUnitCost")}</label>
                              <input
                                type="number"
                                step="any"
                                value={row.unitCost}
                                onChange={(e) => setRowField(row.rowId, "unitCost", e.target.value)}
                                style={compactInputStyle}
                              />
                            </div>
                            <div>
                              <label style={miniLabelStyle}>{t("rowNotes")}</label>
                              <input
                                type="text"
                                value={row.notes}
                                onChange={(e) => setRowField(row.rowId, "notes", e.target.value)}
                                style={compactInputStyle}
                              />
                            </div>
                          </div>
                          <div style={{ display: "flex", alignItems: "center", gap: 10, marginTop: 8 }}>
                            <Btn
                              size="sm"
                              onClick={() => handlePersistRow(row)}
                              disabled={addLine.isPending}
                            >
                              {t("addLineButton")}
                            </Btn>
                            {row.rowError && (
                              <span style={{ color: "#F87171", fontSize: 11 }}>{row.rowError}</span>
                            )}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </>
              )}

              {error && (
                <div
                  style={{
                    background: "#2D0F0F",
                    border: "1px solid #7F1D1D",
                    borderRadius: 8,
                    color: "#F87171",
                    fontSize: 12,
                    padding: "8px 12px",
                  }}
                >
                  {error}
                </div>
              )}

              <div style={{ display: "flex", gap: 10, justifyContent: "flex-end" }}>
                <Btn variant="ghost" onClick={onClose}>
                  {readOnly ? t("close") : t("cancel")}
                </Btn>
                {isDraft && (
                  <Btn onClick={handleFinalize} disabled={finalize.isPending}>
                    {finalize.isPending ? t("saving") : t("finalizeButton")}
                  </Btn>
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </>
  );
}
