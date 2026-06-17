"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { useAddWorkOrderLine } from "../hooks/useAutoService";
import { useServiceCatalog } from "../hooks/useAutoService";
import type { AddWorkOrderLineRequest, WorkOrderLineType } from "../types";

// We fetch spare parts from /api/items?item_type=spare_part
import { api } from "@/lib/api";
import { useQuery } from "@tanstack/react-query";
import type { CatalogProductDto } from "@/features/catalog/types";

function useSparePartItems() {
  return useQuery({
    queryKey: ["items", "spare_part"],
    queryFn: () =>
      api.get<CatalogProductDto[]>("/api/items?item_type=spare_part"),
    staleTime: 60_000,
  });
}

interface Props {
  workOrderId: string;
  onClose: () => void;
}

export function WorkOrderLineForm({ workOrderId, onClose }: Props) {
  const addLine = useAddWorkOrderLine(workOrderId);
  const { data: catalog } = useServiceCatalog();
  const { data: spareParts } = useSparePartItems();

  const [type, setType] = useState<WorkOrderLineType>("service");
  const [name, setName] = useState("");
  const [serviceCatalogId, setServiceCatalogId] = useState("");
  const [itemId, setItemId] = useState("");
  const [qty, setQty] = useState(1);
  const [unitPrice, setUnitPrice] = useState(0);
  const [discount, setDiscount] = useState(0);
  const [error, setError] = useState<string | null>(null);

  function handleServiceSelect(id: string) {
    setServiceCatalogId(id);
    if (!id) { setName(""); setUnitPrice(0); return; }
    const item = (catalog ?? []).find((c) => c.id === id);
    if (item) { setName(item.name); setUnitPrice(item.defaultPrice); }
  }

  function handlePartSelect(id: string) {
    setItemId(id);
    if (!id) { setName(""); setUnitPrice(0); return; }
    const part = (spareParts ?? []).find((p) => p.id === id);
    if (part) { setName(part.name); setUnitPrice(part.priceRetail ?? 0); }
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) { setError("Введіть назву"); return; }
    if (qty <= 0) { setError("Кількість має бути більше 0"); return; }
    const body: AddWorkOrderLineRequest = {
      type,
      name: name.trim(),
      qty,
      unitPrice,
      discount: discount || undefined,
      serviceCatalogId: type === "service" && serviceCatalogId ? serviceCatalogId : undefined,
      itemId: type === "part" && itemId ? itemId : undefined,
    };
    addLine.mutate(body, {
      onSuccess: () => onClose(),
      onError: (err) => setError(err instanceof Error ? err.message : "Помилка"),
    });
  }

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.7)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: "24px",
          width: "100%",
          maxWidth: 480,
        }}
      >
        {/* Header */}
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
            Додати рядок
          </h2>
          <button onClick={onClose} style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}>
            <X size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          {/* Type selector */}
          <div>
            <label style={labelStyle}>Тип</label>
            <div style={{ display: "flex", gap: 8 }}>
              {(["service", "part"] as WorkOrderLineType[]).map((t) => (
                <button
                  key={t}
                  type="button"
                  onClick={() => { setType(t); setName(""); setServiceCatalogId(""); setItemId(""); setUnitPrice(0); }}
                  style={{
                    flex: 1,
                    padding: "8px 12px",
                    borderRadius: 8,
                    border: `1px solid ${type === t ? "#3B82F6" : "#1F2937"}`,
                    background: type === t ? "#1E3A5F" : "transparent",
                    color: type === t ? "#60A5FA" : "#6B7280",
                    fontSize: 13,
                    cursor: "pointer",
                  }}
                >
                  {t === "service" ? "🔧 Послуга" : "⚙️ Запчастина"}
                </button>
              ))}
            </div>
          </div>

          {/* Service / Part dropdown */}
          {type === "service" ? (
            <div>
              <label style={labelStyle}>Послуга з каталогу</label>
              <select
                value={serviceCatalogId}
                onChange={(e) => handleServiceSelect(e.target.value)}
                style={selectStyle}
              >
                <option value="">— Виберіть або введіть нижче —</option>
                {(catalog ?? []).filter((c) => c.isActive).map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name} · {c.defaultPrice.toFixed(2)} грн
                  </option>
                ))}
              </select>
            </div>
          ) : (
            <div>
              <label style={labelStyle}>Запчастина</label>
              <select
                value={itemId}
                onChange={(e) => handlePartSelect(e.target.value)}
                style={selectStyle}
              >
                <option value="">— Виберіть або введіть нижче —</option>
                {(spareParts ?? []).map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </div>
          )}

          {/* Name (editable) */}
          <div>
            <label style={labelStyle}>Назва *</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={type === "service" ? "Назва послуги" : "Назва запчастини"}
              style={inputStyle}
            />
          </div>

          {/* Qty / Price / Discount */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 10 }}>
            <div>
              <label style={labelStyle}>Кількість</label>
              <input
                type="number"
                min={1}
                step={1}
                value={qty}
                onChange={(e) => setQty(Number(e.target.value))}
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>Ціна, грн</label>
              <input
                type="number"
                min={0}
                step={0.01}
                value={unitPrice}
                onChange={(e) => setUnitPrice(Number(e.target.value))}
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>Знижка, %</label>
              <input
                type="number"
                min={0}
                max={100}
                step={1}
                value={discount}
                onChange={(e) => setDiscount(Number(e.target.value))}
                style={inputStyle}
              />
            </div>
          </div>

          {/* Preview total */}
          <div style={{ color: "#9CA3AF", fontSize: 13, textAlign: "right" }}>
            Разом:{" "}
            <strong style={{ color: "#E8EDF5" }}>
              {(qty * unitPrice * (1 - discount / 100)).toLocaleString("uk-UA", {
                style: "currency",
                currency: "UAH",
              })}
            </strong>
          </div>

          {error && (
            <div style={{ color: "#F87171", fontSize: 12, padding: "8px 12px", background: "#1F1010", borderRadius: 6 }}>
              {error}
            </div>
          )}

          <div style={{ display: "flex", gap: 8, justifyContent: "flex-end", marginTop: 4 }}>
            <button type="button" onClick={onClose} style={btnSecondaryStyle}>
              Скасувати
            </button>
            <button type="submit" disabled={addLine.isPending} style={btnPrimaryStyle}>
              {addLine.isPending ? "Збереження…" : "Додати рядок"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ─── Shared micro-styles ──────────────────────────────────────────────────────

const labelStyle: React.CSSProperties = { color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" };

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

const selectStyle: React.CSSProperties = { ...inputStyle, appearance: "auto" };

const btnPrimaryStyle: React.CSSProperties = {
  padding: "9px 20px",
  borderRadius: 8,
  border: "none",
  background: "#1D4ED8",
  color: "#E8EDF5",
  fontSize: 13,
  fontWeight: 600,
  cursor: "pointer",
};

const btnSecondaryStyle: React.CSSProperties = {
  padding: "9px 16px",
  borderRadius: 8,
  border: "1px solid #1F2937",
  background: "transparent",
  color: "#6B7280",
  fontSize: 13,
  cursor: "pointer",
};
