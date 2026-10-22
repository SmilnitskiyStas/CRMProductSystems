"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useAddSupplierItem, useItemCategories } from "../hooks/useMarketplace";
import { ItemCategoryFields, findMissingRequiredField } from "./ItemCategoryFields";
import {
  SupplierItemExtraFields,
  emptyExtraFields,
  parseExtraFields,
} from "./SupplierItemExtraFields";
import type { AddSupplierItemRequest } from "../types";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { inputStyle, labelStyle } from "@/components/ui/fieldStyles";

interface Props {
  supplierId: string;
  onClose: () => void;
}

export function AddSupplierItemModal({ supplierId, onClose }: Props) {
  const t = useTranslations("Dashboard.marketplace.addItemModal");
  const tCategoryFields = useTranslations("Dashboard.marketplace.itemCategoryFields");
  const tExtraFields = useTranslations("Dashboard.marketplace.itemExtraFields");
  const addItem = useAddSupplierItem(supplierId);

  const [customName, setCustomName] = useState("");
  const [priceRaw, setPriceRaw] = useState("");
  const [minQtyRaw, setMinQtyRaw] = useState("");
  const [unit, setUnit] = useState("");
  const [isAvailable, setIsAvailable] = useState(true);
  const [category, setCategory] = useState("");
  const [attributes, setAttributes] = useState<Record<string, string>>({});
  const [extra, setExtra] = useState(emptyExtraFields());
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

    const body: AddSupplierItemRequest = {
      customName: customName.trim(),
      price,
      minQty,
      unit: unit.trim() || undefined,
      isAvailable,
      ...(category ? { category, attributes: attributes as Record<string, unknown> } : {}),
      brand: parsedExtra.brand,
      manufacturer: parsedExtra.manufacturer,
      manufacturerCountry: parsedExtra.manufacturerCountry,
      maxQty: parsedExtra.maxQty,
      grossWeightKg: parsedExtra.grossWeightKg,
      heightCm: parsedExtra.heightCm,
      depthCm: parsedExtra.depthCm,
      widthCm: parsedExtra.widthCm,
      barcodes: parsedExtra.barcodes,
      imageUrls: parsedExtra.imageUrls,
    };

    addItem.mutate(body, {
      onSuccess: () => onClose(),
      onError: (err: any) => {
        setError(err?.message ?? t("errorSaveDefault"));
      },
    });
  }

  return (
    <Modal title={t("title")} onClose={onClose} width={560}>
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
              id="isAvailable"
              checked={isAvailable}
              onChange={(e) => setIsAvailable(e.target.checked)}
              style={{ width: 16, height: 16, cursor: "pointer" }}
            />
            <label
              htmlFor="isAvailable"
              style={{ color: "#E8EDF5", fontSize: 13, cursor: "pointer" }}
            >
              {t("availableLabel")}
            </label>
          </div>

          {error && (
            <div style={{ color: "#F87171", fontSize: 13 }}>{error}</div>
          )}

          <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 4 }}>
            <Btn type="button" variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Btn>
            <Btn type="submit" disabled={addItem.isPending}>
              {addItem.isPending ? t("saving") : t("add")}
            </Btn>
          </div>
        </form>
    </Modal>
  );
}
