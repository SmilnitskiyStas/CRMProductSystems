"use client";

import { useState } from "react";
import { Plus, Pencil, PowerOff } from "lucide-react";
import { useTranslations } from "next-intl";
import { useRecipes, useDeactivateRecipe } from "../hooks/useProduction";
import { RecipeForm } from "./RecipeForm";
import type { RecipeListItemDto } from "../types";

interface Props {
  showInactive: boolean;
}

export function RecipeTable({ showInactive }: Props) {
  const t = useTranslations("Dashboard.production.recipeTable");
  const { data: recipes = [], isLoading, isError } = useRecipes(showInactive);
  const deactivate = useDeactivateRecipe();

  const [createOpen, setCreateOpen] = useState(false);
  const [editRecipe, setEditRecipe] = useState<RecipeListItemDto | null>(null);

  function handleDeactivate(id: string) {
    if (!confirm(t("deactivateConfirm"))) return;
    deactivate.mutate(id);
  }

  if (isLoading) {
    return (
      <div style={{ padding: "48px 32px", color: "#6B7280", fontSize: 14 }}>
        {t("loading")}
      </div>
    );
  }
  if (isError) {
    return (
      <div style={{ padding: "48px 32px", color: "#F87171", fontSize: 14 }}>
        {t("loadError")}
      </div>
    );
  }

  return (
    <div>
      {/* Toolbar */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          marginBottom: 20,
        }}
      >
        <h1 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>
          {t("title")}
        </h1>
        <button
          onClick={() => setCreateOpen(true)}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 6,
            padding: "8px 16px",
            borderRadius: 8,
            border: "none",
            background: "#3B82F6",
            color: "#fff",
            fontSize: 13,
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          <Plus size={15} />
          {t("addRecipe")}
        </button>
      </div>

      {/* Table */}
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 10,
          overflow: "hidden",
        }}
      >
        {recipes.length === 0 ? (
          <div
            style={{
              padding: "48px 32px",
              textAlign: "center",
              color: "#4B5563",
              fontSize: 14,
            }}
          >
            {t("empty")}
          </div>
        ) : (
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                {[
                  t("headerName"),
                  t("headerOutputItem"),
                  t("headerOutput"),
                  t("headerIngredients"),
                  t("headerStatus"),
                  "",
                ].map((h, i) => (
                  <th
                    key={i}
                    style={{
                      padding: "10px 16px",
                      textAlign: "left",
                      color: "#4B5563",
                      fontSize: 11,
                      fontWeight: 600,
                      textTransform: "uppercase",
                      letterSpacing: "0.04em",
                      borderBottom: "1px solid #1F2937",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {recipes.map((recipe) => (
                <tr
                  key={recipe.id}
                  style={{ borderBottom: "1px solid #1F2937" }}
                >
                  <td style={tdStyle}>
                    <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
                      {recipe.name}
                    </span>
                  </td>
                  <td style={tdStyle}>
                    <span style={{ color: "#9CA3AF", fontSize: 13 }}>
                      {recipe.outputItemName}
                    </span>
                  </td>
                  <td style={tdStyle}>
                    <span style={{ color: "#9CA3AF", fontSize: 13 }}>
                      {recipe.outputQty} {recipe.unit}
                    </span>
                  </td>
                  <td style={tdStyle}>
                    <span
                      style={{
                        display: "inline-block",
                        padding: "2px 8px",
                        borderRadius: 12,
                        background: "#1F2937",
                        color: "#9CA3AF",
                        fontSize: 12,
                      }}
                    >
                      {recipe.ingredientCount}
                    </span>
                  </td>
                  <td style={tdStyle}>
                    <StatusBadge active={recipe.isActive} />
                  </td>
                  <td style={{ ...tdStyle, textAlign: "right" }}>
                    <div style={{ display: "flex", justifyContent: "flex-end", gap: 6 }}>
                      <ActionButton
                        onClick={() => setEditRecipe(recipe)}
                        title={t("editTitle")}
                        color="#9CA3AF"
                      >
                        <Pencil size={14} />
                      </ActionButton>
                      {recipe.isActive && (
                        <ActionButton
                          onClick={() => handleDeactivate(recipe.id)}
                          title={t("deactivateTitle")}
                          color="#F87171"
                        >
                          <PowerOff size={14} />
                        </ActionButton>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Create modal */}
      {createOpen && <RecipeForm onClose={() => setCreateOpen(false)} />}

      {/* Edit modal */}
      {editRecipe && (
        <RecipeForm
          recipeId={editRecipe.id}
          onClose={() => setEditRecipe(null)}
        />
      )}
    </div>
  );
}

// ── Sub-components ─────────────────────────────────────────────────────────────

function StatusBadge({ active }: { active: boolean }) {
  const t = useTranslations("Dashboard.production.recipeTable");
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 10px",
        borderRadius: 12,
        fontSize: 12,
        fontWeight: 600,
        background: active ? "#064E3B" : "#1F2937",
        color: active ? "#34D399" : "#6B7280",
      }}
    >
      {active ? t("statusActive") : t("statusInactive")}
    </span>
  );
}

function ActionButton({
  onClick,
  title,
  color,
  children,
}: {
  onClick: () => void;
  title: string;
  color: string;
  children: React.ReactNode;
}) {
  return (
    <button
      onClick={onClick}
      title={title}
      style={{
        background: "transparent",
        border: "1px solid #1F2937",
        borderRadius: 6,
        color,
        cursor: "pointer",
        padding: "5px 8px",
        display: "flex",
        alignItems: "center",
      }}
    >
      {children}
    </button>
  );
}

const tdStyle: React.CSSProperties = {
  padding: "12px 16px",
  verticalAlign: "middle",
};
