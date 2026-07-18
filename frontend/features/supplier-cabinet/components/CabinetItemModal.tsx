"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import { useAddCabinetItem, useUpdateCabinetItem } from "../hooks/useSupplierCabinet";
import { useItemCategories } from "@/features/marketplace/hooks/useMarketplace";
import { ItemCategoryFields, findMissingRequiredField } from "@/features/marketplace/components/ItemCategoryFields";
import {
  SupplierItemExtraFields,
  extraFieldsFromItem,
  parseExtraFields,
} from "@/features/marketplace/components/SupplierItemExtraFields";
import type { CabinetItem } from "../types";

interface Props {
  /** When set — edit mode; otherwise create mode. */
  item?: CabinetItem;
  onClose: () => void;
}

const INPUT_STYLE: React.CSSProperties = {
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

const LABEL_STYLE: React.CSSProperties = {
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
  display: "block",
};

export function CabinetItemModal({ item, onClose }: Props) {
  const t = useTranslations("Dashboard.supplierCabinet.itemModal");
  const tCategoryFields = useTranslations("Dashboard.marketplace.itemCategoryFields");
  const tExtraFields = useTranslations("Dashboard.marketplace.itemExtraFields");
  const isEdit = !!item;
  const addItem = useAddCabinetItem();
  const updateItem = useUpdateCabinetItem();
  const isPending = addItem.isPending || updateItem.isPending;

  const [customName, setCustomName] = useState(item?.customName ?? item?.itemName ?? "");
  const [priceRaw, setPriceRaw] = useState(item?.price != null ? String(item.price) : "");
  const [minQtyRaw, setMinQtyRaw] = useState(item?.minQty != null ? String(item.minQty) : "");
  const [unit, setUnit] = useState(item?.unit ?? "");
  const [isAvailable, setIsAvailable] = useState(item?.isAvailable ?? true);
  const [category, setCategory] = useState(item?.category ?? "");
  const [attributes, setAttributes] = useState<Record<string, string>>(
    (item?.attributes as Record<string, string>) ?? {}
  );
  const [extra, setExtra] = useState(() => extraFieldsFromItem(item));
  const [error, setError] = useState<string | null>(null);
  const { data: categories = [] } = useItemCategories();

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (!customName.trim()) {
      setError(t("errorNameRequired"));
      return;
    }

    const price = priceRaw ? parseFloat(priceRaw) : undefined;
    const minQty = minQtyRaw ? parseInt(minQtyRaw, 10) : undefined;

    if (priceRaw && (isNaN(price!) || price! < 0)) {
      setError(t("errorInvalidPrice"));
      return;
    }
    if (minQtyRaw && (isNaN(minQty!) || minQty! < 1)) {
      setError(t("errorInvalidMinQty"));
      return;
    }

    if (category) {
      const missing = findMissingRequiredField(categories, category, attributes, tCategoryFields);
      if (missing) {
        setError(missing);
        return;
      }
    }

    const parsedExtra = parseExtraFields(extra, tExtraFields);
    if (parsedExtra.error !== null) {
      setError(parsedExtra.error);
      return;
    }

    const onError = (err: unknown) =>
      setError(err instanceof Error ? err.message : t("errorSaveDefault"));

    const categoryFields = category
      ? { category, attributes: attributes as Record<string, unknown> }
      : {};

    const extraFields = {
      brand: parsedExtra.brand,
      manufacturer: parsedExtra.manufacturer,
      manufacturerCountry: parsedExtra.manufacturerCountry,
      maxQty: parsedExtra.maxQty,
      grossWeightKg: parsedExtra.grossWeightKg,
      heightCm: parsedExtra.heightCm,
      depthCm: parsedExtra.depthCm,
      widthCm: parsedExtra.widthCm,
      // Always send the current full list back on edit so an untouched list
      // isn't wiped by the backend's patch semantics (omitted = untouched).
      barcodes: parsedExtra.barcodes,
      imageUrls: parsedExtra.imageUrls,
    };

    if (isEdit) {
      updateItem.mutate(
        {
          id: item!.id,
          body: {
            customName: customName.trim(),
            price,
            minQty,
            unit: unit.trim() || undefined,
            isAvailable,
            ...categoryFields,
            ...extraFields,
          },
        },
        { onSuccess: onClose, onError }
      );
    } else {
      addItem.mutate(
        {
          customName: customName.trim(),
          price,
          minQty,
          unit: unit.trim() || undefined,
          isAvailable,
          ...categoryFields,
          ...extraFields,
        },
        { onSuccess: onClose, onError }
      );
    }
  }

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
          maxWidth: 560,
          maxHeight: "90vh",
          overflowY: "auto",
          display: "flex",
          flexDirection: "column",
          gap: 20,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>
            {isEdit ? t("titleEdit") : t("titleAdd")}
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

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={LABEL_STYLE}>
              {t("nameLabel")} <span style={{ color: "#F87171" }}>*</span>
            </label>
            <input
              type="text"
              value={customName}
              onChange={(e) => setCustomName(e.target.value)}
              placeholder={t("namePlaceholder")}
              style={INPUT_STYLE}
              required
            />
          </div>

          <ItemCategoryFields
            category={category}
            onCategoryChange={setCategory}
            attributes={attributes}
            onAttributesChange={setAttributes}
          />

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <div>
              <label style={LABEL_STYLE}>{t("priceLabel")}</label>
              <input
                type="number"
                min="0"
                step="0.01"
                value={priceRaw}
                onChange={(e) => setPriceRaw(e.target.value)}
                placeholder="0.00"
                style={INPUT_STYLE}
              />
            </div>
            <div>
              <label style={LABEL_STYLE}>{t("minQtyLabel")}</label>
              <input
                type="number"
                min="1"
                step="1"
                value={minQtyRaw}
                onChange={(e) => setMinQtyRaw(e.target.value)}
                placeholder="1"
                style={INPUT_STYLE}
              />
            </div>
          </div>

          <div>
            <label style={LABEL_STYLE}>{t("unitLabel")}</label>
            <input
              type="text"
              value={unit}
              onChange={(e) => setUnit(e.target.value)}
              placeholder={t("unitPlaceholder")}
              style={INPUT_STYLE}
            />
          </div>

          <SupplierItemExtraFields value={extra} onChange={setExtra} />

          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <input
              type="checkbox"
              id="cabinetItemAvailable"
              checked={isAvailable}
              onChange={(e) => setIsAvailable(e.target.checked)}
              style={{ width: 16, height: 16, cursor: "pointer" }}
            />
            <label
              htmlFor="cabinetItemAvailable"
              style={{ color: "#E8EDF5", fontSize: 13, cursor: "pointer" }}
            >
              {t("availableLabel")}
            </label>
          </div>

          {error && <div style={{ color: "#F87171", fontSize: 13 }}>{error}</div>}

          <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 4 }}>
            <button
              type="button"
              onClick={onClose}
              style={{
                padding: "9px 20px",
                borderRadius: 8,
                border: "1px solid #1F2937",
                background: "transparent",
                color: "#6B7280",
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
                padding: "9px 20px",
                borderRadius: 8,
                border: "none",
                background: "#1D4ED8",
                color: "#E8EDF5",
                fontSize: 13,
                fontWeight: 600,
                cursor: isPending ? "not-allowed" : "pointer",
                opacity: isPending ? 0.7 : 1,
              }}
            >
              {isPending ? t("saving") : isEdit ? t("save") : t("add")}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
