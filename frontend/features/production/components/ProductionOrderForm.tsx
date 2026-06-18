"use client";

import { useState } from "react";
import { X } from "lucide-react";
import {
  useCreateProductionOrder,
  useRecipes,
  useProductionLocations,
} from "../hooks/useProduction";

interface Props {
  onClose: () => void;
}

export function ProductionOrderForm({ onClose }: Props) {
  const { data: recipes = [] } = useRecipes(false);
  const { data: locations = [] } = useProductionLocations();
  const createOrder = useCreateProductionOrder();

  const [recipeId, setRecipeId] = useState("");
  const [locationId, setLocationId] = useState("");
  const [plannedQty, setPlannedQty] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);

  function validate(): string | null {
    if (!recipeId) return "Оберіть рецепт";
    if (!locationId) return "Оберіть локацію";
    const qty = parseFloat(plannedQty);
    if (isNaN(qty) || qty <= 0) return "Плановий вихід має бути більше 0";
    return null;
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const err = validate();
    if (err) {
      setError(err);
      return;
    }
    createOrder.mutate(
      {
        recipeId,
        locationId,
        plannedQty: parseFloat(plannedQty),
        notes: notes.trim() || undefined,
      },
      {
        onSuccess: () => onClose(),
        onError: (err) => setError(String(err)),
      }
    );
  }

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.7)",
        zIndex: 50,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: 16,
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
          width: "100%",
          maxWidth: 480,
          boxShadow: "0 25px 50px rgba(0,0,0,0.6)",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "20px 24px 16px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
            Новий виробничий ордер
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
        <form onSubmit={handleSubmit} style={{ padding: "20px 24px" }}>
          <div style={{ marginBottom: 16 }}>
            <label style={labelStyle}>Рецепт *</label>
            <select
              value={recipeId}
              onChange={(e) => setRecipeId(e.target.value)}
              style={inputStyle}
            >
              <option value="">— Оберіть рецепт —</option>
              {recipes.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </select>
          </div>

          <div style={{ marginBottom: 16 }}>
            <label style={labelStyle}>Локація *</label>
            <select
              value={locationId}
              onChange={(e) => setLocationId(e.target.value)}
              style={inputStyle}
            >
              <option value="">— Оберіть локацію —</option>
              {locations.map((loc) => (
                <option key={loc.id} value={loc.id}>
                  {loc.name}
                </option>
              ))}
            </select>
          </div>

          <div style={{ marginBottom: 16 }}>
            <label style={labelStyle}>Плановий вихід *</label>
            <input
              type="number"
              value={plannedQty}
              onChange={(e) => setPlannedQty(e.target.value)}
              placeholder="1"
              min={0}
              step="0.001"
              style={inputStyle}
            />
          </div>

          <div style={{ marginBottom: 20 }}>
            <label style={labelStyle}>Примітки</label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
              placeholder="Необов'язково"
              style={{ ...inputStyle, resize: "vertical" }}
            />
          </div>

          {error && (
            <div
              style={{
                marginBottom: 16,
                padding: "10px 14px",
                background: "#1F1010",
                border: "1px solid #7F1D1D",
                borderRadius: 8,
                color: "#F87171",
                fontSize: 13,
              }}
            >
              {error}
            </div>
          )}

          <div style={{ display: "flex", justifyContent: "flex-end", gap: 10 }}>
            <button
              type="button"
              onClick={onClose}
              style={{
                padding: "8px 18px",
                borderRadius: 8,
                border: "1px solid #1F2937",
                background: "transparent",
                color: "#9CA3AF",
                fontSize: 13,
                cursor: "pointer",
              }}
            >
              Скасувати
            </button>
            <button
              type="submit"
              disabled={createOrder.isPending}
              style={{
                padding: "8px 18px",
                borderRadius: 8,
                border: "none",
                background: createOrder.isPending ? "#1D3461" : "#3B82F6",
                color: "#fff",
                fontSize: 13,
                fontWeight: 600,
                cursor: createOrder.isPending ? "not-allowed" : "pointer",
              }}
            >
              {createOrder.isPending ? "Створення…" : "Створити"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  padding: "8px 10px",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};
