"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { useAddSupplierItem } from "../hooks/useMarketplace";
import type { AddSupplierItemRequest } from "../types";
import { Btn } from "@/components/ui/Btn";

interface Props {
  supplierId: string;
  onClose: () => void;
}

export function AddSupplierItemModal({ supplierId, onClose }: Props) {
  const addItem = useAddSupplierItem(supplierId);

  const [customName, setCustomName] = useState("");
  const [priceRaw, setPriceRaw] = useState("");
  const [minQtyRaw, setMinQtyRaw] = useState("");
  const [unit, setUnit] = useState("");
  const [isAvailable, setIsAvailable] = useState(true);
  const [error, setError] = useState<string | null>(null);

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (!customName.trim()) {
      setError("Назва товару є обов'язковою.");
      return;
    }

    const price = priceRaw ? parseFloat(priceRaw) : undefined;
    const minQty = minQtyRaw ? parseInt(minQtyRaw, 10) : undefined;

    if (priceRaw && (isNaN(price!) || price! < 0)) {
      setError("Некоректна ціна.");
      return;
    }
    if (minQtyRaw && (isNaN(minQty!) || minQty! < 1)) {
      setError("Некоректна мінімальна кількість.");
      return;
    }

    const body: AddSupplierItemRequest = {
      customName: customName.trim(),
      price,
      minQty,
      unit: unit.trim() || undefined,
      isAvailable,
    };

    addItem.mutate(body, {
      onSuccess: () => onClose(),
      onError: (err: any) => {
        setError(err?.message ?? "Помилка при додаванні товару.");
      },
    });
  }

  const inputStyle: React.CSSProperties = {
    width: "100%",
    padding: "9px 12px",
    background: "#0D1117",
    border: "1px solid #1F2937",
    borderRadius: 8,
    color: "#E8EDF5",
    fontSize: 13,
    outline: "none",
    boxSizing: "border-box",
  };

  const labelStyle: React.CSSProperties = {
    color: "#9CA3AF",
    fontSize: 12,
    fontWeight: 500,
    marginBottom: 6,
    display: "block",
  };

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: 28,
          width: "100%",
          maxWidth: 440,
          display: "flex",
          flexDirection: "column",
          gap: 20,
        }}
      >
        {/* Header */}
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>
            Додати товар до каталогу
          </h2>
          <button
            onClick={onClose}
            style={{
              background: "transparent",
              border: "none",
              color: "#6B7280",
              cursor: "pointer",
              padding: 4,
            }}
          >
            <X size={18} />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={labelStyle}>
              Назва товару <span style={{ color: "#F87171" }}>*</span>
            </label>
            <input
              type="text"
              value={customName}
              onChange={(e) => setCustomName(e.target.value)}
              placeholder="Молоко 2.5%, 1л"
              style={inputStyle}
              required
            />
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <div>
              <label style={labelStyle}>Ціна (грн)</label>
              <input
                type="number"
                min="0"
                step="0.01"
                value={priceRaw}
                onChange={(e) => setPriceRaw(e.target.value)}
                placeholder="0.00"
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>Мін. замовлення</label>
              <input
                type="number"
                min="1"
                step="1"
                value={minQtyRaw}
                onChange={(e) => setMinQtyRaw(e.target.value)}
                placeholder="1"
                style={inputStyle}
              />
            </div>
          </div>

          <div>
            <label style={labelStyle}>Одиниця виміру</label>
            <input
              type="text"
              value={unit}
              onChange={(e) => setUnit(e.target.value)}
              placeholder="шт, кг, л, упак."
              style={inputStyle}
            />
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <input
              type="checkbox"
              id="isAvailable"
              checked={isAvailable}
              onChange={(e) => setIsAvailable(e.target.checked)}
              style={{ width: 16, height: 16, cursor: "pointer" }}
            />
            <label
              htmlFor="isAvailable"
              style={{ color: "#E8EDF5", fontSize: 13, cursor: "pointer" }}
            >
              Товар в наявності
            </label>
          </div>

          {error && (
            <div style={{ color: "#F87171", fontSize: 13 }}>{error}</div>
          )}

          <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 4 }}>
            <Btn type="button" variant="ghost" onClick={onClose}>
              Скасувати
            </Btn>
            <Btn type="submit" disabled={addItem.isPending}>
              {addItem.isPending ? "Збереження…" : "Додати"}
            </Btn>
          </div>
        </form>
      </div>
    </div>
  );
}
