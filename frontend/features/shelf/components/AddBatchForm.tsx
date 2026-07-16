"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useCreateStock } from "../hooks/useStock";
import type { CreateStockRequest } from "../types";
import { Btn } from "@/components/ui/Btn";

interface StoreOption {
  id: string;
  name: string;
  zones: { id: string; name: string }[];
}

interface ProductOption {
  id: string;
  name: string;
  barcode: string | null;
}

interface Props {
  stores: StoreOption[];
  products: ProductOption[];
  onSuccess: () => void;
  onCancel: () => void;
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

export function AddBatchForm({ stores, products, onSuccess, onCancel }: Props) {
  const t = useTranslations("Dashboard.shelf.addBatchForm");
  const tCommon = useTranslations("Common");
  const create = useCreateStock();

  const [form, setForm] = useState<{
    productId: string;
    storeId: string;
    zoneId: string;
    quantity: string;
    expiryDate: string;
    batchNumber: string;
    shelfNumber: string;
  }>({
    productId: "",
    storeId: "",
    zoneId: "",
    quantity: "",
    expiryDate: "",
    batchNumber: "",
    shelfNumber: "",
  });

  const [error, setError] = useState<string | null>(null);

  const selectedStore = stores.find((s) => s.id === form.storeId);

  function set(key: keyof typeof form, val: string) {
    setForm((prev) => ({ ...prev, [key]: val }));
    if (key === "storeId") setForm((prev) => ({ ...prev, storeId: val, zoneId: "" }));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (!form.productId || !form.storeId || !form.quantity || !form.expiryDate) {
      setError(t("errorRequired"));
      return;
    }

    const qty = parseFloat(form.quantity);
    if (isNaN(qty) || qty <= 0) {
      setError(t("errorQuantity"));
      return;
    }

    const data: CreateStockRequest = {
      productId: form.productId,
      storeId: form.storeId,
      zoneId: form.zoneId || null,
      quantity: qty,
      expiryDate: form.expiryDate,
      batchNumber: form.batchNumber || null,
      shelfNumber: form.shelfNumber ? parseInt(form.shelfNumber) : null,
    };

    try {
      await create.mutateAsync(data);
      onSuccess();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t("errorSaveFailed"));
    }
  }

  return (
    <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      <div>
        <label style={labelStyle}>{t("productLabel")}</label>
        <select
          value={form.productId}
          onChange={(e) => set("productId", e.target.value)}
          style={inputStyle}
          required
        >
          <option value="">{t("productPlaceholder")}</option>
          {products.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name} {p.barcode ? `(${p.barcode})` : ""}
            </option>
          ))}
        </select>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
        <div>
          <label style={labelStyle}>{t("storeLabel")}</label>
          <select
            value={form.storeId}
            onChange={(e) => {
              setForm((prev) => ({ ...prev, storeId: e.target.value, zoneId: "" }));
            }}
            style={inputStyle}
            required
          >
            <option value="">{t("storePlaceholder")}</option>
            {stores.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label style={labelStyle}>{t("zoneLabel")}</label>
          <select
            value={form.zoneId}
            onChange={(e) => set("zoneId", e.target.value)}
            style={inputStyle}
            disabled={!selectedStore}
          >
            <option value="">{t("zoneNone")}</option>
            {selectedStore?.zones.map((z) => (
              <option key={z.id} value={z.id}>
                {z.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
        <div>
          <label style={labelStyle}>{t("quantityLabel")}</label>
          <input
            type="number"
            min="0.01"
            step="any"
            value={form.quantity}
            onChange={(e) => set("quantity", e.target.value)}
            placeholder="0"
            style={inputStyle}
            required
          />
        </div>

        <div>
          <label style={labelStyle}>{t("expiryDateLabel")}</label>
          <input
            type="date"
            value={form.expiryDate}
            onChange={(e) => set("expiryDate", e.target.value)}
            style={inputStyle}
            required
          />
        </div>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
        <div>
          <label style={labelStyle}>{t("batchNumberLabel")}</label>
          <input
            type="text"
            value={form.batchNumber}
            onChange={(e) => set("batchNumber", e.target.value)}
            placeholder={t("batchNumberPlaceholder")}
            style={inputStyle}
          />
        </div>

        <div>
          <label style={labelStyle}>{t("shelfNumberLabel")}</label>
          <input
            type="number"
            min="1"
            step="1"
            value={form.shelfNumber}
            onChange={(e) => set("shelfNumber", e.target.value)}
            placeholder={t("shelfNumberPlaceholder")}
            style={inputStyle}
          />
        </div>
      </div>

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

      <div style={{ display: "flex", gap: 10, justifyContent: "flex-end", marginTop: 4 }}>
        <Btn variant="ghost" type="button" onClick={onCancel}>
          {tCommon("cancel")}
        </Btn>
        <Btn type="submit" disabled={create.isPending}>
          {create.isPending ? t("saving") : t("submit")}
        </Btn>
      </div>
    </form>
  );
}
