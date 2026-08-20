"use client";

import { useEffect, useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useSuppliers } from "@/features/suppliers/hooks/useSuppliers";
import { useCatalogProducts } from "@/features/catalog/hooks/useCatalog";
import type { CatalogProductDto } from "@/features/catalog/types";
import { useCreateReceipt } from "../hooks/useReceipts";
import { Btn } from "@/components/ui/Btn";

interface Props {
  onSuccess: () => void;
  onCancel: () => void;
}

interface ReceiptRow {
  productId: string;
  productName: string;
  productBarcode: string | null;
  quantityOrdered: string; // controlled input, parseFloat on submit
  pricePurchase: string; // controlled input, optional, parseFloat on submit or omit if blank
  expiryDate: string; // "" or "YYYY-MM-DD", optional
  batchNumber: string; // optional free text
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

const productListStyle: React.CSSProperties = {
  maxHeight: 220,
  overflowY: "auto",
  border: "1px solid #1F2937",
  borderRadius: 8,
};

const productRowStyle: React.CSSProperties = {
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

const checkboxRowStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: 8,
  height: "100%",
  paddingTop: 20,
};

const errorBoxStyle: React.CSSProperties = {
  background: "#2D0F0F",
  border: "1px solid #7F1D1D",
  borderRadius: 8,
  color: "#F87171",
  fontSize: 12,
  padding: "8px 12px",
};

export function CreateReceiptForm({ onSuccess, onCancel }: Props) {
  const t = useTranslations("Dashboard.receipts.createForm");
  const tCommon = useTranslations("Common");

  const { data: locations = [] } = useLocations();
  const { data: suppliers = [] } = useSuppliers();
  const create = useCreateReceipt();

  const [destinationStoreId, setDestinationStoreId] = useState("");
  const [supplierId, setSupplierId] = useState("");
  const [viaCentralStore, setViaCentralStore] = useState(false);
  const [expectedAt, setExpectedAt] = useState("");
  const [notes, setNotes] = useState("");
  const [productQuery, setProductQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [rows, setRows] = useState<ReceiptRow[]>([]);
  const [error, setError] = useState<string | null>(null);

  // Debounced (~300ms) search box — same setTimeout-in-useEffect pattern as
  // ProductPickerField.tsx's own search debounce.
  useEffect(() => {
    const handle = setTimeout(() => setDebouncedQuery(productQuery.trim()), 300);
    return () => clearTimeout(handle);
  }, [productQuery]);

  const { data: searchResults = [], isLoading: isSearching } = useCatalogProducts({
    search: debouncedQuery || undefined,
  });

  const candidates = useMemo(
    () => searchResults.filter((p) => p.isActive),
    [searchResults],
  );

  function isRowAdded(productId: string) {
    return rows.some((r) => r.productId === productId);
  }

  function addRow(product: CatalogProductDto) {
    if (isRowAdded(product.id)) return;
    setRows((prev) => [
      ...prev,
      {
        productId: product.id,
        productName: product.name,
        productBarcode: product.barcode,
        quantityOrdered: "",
        pricePurchase: product.pricePurchase != null ? String(product.pricePurchase) : "",
        expiryDate: "",
        batchNumber: "",
      },
    ]);
  }

  function removeRow(productId: string) {
    setRows((prev) => prev.filter((r) => r.productId !== productId));
  }

  function setRowField(
    productId: string,
    field: "quantityOrdered" | "pricePurchase" | "expiryDate" | "batchNumber",
    value: string,
  ) {
    setRows((prev) =>
      prev.map((r) => (r.productId === productId ? { ...r, [field]: value } : r)),
    );
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (!destinationStoreId) {
      setError(t("errorNoDestination"));
      return;
    }
    if (rows.length === 0) {
      setError(t("errorNoItems"));
      return;
    }
    for (const row of rows) {
      const qty = parseFloat(row.quantityOrdered);
      if (isNaN(qty) || qty <= 0) {
        setError(t("errorQuantityRequired", { product: row.productName }));
        return;
      }
    }

    try {
      await create.mutateAsync({
        supplierId: supplierId || null,
        destinationStoreId,
        viaCentralStore,
        expectedAt: expectedAt || null,
        notes: notes.trim() || undefined,
        items: rows.map((r) => ({
          productId: r.productId,
          quantityOrdered: parseFloat(r.quantityOrdered),
          pricePurchase: r.pricePurchase ? parseFloat(r.pricePurchase) : null,
          expiryDate: r.expiryDate || null,
          batchNumber: r.batchNumber || null,
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
          <label style={labelStyle}>{t("supplierLabel")}</label>
          <select
            value={supplierId}
            onChange={(e) => setSupplierId(e.target.value)}
            style={inputStyle}
          >
            <option value="">{t("supplierPlaceholder")}</option>
            {suppliers.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label style={labelStyle}>{t("destinationStoreLabel")}</label>
          <select
            value={destinationStoreId}
            onChange={(e) => setDestinationStoreId(e.target.value)}
            style={inputStyle}
          >
            <option value="">{t("destinationStorePlaceholder")}</option>
            {locations.map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
        <div>
          <label style={labelStyle}>{t("expectedAtLabel")}</label>
          <input
            type="date"
            value={expectedAt}
            onChange={(e) => setExpectedAt(e.target.value)}
            style={inputStyle}
          />
        </div>

        <div style={checkboxRowStyle}>
          <input
            type="checkbox"
            id="viaCentralStore"
            checked={viaCentralStore}
            onChange={(e) => setViaCentralStore(e.target.checked)}
            style={{ width: 16, height: 16, cursor: "pointer" }}
          />
          <label
            htmlFor="viaCentralStore"
            style={{ color: "#9CA3AF", fontSize: 13, cursor: "pointer" }}
          >
            {t("viaCentralStoreLabel")}
          </label>
        </div>
      </div>

      {/* Product picker */}
      <div>
        <input
          type="text"
          value={productQuery}
          onChange={(e) => setProductQuery(e.target.value)}
          placeholder={t("productSearchPlaceholder")}
          style={{ ...inputStyle, marginBottom: 8 }}
        />
        <div style={productListStyle}>
          {!debouncedQuery ? (
            <div style={emptyHintStyle}>{t("productSearchHint")}</div>
          ) : isSearching ? (
            <div style={emptyHintStyle}>{tCommon("loading")}</div>
          ) : candidates.length === 0 ? (
            <div style={emptyHintStyle}>{t("productSearchEmpty")}</div>
          ) : (
            candidates.map((p) => {
              const added = isRowAdded(p.id);
              return (
                <div
                  key={p.id}
                  onClick={() => addRow(p)}
                  style={{
                    ...productRowStyle,
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
                      {p.name}
                    </div>
                    {p.barcode && (
                      <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
                        {p.barcode}
                      </div>
                    )}
                  </div>
                  {added && <span style={badgeStyle}>{t("alreadyAdded")}</span>}
                </div>
              );
            })
          )}
        </div>
      </div>

      {/* Selected rows */}
      <div>
        <label style={labelStyle}>{t("itemsSectionTitle", { count: rows.length })}</label>
        {rows.length === 0 ? (
          <div style={emptyHintStyle}>{t("noRowsYet")}</div>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            {rows.map((r) => (
              <div key={r.productId} style={selectedRowStyle}>
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
                    {r.productBarcode && (
                      <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
                        {r.productBarcode}
                      </div>
                    )}
                  </div>
                  <button
                    type="button"
                    onClick={() => removeRow(r.productId)}
                    aria-label={t("removeItem")}
                    style={removeBtnStyle}
                  >
                    ×
                  </button>
                </div>
                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "1fr 1fr 1fr 1fr",
                    gap: 8,
                    marginTop: 8,
                  }}
                >
                  <div>
                    <label style={miniLabelStyle}>{t("quantityLabel")}</label>
                    <input
                      type="number"
                      step="any"
                      value={r.quantityOrdered}
                      onChange={(e) => setRowField(r.productId, "quantityOrdered", e.target.value)}
                      placeholder={t("quantityPlaceholder")}
                      style={compactInputStyle}
                    />
                  </div>
                  <div>
                    <label style={miniLabelStyle}>{t("priceLabel")}</label>
                    <input
                      type="number"
                      step="any"
                      value={r.pricePurchase}
                      onChange={(e) => setRowField(r.productId, "pricePurchase", e.target.value)}
                      placeholder={t("pricePlaceholder")}
                      style={compactInputStyle}
                    />
                  </div>
                  <div>
                    <label style={miniLabelStyle}>{t("expiryLabel")}</label>
                    <input
                      type="date"
                      value={r.expiryDate}
                      onChange={(e) => setRowField(r.productId, "expiryDate", e.target.value)}
                      style={compactInputStyle}
                    />
                  </div>
                  <div>
                    <label style={miniLabelStyle}>{t("batchLabel")}</label>
                    <input
                      type="text"
                      value={r.batchNumber}
                      onChange={(e) => setRowField(r.productId, "batchNumber", e.target.value)}
                      placeholder={t("batchPlaceholder")}
                      style={compactInputStyle}
                    />
                  </div>
                </div>
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
        <Btn
          type="submit"
          disabled={create.isPending || rows.length === 0 || !destinationStoreId}
        >
          {create.isPending ? t("saving") : t("submit")}
        </Btn>
      </div>
    </form>
  );
}
