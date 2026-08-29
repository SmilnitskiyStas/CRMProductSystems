"use client";

import { useState } from "react";
import { Plus, Pencil, PowerOff } from "lucide-react";
import { useTranslations } from "next-intl";
import { useRecipes, useDeactivateRecipe } from "../hooks/useProduction";
import { RecipeForm } from "./RecipeForm";
import type { RecipeListItemDto } from "../types";
import { Table, type TableColumn } from "@/components/ui/Table";

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

  const columns: TableColumn<RecipeListItemDto>[] = [
    {
      key: "name",
      header: t("headerName"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (recipe) => recipe.name,
    },
    {
      key: "outputItem",
      header: t("headerOutputItem"),
      render: (recipe) => recipe.outputItemName,
    },
    {
      key: "output",
      header: t("headerOutput"),
      render: (recipe) => `${recipe.outputQty} ${recipe.unit}`,
    },
    {
      key: "ingredients",
      header: t("headerIngredients"),
      render: (recipe) => (
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
      ),
    },
    {
      key: "status",
      header: t("headerStatus"),
      render: (recipe) => <StatusBadge active={recipe.isActive} />,
    },
    {
      key: "actions",
      header: "",
      render: (recipe) => (
        <div style={{ display: "flex", justifyContent: "center", gap: 6 }}>
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
      ),
    },
  ];

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
      <Table
        columns={columns}
        rows={recipes}
        rowKey={(recipe) => recipe.id}
        emptyMessage={t("empty")}
      />

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
