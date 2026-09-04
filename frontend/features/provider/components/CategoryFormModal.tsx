"use client";

import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { ALL_BUSINESS_TYPES, type CategoryDefaults, type PlatformCategoryDto } from "../types";
import { useCreateCategory, useUpdateCategory } from "../hooks/useProviderCategories";
import { flattenPlatformTree, indentLabel, subtreeIds } from "../lib/categoryTree";

const PERISHABILITY_VALUES = ["fresh", "chilled", "standard", "durable"] as const;
const ITEM_TYPE_VALUES = ["product", "service", "spare_part", "consumable", "raw_material", "kit"] as const;

interface Props {
  /** null → create mode; a row → edit mode. */
  category: PlatformCategoryDto | null;
  /** Preset parent for "Add sub-category" (create mode only). */
  presetParentId?: string | null;
  allCategories: PlatformCategoryDto[];
  onClose: () => void;
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
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

export function CategoryFormModal({ category, presetParentId, allCategories, onClose }: Props) {
  const t = useTranslations("Dashboard.providerCategories");
  const tBusinessTypes = useTranslations("Dashboard.provider.businessTypes");
  const tForm = useTranslations("Dashboard.inventory.form");
  const tItemTypes = useTranslations("Dashboard.inventory.itemTypes");
  const isEditing = category !== null;

  const createCategory = useCreateCategory();
  const updateCategory = useUpdateCategory();

  const [name, setName] = useState(category?.name ?? "");
  const [parentId, setParentId] = useState<string>(
    category?.parentId ?? presetParentId ?? "",
  );
  const [businessTypes, setBusinessTypes] = useState<string[]>(category?.businessTypes ?? []);
  const [sortOrder, setSortOrder] = useState<string>(String(category?.sortOrder ?? 0));
  const [isActive, setIsActive] = useState(category?.isActive ?? true);
  const [error, setError] = useState<string | null>(null);

  // ── Item-attribute defaults (Slice 2) ──────────────────────────────────
  const d = category?.defaults;
  const [defVat, setDefVat] = useState<string>(d?.vatRate != null ? String(d.vatRate) : "");
  const [defClass, setDefClass] = useState<string>(d?.perishabilityClass ?? "");
  const [defMgmt, setDefMgmt] = useState<string>(d?.managementType ?? "");
  const [defItemType, setDefItemType] = useState<string>(d?.itemType ?? "");
  const [defShelf, setDefShelf] = useState<string>(d?.shelfLifeDays != null ? String(d.shelfLifeDays) : "");

  // Parent options: the whole tree minus the node itself and its descendants (server also
  // guards against cycles, this just keeps the obvious ones out of the picker).
  const excluded = useMemo(
    () => (isEditing && category ? subtreeIds(allCategories, category.id) : new Set<string>()),
    [isEditing, category, allCategories],
  );
  const parentOptions = useMemo(
    () => flattenPlatformTree(allCategories).filter(({ category: c }) => !excluded.has(c.id)),
    [allCategories, excluded],
  );

  const isPending = createCategory.isPending || updateCategory.isPending;

  function toggleBusinessType(bt: string) {
    setBusinessTypes((prev) =>
      prev.includes(bt) ? prev.filter((x) => x !== bt) : [...prev, bt],
    );
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const trimmed = name.trim();
    if (!trimmed) {
      setError(t("modal.errorNameRequired"));
      return;
    }
    const parsedSort = Number.parseInt(sortOrder, 10);
    const sort = Number.isFinite(parsedSort) ? parsedSort : 0;

    const num = (s: string) => (s.trim() !== "" && Number.isFinite(Number(s)) ? Number(s) : null);
    const defaults: CategoryDefaults = {
      vatRate: num(defVat),
      perishabilityClass: defClass || null,
      managementType: defMgmt || null,
      itemType: defItemType || null,
      shelfLifeDays: num(defShelf),
    };

    try {
      if (isEditing && category) {
        await updateCategory.mutateAsync({
          id: category.id,
          body: {
            name: trimmed,
            parentId: parentId || null,
            businessTypes,
            sortOrder: sort,
            isActive,
            defaults,
          },
        });
        toast.success(t("toasts.updated"));
      } else {
        await createCategory.mutateAsync({
          name: trimmed,
          parentId: parentId || null,
          businessTypes,
          sortOrder: sort,
          defaults,
        });
        toast.success(t("toasts.created"));
      }
      onClose();
    } catch (err) {
      const message = (err as Error)?.message ?? t("toasts.error");
      setError(message);
      toast.error(message);
    }
  }

  return (
    <Modal title={isEditing ? t("modal.editTitle") : t("modal.createTitle")} onClose={onClose} width={460}>
      <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 16 }}>
        {error && (
          <div
            style={{
              color: "#F87171",
              fontSize: 13,
              background: "#1F1211",
              border: "1px solid #7F1D1D",
              borderRadius: 8,
              padding: "10px 14px",
            }}
          >
            {error}
          </div>
        )}

        {/* Name */}
        <div>
          <label style={labelStyle}>{t("modal.nameLabel")}</label>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t("modal.namePlaceholder")}
            autoFocus
            style={inputStyle}
          />
        </div>

        {/* Parent */}
        <div>
          <label style={labelStyle}>{t("modal.parentLabel")}</label>
          <select
            value={parentId}
            onChange={(e) => setParentId(e.target.value)}
            style={{ ...inputStyle, cursor: "pointer" }}
          >
            <option value="">— {t("modal.parentNone")} —</option>
            {parentOptions.map(({ category: c, depth }) => (
              <option key={c.id} value={c.id}>
                {indentLabel(c.name, depth)}
              </option>
            ))}
          </select>
        </div>

        {/* Business types */}
        <div>
          <label style={labelStyle}>{t("modal.businessTypesLabel")}</label>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "1fr 1fr",
              gap: 6,
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 8,
              padding: 10,
            }}
          >
            {ALL_BUSINESS_TYPES.map((bt) => (
              <label
                key={bt}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 7,
                  color: "#D1D5DB",
                  fontSize: 12,
                  cursor: "pointer",
                }}
              >
                <input
                  type="checkbox"
                  checked={businessTypes.includes(bt)}
                  onChange={() => toggleBusinessType(bt)}
                  style={{ accentColor: "#3B82F6", width: 15, height: 15, cursor: "pointer" }}
                />
                {tBusinessTypes.has(bt) ? tBusinessTypes(bt) : bt}
              </label>
            ))}
          </div>
          {businessTypes.length === 0 && (
            <p style={{ color: "#6B7280", fontSize: 11, margin: "6px 0 0" }}>{t("modal.businessTypesAllHint")}</p>
          )}
        </div>

        {/* Sort order */}
        <div>
          <label style={labelStyle}>{t("modal.sortOrderLabel")}</label>
          <input
            type="number"
            value={sortOrder}
            onChange={(e) => setSortOrder(e.target.value)}
            style={inputStyle}
          />
        </div>

        {/* Item-attribute defaults (Slice 2) */}
        <div style={{ border: "1px solid #1F2937", borderRadius: 8, padding: 12 }}>
          <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
            {t("modal.defaultsTitle")}
          </div>
          <p style={{ color: "#6B7280", fontSize: 11, margin: "0 0 10px" }}>{t("modal.defaultsHint")}</p>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <div>
              <label style={labelStyle}>{tForm("vatRateLabel")}</label>
              <input
                type="number" step="0.01" min="0" max="100"
                value={defVat}
                onChange={(e) => setDefVat(e.target.value)}
                placeholder={t("modal.defaultsNone")}
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>{tForm("shelfLifeLabel")}</label>
              <input
                type="number" min="1"
                value={defShelf}
                onChange={(e) => setDefShelf(e.target.value)}
                placeholder={t("modal.defaultsNone")}
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>{tForm("perishabilityLabel")}</label>
              <select value={defClass} onChange={(e) => setDefClass(e.target.value)} style={{ ...inputStyle, cursor: "pointer" }}>
                <option value="">— {t("modal.defaultsNone")} —</option>
                {PERISHABILITY_VALUES.map((v) => (
                  <option key={v} value={v}>{tForm(`perishability.${v}`)}</option>
                ))}
              </select>
            </div>
            <div>
              <label style={labelStyle}>{tForm("managementTypeLabel")}</label>
              <select value={defMgmt} onChange={(e) => setDefMgmt(e.target.value)} style={{ ...inputStyle, cursor: "pointer" }}>
                <option value="">— {t("modal.defaultsNone")} —</option>
                <option value="MTS">{tForm("managementTypeMts")}</option>
                <option value="MTO">{tForm("managementTypeMto")}</option>
              </select>
            </div>
            <div style={{ gridColumn: "1 / -1" }}>
              <label style={labelStyle}>{tForm("itemTypeLabel")}</label>
              <select value={defItemType} onChange={(e) => setDefItemType(e.target.value)} style={{ ...inputStyle, cursor: "pointer" }}>
                <option value="">— {t("modal.defaultsNone")} —</option>
                {ITEM_TYPE_VALUES.map((v) => (
                  <option key={v} value={v}>{tItemTypes.has(v) ? tItemTypes(v) : v}</option>
                ))}
              </select>
            </div>
          </div>
        </div>

        {/* Active — edit only */}
        {isEditing && (
          <label style={{ display: "flex", alignItems: "center", gap: 9, color: "#D1D5DB", fontSize: 13, cursor: "pointer" }}>
            <input
              type="checkbox"
              checked={isActive}
              onChange={(e) => setIsActive(e.target.checked)}
              style={{ accentColor: "#3B82F6", width: 16, height: 16, cursor: "pointer" }}
            />
            {t("modal.activeLabel")}
          </label>
        )}

        {/* Actions */}
        <div style={{ display: "flex", gap: 10, justifyContent: "flex-end", marginTop: 4 }}>
          <Btn type="button" variant="ghost" onClick={onClose}>
            {t("modal.cancel")}
          </Btn>
          <Btn type="submit" disabled={isPending}>
            {isPending ? t("modal.saving") : isEditing ? t("modal.save") : t("modal.create")}
          </Btn>
        </div>
      </form>
    </Modal>
  );
}
