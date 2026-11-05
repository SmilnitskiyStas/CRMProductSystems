"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { inputStyle, labelStyle } from "@/components/ui/fieldStyles";
import { useAddCabinetItem, useUpdateCabinetItem } from "../hooks/useSupplierCabinet";
import { useItemCategories } from "@/features/marketplace/hooks/useMarketplace";
import { ItemCategoryFields, findMissingRequiredField } from "@/features/marketplace/components/ItemCategoryFields";
import {
  SupplierItemExtraFields,
  extraFieldsFromItem,
  parseExtraFields,
} from "@/features/marketplace/components/SupplierItemExtraFields";
import {
  CategoryTypeahead,
  type CategoryRef,
} from "@/features/catalog/components/CategoryTypeahead";
import type { CabinetItem } from "../types";

/** Sentinel the cabinet update endpoint reads as "clear the platform category" (Phase 6e). */
const CLEAR_PLATFORM_CATEGORY = "00000000-0000-0000-0000-000000000000";

interface Props {
  /** When set — edit mode; otherwise create mode. */
  item?: CabinetItem;
  onClose: () => void;
}

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
  const initialPlatformCategoryId = item?.platformCategoryId ?? null;
  const [platformCategory, setPlatformCategory] = useState<CategoryRef | null>(
    item?.platformCategoryId
      ? { id: item.platformCategoryId, name: item.platformCategoryName ?? "" }
      : null
  );
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

    // Phase 6e — browse-taxonomy link. Add mode: send the id only when one is picked.
    // Edit mode (patch): send nothing when unchanged, the clear-sentinel when removed,
    // the new id when changed.
    const currentPlatformCategoryId = platformCategory?.id ?? null;
    let platformCategoryId: string | undefined;
    if (isEdit) {
      if (currentPlatformCategoryId !== initialPlatformCategoryId) {
        platformCategoryId = currentPlatformCategoryId ?? CLEAR_PLATFORM_CATEGORY;
      }
    } else if (currentPlatformCategoryId) {
      platformCategoryId = currentPlatformCategoryId;
    }

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
            platformCategoryId,
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
          platformCategoryId,
          ...categoryFields,
          ...extraFields,
        },
        { onSuccess: onClose, onError }
      );
    }
  }

  return (
    <Modal title={isEdit ? t("titleEdit") : t("titleAdd")} onClose={onClose} width={560}>
        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={labelStyle}>
              {t("nameLabel")} <span style={{ color: "#F87171" }}>*</span>
            </label>
            <input
              type="text"
              value={customName}
              onChange={(e) => setCustomName(e.target.value)}
              placeholder={t("namePlaceholder")}
              style={inputStyle}
              required
            />
          </div>

          <ItemCategoryFields
            category={category}
            onCategoryChange={setCategory}
            attributes={attributes}
            onAttributesChange={setAttributes}
          />

          <div>
            <label style={labelStyle} htmlFor="cabinetItemPlatformCategory">
              {t("platformCategoryLabel")}
            </label>
            <CategoryTypeahead
              inputId="cabinetItemPlatformCategory"
              value={platformCategory}
              onChange={setPlatformCategory}
              placeholder={t("platformCategoryPlaceholder")}
            />
            <p style={{ color: "#4B5563", fontSize: 11, marginTop: 6, marginBottom: 0 }}>
              {t("platformCategoryHint")}
            </p>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <div>
              <label style={labelStyle}>{t("priceLabel")}</label>
              <input
                type="number"
                min="0"
                step="0.01"
                value={priceRaw}
                onChange={(e) => setPriceRaw(e.target.value)}
                placeholder="0.00"
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>{t("minQtyLabel")}</label>
              <input
                type="number"
                min="1"
                step="1"
                value={minQtyRaw}
                onChange={(e) => setMinQtyRaw(e.target.value)}
                placeholder="1"
                style={inputStyle}
              />
            </div>
          </div>

          <div>
            <label style={labelStyle}>{t("unitLabel")}</label>
            <input
              type="text"
              value={unit}
              onChange={(e) => setUnit(e.target.value)}
              placeholder={t("unitPlaceholder")}
              style={inputStyle}
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
            <Btn variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Btn>
            <Btn type="submit" disabled={isPending}>
              {isPending ? t("saving") : isEdit ? t("save") : t("add")}
            </Btn>
          </div>
        </form>
    </Modal>
  );
}
