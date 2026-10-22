"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import {
  useCreateProductionOrder,
  useRecipes,
  useProductionLocations,
} from "../hooks/useProduction";

interface Props {
  onClose: () => void;
}

export function ProductionOrderForm({ onClose }: Props) {
  const t = useTranslations("Dashboard.production.orderForm");
  const { data: recipes = [] } = useRecipes(false);
  const { data: locations = [] } = useProductionLocations();
  const createOrder = useCreateProductionOrder();

  const [recipeId, setRecipeId] = useState("");
  const [locationId, setLocationId] = useState("");
  const [plannedQty, setPlannedQty] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);

  function validate(): string | null {
    if (!recipeId) return t("errorRecipeRequired");
    if (!locationId) return t("errorLocationRequired");
    const qty = parseFloat(plannedQty);
    if (isNaN(qty) || qty <= 0) return t("errorPlannedQtyPositive");
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
    <Modal title={t("title")} onClose={onClose} width={480}>
    <form onSubmit={handleSubmit}>
      <div style={{ marginBottom: 16 }}>
        <label style={labelStyle}>{t("recipeLabel")}</label>
        <select
          value={recipeId}
          onChange={(e) => setRecipeId(e.target.value)}
          style={inputStyle}
        >
          <option value="">{t("selectRecipePlaceholder")}</option>
          {recipes.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name}
            </option>
          ))}
        </select>
      </div>

      <div style={{ marginBottom: 16 }}>
        <label style={labelStyle}>{t("locationLabel")}</label>
        <select
          value={locationId}
          onChange={(e) => setLocationId(e.target.value)}
          style={inputStyle}
        >
          <option value="">{t("selectLocationPlaceholder")}</option>
          {locations.map((loc) => (
            <option key={loc.id} value={loc.id}>
              {loc.name}
            </option>
          ))}
        </select>
      </div>

      <div style={{ marginBottom: 16 }}>
        <label style={labelStyle}>{t("plannedQtyLabel")}</label>
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
        <label style={labelStyle}>{t("notesLabel")}</label>
        <textarea
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          rows={2}
          placeholder={t("notesPlaceholder")}
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
        <Btn variant="ghost" type="button" onClick={onClose}>
          {t("cancel")}
        </Btn>
        <Btn type="submit" disabled={createOrder.isPending}>
          {createOrder.isPending ? t("creating") : t("create")}
        </Btn>
      </div>
    </form>
    </Modal>
  );
}

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  marginBottom: 5,
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  padding: "8px 12px",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};
