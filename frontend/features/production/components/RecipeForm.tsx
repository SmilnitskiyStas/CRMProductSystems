"use client";

import { useState, useEffect } from "react";
import { X, Plus, Trash2 } from "lucide-react";
import { useTranslations } from "next-intl";
import {
  useRecipe,
  useCreateRecipe,
  useUpdateRecipe,
  useProductionItems,
} from "../hooks/useProduction";
import type { RecipeIngredientCreateDto } from "../types";

interface Props {
  recipeId?: string;
  onClose: () => void;
}

interface IngredientRow {
  itemId: string;
  qty: string;
  unit: string;
}

const EMPTY_INGREDIENT: IngredientRow = { itemId: "", qty: "", unit: "" };

export function RecipeForm({ recipeId, onClose }: Props) {
  const t = useTranslations("Dashboard.production.recipeForm");
  const isEdit = !!recipeId;

  // Load existing recipe when editing
  const { data: existing } = useRecipe(recipeId ?? "");
  const { data: items = [] } = useProductionItems();

  const createRecipe = useCreateRecipe();
  const updateRecipe = useUpdateRecipe(recipeId ?? "");

  // Form state
  const [name, setName] = useState("");
  const [outputItemId, setOutputItemId] = useState("");
  const [outputQty, setOutputQty] = useState("");
  const [unit, setUnit] = useState("");
  const [notes, setNotes] = useState("");
  const [ingredients, setIngredients] = useState<IngredientRow[]>([
    { ...EMPTY_INGREDIENT },
  ]);
  const [error, setError] = useState<string | null>(null);

  // Pre-fill when editing
  useEffect(() => {
    if (existing) {
      setName(existing.name);
      setOutputItemId(existing.outputItemId);
      setOutputQty(String(existing.outputQty));
      setUnit(existing.unit);
      setNotes(existing.notes ?? "");
      if (existing.ingredients.length > 0) {
        setIngredients(
          existing.ingredients.map((ing) => ({
            itemId: ing.itemId,
            qty: String(ing.qty),
            unit: ing.unit,
          }))
        );
      }
    }
  }, [existing]);

  function addIngredient() {
    setIngredients((prev) => [...prev, { ...EMPTY_INGREDIENT }]);
  }

  function removeIngredient(index: number) {
    setIngredients((prev) => prev.filter((_, i) => i !== index));
  }

  function updateIngredient(
    index: number,
    field: keyof IngredientRow,
    value: string
  ) {
    setIngredients((prev) =>
      prev.map((row, i) => (i === index ? { ...row, [field]: value } : row))
    );
  }

  function validate(): string | null {
    if (!name.trim()) return t("errorNameRequired");
    if (!outputItemId) return t("errorOutputItemRequired");
    const qty = parseFloat(outputQty);
    if (isNaN(qty) || qty <= 0) return t("errorOutputQtyPositive");
    if (!unit.trim()) return t("errorUnitRequired");
    if (ingredients.length === 0) return t("errorIngredientsRequired");
    for (const ing of ingredients) {
      if (!ing.itemId) return t("errorIngredientItemRequired");
      const ingQty = parseFloat(ing.qty);
      if (isNaN(ingQty) || ingQty <= 0)
        return t("errorIngredientQtyPositive");
      if (!ing.unit.trim()) return t("errorIngredientUnitRequired");
    }
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

    const ingredientDtos: RecipeIngredientCreateDto[] = ingredients.map(
      (ing) => ({
        itemId: ing.itemId,
        qty: parseFloat(ing.qty),
        unit: ing.unit,
      })
    );

    if (isEdit) {
      // Edit mode: only update name/notes/isActive via PUT
      updateRecipe.mutate(
        { name: name.trim(), notes: notes.trim() || undefined },
        { onSuccess: () => onClose(), onError: (err) => setError(String(err)) }
      );
    } else {
      createRecipe.mutate(
        {
          name: name.trim(),
          outputItemId,
          outputQty: parseFloat(outputQty),
          unit: unit.trim(),
          notes: notes.trim() || undefined,
          ingredients: ingredientDtos,
        },
        { onSuccess: () => onClose(), onError: (err) => setError(String(err)) }
      );
    }
  }

  const isPending = createRecipe.isPending || updateRecipe.isPending;

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
          maxWidth: 640,
          maxHeight: "90vh",
          overflow: "auto",
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
            {isEdit ? t("titleEdit") : t("titleNew")}
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
          {/* Name */}
          <div style={{ marginBottom: 16 }}>
            <label style={labelStyle}>{t("nameLabel")}</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t("namePlaceholder")}
              style={inputStyle}
            />
          </div>

          {/* Output item + qty + unit */}
          <div
            style={{ display: "grid", gridTemplateColumns: "1fr 120px 100px", gap: 12, marginBottom: 16 }}
          >
            <div>
              <label style={labelStyle}>{t("outputItemLabel")}</label>
              <select
                value={outputItemId}
                onChange={(e) => setOutputItemId(e.target.value)}
                style={inputStyle}
                disabled={isEdit}
              >
                <option value="">{t("selectItemPlaceholder")}</option>
                {items.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label style={labelStyle}>{t("outputQtyLabel")}</label>
              <input
                type="number"
                value={outputQty}
                onChange={(e) => setOutputQty(e.target.value)}
                placeholder="1"
                min={0}
                step="0.001"
                style={inputStyle}
                disabled={isEdit}
              />
            </div>
            <div>
              <label style={labelStyle}>{t("unitLabel")}</label>
              <input
                type="text"
                value={unit}
                onChange={(e) => setUnit(e.target.value)}
                placeholder={t("unitPlaceholder")}
                style={inputStyle}
                disabled={isEdit}
              />
            </div>
          </div>

          {/* Notes */}
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

          {/* Ingredients — shown only on create */}
          {!isEdit && (
            <div style={{ marginBottom: 20 }}>
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  marginBottom: 10,
                }}
              >
                <label style={{ ...labelStyle, margin: 0 }}>
                  {t("ingredientsLabel")}
                </label>
                <button
                  type="button"
                  onClick={addIngredient}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: 4,
                    padding: "4px 10px",
                    borderRadius: 6,
                    border: "1px solid #1F2937",
                    background: "transparent",
                    color: "#9CA3AF",
                    fontSize: 12,
                    cursor: "pointer",
                  }}
                >
                  <Plus size={13} />
                  {t("addIngredient")}
                </button>
              </div>

              <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                {ingredients.map((ing, idx) => (
                  <div
                    key={idx}
                    style={{
                      display: "grid",
                      gridTemplateColumns: "1fr 100px 80px 32px",
                      gap: 8,
                      alignItems: "center",
                    }}
                  >
                    <select
                      value={ing.itemId}
                      onChange={(e) =>
                        updateIngredient(idx, "itemId", e.target.value)
                      }
                      style={inputStyle}
                    >
                      <option value="">{t("ingredientItemPlaceholder")}</option>
                      {items.map((item) => (
                        <option key={item.id} value={item.id}>
                          {item.name}
                        </option>
                      ))}
                    </select>
                    <input
                      type="number"
                      value={ing.qty}
                      onChange={(e) =>
                        updateIngredient(idx, "qty", e.target.value)
                      }
                      placeholder={t("ingredientQtyPlaceholder")}
                      min={0}
                      step="0.001"
                      style={inputStyle}
                    />
                    <input
                      type="text"
                      value={ing.unit}
                      onChange={(e) =>
                        updateIngredient(idx, "unit", e.target.value)
                      }
                      placeholder={t("ingredientUnitPlaceholder")}
                      style={inputStyle}
                    />
                    <button
                      type="button"
                      onClick={() => removeIngredient(idx)}
                      disabled={ingredients.length === 1}
                      title={t("removeIngredientTitle")}
                      style={{
                        background: "transparent",
                        border: "1px solid #1F2937",
                        borderRadius: 6,
                        color:
                          ingredients.length === 1 ? "#374151" : "#F87171",
                        cursor:
                          ingredients.length === 1 ? "not-allowed" : "pointer",
                        padding: "5px",
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "center",
                      }}
                    >
                      <Trash2 size={13} />
                    </button>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Error */}
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

          {/* Actions */}
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
              {t("cancel")}
            </button>
            <button
              type="submit"
              disabled={isPending}
              style={{
                padding: "8px 18px",
                borderRadius: 8,
                border: "none",
                background: isPending ? "#1D3461" : "#3B82F6",
                color: "#fff",
                fontSize: 13,
                fontWeight: 600,
                cursor: isPending ? "not-allowed" : "pointer",
              }}
            >
              {isPending ? t("saving") : isEdit ? t("save") : t("create")}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ── Styles ─────────────────────────────────────────────────────────────────────

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
