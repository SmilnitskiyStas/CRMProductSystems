"use client";

import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { ChevronDown, ChevronRight, Pencil, Plus, Trash2 } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import type { PlatformCategoryDto } from "../types";
import { useDeleteCategory } from "../hooks/useProviderCategories";
import { buildChildrenMap } from "../lib/categoryTree";

interface Props {
  categories: PlatformCategoryDto[];
  onEdit: (category: PlatformCategoryDto) => void;
  onAddSub: (parentId: string) => void;
}

export function CategoryTreeManager({ categories, onEdit, onAddSub }: Props) {
  const t = useTranslations("Dashboard.providerCategories");
  const tBusinessTypes = useTranslations("Dashboard.provider.businessTypes");
  const deleteCategory = useDeleteCategory();

  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [pendingDelete, setPendingDelete] = useState<PlatformCategoryDto | null>(null);

  const childrenByParent = useMemo(() => buildChildrenMap(categories), [categories]);
  const roots = childrenByParent.get(null) ?? [];

  function toggle(id: string) {
    setExpanded((current) => {
      const next = new Set(current);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  async function confirmDelete() {
    if (!pendingDelete) return;
    try {
      await deleteCategory.mutateAsync(pendingDelete.id);
      toast.success(t("toasts.deleted"));
      setPendingDelete(null);
    } catch (err) {
      toast.error((err as Error)?.message ?? t("toasts.error"));
      setPendingDelete(null);
    }
  }

  function renderRow(category: PlatformCategoryDto, depth: number): React.ReactNode {
    const children = childrenByParent.get(category.id) ?? [];
    const isExpanded = expanded.has(category.id);
    return (
      <div key={category.id}>
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            minHeight: 42,
            padding: "4px 8px",
            paddingLeft: 8 + depth * 22,
            borderBottom: "1px solid #161B26",
          }}
        >
          {children.length > 0 ? (
            <button
              type="button"
              onClick={() => toggle(category.id)}
              aria-label={isExpanded ? t("collapse") : t("expand")}
              style={{ display: "flex", padding: 2, border: 0, background: "transparent", color: "#6B7280", cursor: "pointer" }}
            >
              {isExpanded ? <ChevronDown size={15} /> : <ChevronRight size={15} />}
            </button>
          ) : (
            <span style={{ width: 19, flexShrink: 0 }} />
          )}

          <span style={{ color: category.isActive ? "#E8EDF5" : "#6B7280", fontSize: 13, fontWeight: 500 }}>
            {category.name}
          </span>

          <span style={{ color: "#4B5563", fontSize: 11 }}>
            {t("itemCountBadge", { count: category.itemCount })}
          </span>

          {!category.isActive && (
            <span
              style={{
                padding: "1px 7px",
                borderRadius: 20,
                background: "#111827",
                border: "1px solid #374151",
                color: "#9CA3AF",
                fontSize: 10,
                fontWeight: 600,
              }}
            >
              {t("inactivePill")}
            </span>
          )}

          <div style={{ display: "flex", gap: 4, flexWrap: "wrap" }}>
            {category.businessTypes.map((bt) => (
              <span
                key={bt}
                style={{
                  padding: "1px 7px",
                  borderRadius: 20,
                  background: "#0F1F3D",
                  border: "1px solid #1E3A5F",
                  color: "#93C5FD",
                  fontSize: 10,
                }}
              >
                {tBusinessTypes.has(bt) ? tBusinessTypes(bt) : bt}
              </span>
            ))}
            {category.businessTypes.length === 0 && (
              <span style={{ color: "#4B5563", fontSize: 10 }}>{t("allBusinessTypes")}</span>
            )}
          </div>

          <div style={{ marginLeft: "auto", display: "flex", gap: 4 }}>
            <IconButton title={t("addSubCategory")} onClick={() => onAddSub(category.id)}>
              <Plus size={14} />
            </IconButton>
            <IconButton title={t("edit")} onClick={() => onEdit(category)}>
              <Pencil size={13} />
            </IconButton>
            <IconButton title={t("delete")} danger onClick={() => setPendingDelete(category)}>
              <Trash2 size={13} />
            </IconButton>
          </div>
        </div>

        {isExpanded && children.map((child) => renderRow(child, depth + 1))}
      </div>
    );
  }

  return (
    <>
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          overflow: "hidden",
        }}
      >
        {roots.length === 0 ? (
          <div style={{ padding: "32px 20px", color: "#4B5563", fontSize: 13, textAlign: "center" }}>
            {t("empty")}
          </div>
        ) : (
          roots.map((root) => renderRow(root, 0))
        )}
      </div>

      {pendingDelete && (
        <>
          <div
            onClick={() => setPendingDelete(null)}
            style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,0.6)", zIndex: 1000, backdropFilter: "blur(2px)" }}
          />
          <div
            style={{
              position: "fixed",
              top: "50%",
              left: "50%",
              transform: "translate(-50%, -50%)",
              width: "min(420px, 95vw)",
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 14,
              zIndex: 1001,
              padding: "24px 28px",
            }}
          >
            <h3 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: "0 0 8px" }}>
              {t("deleteDialog.title")}
            </h3>
            <p style={{ color: "#6B7280", fontSize: 13, margin: "0 0 20px" }}>
              {t("deleteDialog.body", { name: pendingDelete.name })}
            </p>
            <div style={{ display: "flex", gap: 10, justifyContent: "flex-end" }}>
              <Btn type="button" variant="ghost" onClick={() => setPendingDelete(null)}>
                {t("deleteDialog.cancel")}
              </Btn>
              <Btn type="button" variant="danger" onClick={confirmDelete} disabled={deleteCategory.isPending}>
                {deleteCategory.isPending ? t("deleteDialog.deleting") : t("deleteDialog.confirm")}
              </Btn>
            </div>
          </div>
        </>
      )}
    </>
  );
}

function IconButton({
  children,
  title,
  onClick,
  danger,
}: {
  children: React.ReactNode;
  title: string;
  onClick: () => void;
  danger?: boolean;
}) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: 28,
        height: 28,
        borderRadius: 7,
        background: "transparent",
        border: "1px solid #1F2937",
        color: danger ? "#F87171" : "#6B7280",
        cursor: "pointer",
      }}
    >
      {children}
    </button>
  );
}
