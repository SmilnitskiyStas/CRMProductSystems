"use client";

import { useState } from "react";
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
      setError("Заповніть усі обов'язкові поля");
      return;
    }

    const qty = parseFloat(form.quantity);
    if (isNaN(qty) || qty <= 0) {
      setError("Кількість має бути більше 0");
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
      setError(err instanceof Error ? err.message : "Помилка збереження");
    }
  }

  return (
    <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      <div>
        <label style={labelStyle}>Товар *</label>
        <select
          value={form.productId}
          onChange={(e) => set("productId", e.target.value)}
          style={inputStyle}
          required
        >
          <option value="">Оберіть товар…</option>
          {products.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name} {p.barcode ? `(${p.barcode})` : ""}
            </option>
          ))}
        </select>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
        <div>
          <label style={labelStyle}>Магазин *</label>
          <select
            value={form.storeId}
            onChange={(e) => {
              setForm((prev) => ({ ...prev, storeId: e.target.value, zoneId: "" }));
            }}
            style={inputStyle}
            required
          >
            <option value="">Оберіть магазин…</option>
            {stores.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label style={labelStyle}>Зона</label>
          <select
            value={form.zoneId}
            onChange={(e) => set("zoneId", e.target.value)}
            style={inputStyle}
            disabled={!selectedStore}
          >
            <option value="">Без зони</option>
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
          <label style={labelStyle}>Кількість *</label>
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
          <label style={labelStyle}>Термін придатності *</label>
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
          <label style={labelStyle}>Номер партії</label>
          <input
            type="text"
            value={form.batchNumber}
            onChange={(e) => set("batchNumber", e.target.value)}
            placeholder="необов'язково"
            style={inputStyle}
          />
        </div>

        <div>
          <label style={labelStyle}>Полиця №</label>
          <input
            type="number"
            min="1"
            step="1"
            value={form.shelfNumber}
            onChange={(e) => set("shelfNumber", e.target.value)}
            placeholder="необов'язково"
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
          Скасувати
        </Btn>
        <Btn type="submit" disabled={create.isPending}>
          {create.isPending ? "Збереження…" : "Додати партію"}
        </Btn>
      </div>
    </form>
  );
}
