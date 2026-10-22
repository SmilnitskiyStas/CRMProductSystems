"use client";

import { useState } from "react";
import { Plus, Pencil, PowerOff } from "lucide-react";
import { useTranslations } from "next-intl";
import { useRecipes, useDeactivateRecipe } from "../hooks/useProduction";
import { RecipeForm } from "./RecipeForm";
import type { RecipeListItemDto } from "../types";
import { Table, type TableColumn } from "@/components/ui/Table";
import { Btn } from "@/components/ui/Btn";
import { ActionMenu } from "@/components/ui/ActionMenu";

interface Props {
  showInactive: boolean;
  /** Extra toolbar content (e.g. the "show inactive" toggle) rendered next to the add button. */
  toolbarExtra?: React.ReactNode;
}

export function RecipeTable({ showInactive, toolbarExtra }: Props) {
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
            padding: "3px 8px",
            borderRadius: 20,
            background: "#1F2937",
            color: "#9CA3AF",
            fontSize: 11,
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
        <ActionMenu
          items={[
            { label: t("editTitle"), icon: <Pencil size={13} />, onClick: () => setEditRecipe(recipe) },
            ...(recipe.isActive
              ? [
                  { separator: true },
                  {
                    label: t("deactivateTitle"),
                    icon: <PowerOff size={13} />,
                    variant: "danger" as const,
                    onClick: () => handleDeactivate(recipe.id),
                  },
                ]
              : []),
          ]}
        />
      ),
    },
  ];

  if (isError) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 13 }}>
        {t("loadError")}
      </div>
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 16, flexWrap: "wrap" }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
        <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
          {toolbarExtra}
          <Btn icon={<Plus size={15} />} onClick={() => setCreateOpen(true)}>
            {t("addRecipe")}
          </Btn>
        </div>
      </div>

      {/* Table */}
      <Table
        columns={columns}
        rows={recipes}
        rowKey={(recipe) => recipe.id}
        isLoading={isLoading}
        emptyMessage={isLoading ? t("loading") : t("empty")}
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
        padding: "3px 8px",
        borderRadius: 20,
        fontSize: 11,
        fontWeight: 600,
        background: active ? "#064E3B" : "#1F2937",
        color: active ? "#34D399" : "#6B7280",
      }}
    >
      {active ? t("statusActive") : t("statusInactive")}
    </span>
  );
}
