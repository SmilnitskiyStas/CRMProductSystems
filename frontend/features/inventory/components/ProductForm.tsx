"use client";

import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import type { CreateProductPayload, Product, UpdateProductPayload } from "../types";
import { productsApi } from "../api/products";
import { useCategories } from "../hooks/useCategories";
import { flattenTree, indentLabel } from "../lib/categoryTree";

export const PERISHABILITY_CLASS_VALUES = ["fresh", "chilled", "standard", "durable"] as const;

export const ITEM_TYPE_VALUES = [
  "product",
  "service",
  "spare_part",
  "consumable",
  "raw_material",
  "kit",
] as const;

// Zod .min(1, message) needs a translated message, but next-intl's `useTranslations` is a
// hook and this schema is built once per render inside the component (see
// `useMemo(() => buildProductSchema(t), [t])` below) rather than at module scope — mirrors
// the `buildNavGroups(t)` pattern in components/layout/Sidebar.tsx (i18n Block 1).
function buildProductSchema(t: ReturnType<typeof useTranslations>) {
  return z.object({
    name:           z.string().min(1, t("validationRequired")).max(200),
    unit:           z.string().min(1, t("validationRequired")).max(50),
    categoryId:     z.string().optional(),
    managementType: z.enum(["MTS", "MTO"]),
    itemType:       z.string().min(1),
    minStock:       z.coerce.number().min(0),
    maxStock:       z.coerce.number().min(0),
    safetyBuffer:   z.coerce.number().min(0),
    shelfLifeDays:  z.coerce.number().int().min(1).optional().or(z.literal("")),
    vatRate:        z.coerce.number().min(0).max(100),
    pricePurchase:  z.coerce.number().min(0).optional().or(z.literal("")),
    priceRetail:    z.coerce.number().min(0).optional().or(z.literal("")),
    isActive:       z.boolean(),
    manufacturer:        z.string().max(255).optional(),
    countryOrigin:       z.string().max(100).optional(),
    perishabilityClass:  z.string().min(1),
  });
}

type FormValues = z.infer<ReturnType<typeof buildProductSchema>>;

// `unit` default is locale-aware ("шт"/"pcs") so a brand-new product form doesn't show a
// Ukrainian abbreviation in the English UI — everything else here is language-neutral.
function buildDefaultValues(locale: string): FormValues {
  return {
    name: "", unit: locale === "en" ? "pcs" : "шт", categoryId: "", managementType: "MTS", itemType: "product",
    minStock: 0, maxStock: 100, safetyBuffer: 5,
    shelfLifeDays: "", vatRate: 20,
    pricePurchase: "", priceRetail: "",
    isActive: true,
    manufacturer: "",
    countryOrigin: "",
    perishabilityClass: "standard",
  };
}

interface Props {
  open: boolean;
  product: Product | null;
  isPending: boolean;
  onClose: () => void;
  onCreate: (payload: CreateProductPayload) => void;
  onUpdate: (id: string, payload: UpdateProductPayload) => void;
  onImageUpload?: (id: string, file: File) => void;
}

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px",
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 5,
};

export function ProductForm({ open, product, isPending, onClose, onCreate, onUpdate, onImageUpload }: Props) {
  const t = useTranslations("Dashboard.inventory.form");
  const tItemTypes = useTranslations("Dashboard.inventory.itemTypes");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const defaultValues = useMemo(() => buildDefaultValues(locale), [locale]);
  const isEditing = product !== null;

  const [barcodes, setBarcodes] = useState<string[]>([]);
  const [barcodeInput, setBarcodeInput] = useState("");
  const [imageFile, setImageFile] = useState<File | null>(null);
  const [imagePreview, setImagePreview] = useState<string | null>(null);
  const [lookupLoading, setLookupLoading] = useState(false);
  const [lookupError, setLookupError] = useState<string | null>(null);

  const productSchema = useMemo(() => buildProductSchema(t), [t]);
  const { data: categories = [] } = useCategories();
  const categoryOptions = useMemo(() => flattenTree(categories), [categories]);
  // The current item may sit on a category the tenant can no longer pick from the list — the
  // provider retagged it to another business type or soft-deleted it. Surface it as its own
  // option so the field isn't blank and an unrelated edit doesn't silently drop the link
  // (QA BUG-2); the backend grandfathers an unchanged category (QA BUG-1).
  const orphanCategoryOption =
    product?.categoryId && !categoryOptions.some(({ category }) => category.id === product.categoryId)
      ? { id: product.categoryId, name: product.categoryName ?? product.categoryId }
      : null;

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(productSchema),
    defaultValues,
  });

  useEffect(() => {
    reset(
      product
        ? {
            name:           product.name,
            unit:           product.unit,
            categoryId:     product.categoryId ?? "",
            managementType: (product.managementType as "MTS" | "MTO") ?? "MTS",
            itemType:       product.itemType ?? "product",
            minStock:       product.minStock,
            maxStock:       product.maxStock,
            safetyBuffer:   product.safetyBuffer,
            shelfLifeDays:  product.shelfLifeDays ?? "",
            vatRate:        product.vatRate,
            pricePurchase:  product.pricePurchase ?? "",
            priceRetail:    product.priceRetail ?? "",
            isActive:       product.isActive,
            manufacturer:        product.manufacturer ?? "",
            countryOrigin:       product.countryOrigin ?? "",
            perishabilityClass:  product.perishabilityClass ?? "standard",
          }
        : defaultValues,
    );
    if (product) {
      setBarcodes(product.barcodes ?? []);
      setImagePreview(product.imageUrl ?? null);
    } else {
      setBarcodes([]);
      setImagePreview(null);
    }
    setImageFile(null);
    setBarcodeInput("");
    setLookupError(null);
  }, [product, reset]);

  if (!open) return null;

  function addBarcode() {
    const v = barcodeInput.trim();
    if (v && !barcodes.includes(v)) setBarcodes(prev => [...prev, v]);
    setBarcodeInput("");
  }

  function removeBarcode(b: string) {
    setBarcodes(prev => prev.filter(x => x !== b));
  }

  // First barcode in the list is the primary/active one (project-wide convention — POS,
  // receipts and analytics all read Barcodes[0]). "Make primary" moves it to the front.
  function makePrimary(b: string) {
    setBarcodes(prev => [b, ...prev.filter(x => x !== b)]);
  }

  async function handleLookup() {
    const bc = barcodes[0] ?? barcodeInput.trim();
    if (!bc) return;
    setLookupLoading(true);
    setLookupError(null);
    try {
      const data = await productsApi.lookupByBarcode(bc);
      setValue("name", data.name || "");
      if (data.manufacturer) setValue("manufacturer", data.manufacturer);
      if (data.countryOrigin) setValue("countryOrigin", data.countryOrigin);
      if (data.unit) setValue("unit", data.unit);
      if (data.shelfLifeDays) setValue("shelfLifeDays", data.shelfLifeDays);
      if (data.barcodes.length > 0) setBarcodes(data.barcodes);
      if (data.imageUrl) setImagePreview(data.imageUrl);
    } catch {
      setLookupError(t("lookupError"));
    } finally {
      setLookupLoading(false);
    }
  }

  const onSubmit = (values: FormValues) => {
    const payload: CreateProductPayload = {
      name:          values.name,
      barcodes:      barcodes.length > 0 ? barcodes : undefined,
      unit:          values.unit,
      categoryId:    values.categoryId || undefined,
      managementType: values.managementType,
      itemType:      values.itemType,
      minStock:      values.minStock,
      maxStock:      values.maxStock,
      safetyBuffer:  values.safetyBuffer,
      shelfLifeDays: values.shelfLifeDays !== "" ? Number(values.shelfLifeDays) : undefined,
      vatRate:       values.vatRate,
      pricePurchase: values.pricePurchase !== "" ? Number(values.pricePurchase) : undefined,
      priceRetail:   values.priceRetail !== "" ? Number(values.priceRetail) : undefined,
      manufacturer:       values.manufacturer || undefined,
      countryOrigin:      values.countryOrigin || undefined,
      perishabilityClass: values.perishabilityClass,
    };

    if (isEditing) {
      onUpdate(product.id, { ...payload, isActive: values.isActive });
      if (imageFile && onImageUpload) {
        onImageUpload(product.id, imageFile);
      }
    } else {
      onCreate(payload);
    }
  };

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed", inset: 0,
          background: "rgba(0,0,0,0.6)",
          zIndex: 300,
          backdropFilter: "blur(2px)",
        }}
      />

      {/* Modal */}
      <div
        style={{
          position: "fixed",
          top: "50%", left: "50%",
          transform: "translate(-50%, -50%)",
          width: "min(560px, 95vw)",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 14,
          zIndex: 301,
          display: "flex",
          flexDirection: "column",
          maxHeight: "90vh",
          overflowY: "auto",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "18px 22px",
            borderBottom: "1px solid #1F2937",
            position: "sticky",
            top: 0,
            background: "#0D1117",
            zIndex: 1,
          }}
        >
          <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
            {isEditing ? t("titleEdit") : t("titleCreate")}
          </h2>
          <button
            onClick={onClose}
            style={{
              background: "transparent", border: "1px solid #1F2937",
              borderRadius: 8, padding: "5px 9px",
              color: "#4B5563", fontSize: 16, cursor: "pointer",
            }}
          >
            ✕
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit(onSubmit)} style={{ padding: 22 }}>
          <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            {/* Name */}
            <div>
              <label style={labelStyle}>{t("nameLabel")}</label>
              <input {...register("name")} placeholder={t("namePlaceholder")} style={inputStyle} />
              {errors.name && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 3 }}>{errors.name.message}</p>
              )}
            </div>

            {/* Штрихкоди */}
            <div>
              <label style={labelStyle}>{t("barcodesLabel")}</label>
              {barcodes.length > 1 && (
                <p style={{ color: "#4B5563", fontSize: 11, margin: "0 0 6px" }}>{t("barcodePrimaryHint")}</p>
              )}

              {/* Теги */}
              {barcodes.length > 0 && (
                <div style={{ display: "flex", flexWrap: "wrap", gap: 6, marginBottom: 8 }}>
                  {barcodes.map((b, i) => {
                    const isPrimary = i === 0;
                    return (
                      <span key={b} style={{
                        display: "flex", alignItems: "center", gap: 4,
                        background: isPrimary ? "#0F2D1A" : "#0F1F3D",
                        border: `1px solid ${isPrimary ? "#166534" : "#1E3A5F"}`,
                        borderRadius: 6, padding: "3px 8px",
                        color: isPrimary ? "#4ADE80" : "#93C5FD", fontSize: 12,
                      }}>
                        {isPrimary ? (
                          <span title={t("barcodePrimaryHint")} style={{ fontSize: 11 }}>★</span>
                        ) : (
                          <button type="button" onClick={() => makePrimary(b)} title={t("barcodeMakePrimary")}
                            style={{ background: "none", border: "none", color: "#60A5FA", cursor: "pointer", padding: 0, fontSize: 12, lineHeight: 1 }}>
                            ☆
                          </button>
                        )}
                        {b}
                        <button type="button" onClick={() => removeBarcode(b)}
                          style={{ background: "none", border: "none", color: isPrimary ? "#4ADE80" : "#60A5FA", cursor: "pointer", padding: 0, fontSize: 13, lineHeight: 1 }}>
                          ×
                        </button>
                      </span>
                    );
                  })}
                </div>
              )}

              {/* Додати */}
              <div style={{ display: "flex", gap: 6 }}>
                <input
                  value={barcodeInput}
                  onChange={e => setBarcodeInput(e.target.value)}
                  onKeyDown={e => e.key === "Enter" && (e.preventDefault(), addBarcode())}
                  placeholder={t("barcodePlaceholder")}
                  style={{ ...inputStyle, flex: 1 }}
                />
                <button type="button" onClick={addBarcode}
                  style={{
                    padding: "8px 12px", borderRadius: 8, cursor: "pointer",
                    background: "#1D3461", border: "1px solid #3B82F6",
                    color: "#93C5FD", fontSize: 13, whiteSpace: "nowrap",
                  }}>
                  +
                </button>
                <button type="button" onClick={handleLookup} disabled={lookupLoading}
                  style={{
                    padding: "8px 12px", borderRadius: 8, cursor: lookupLoading ? "default" : "pointer",
                    background: lookupLoading ? "#1F2937" : "#0F2D1A", border: "1px solid #166534",
                    color: lookupLoading ? "#4B5563" : "#4ADE80", fontSize: 12, whiteSpace: "nowrap",
                  }}>
                  {lookupLoading ? t("lookupSearching") : t("lookupFind")}
                </button>
              </div>
              {lookupError && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{lookupError}</p>}
            </div>

            {/* Зображення */}
            <div>
              <label style={labelStyle}>{t("imageLabel")}</label>
              <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
                {imagePreview && (
                  <img
                    src={imagePreview} alt="preview"
                    style={{ width: 60, height: 60, objectFit: "cover", borderRadius: 8, border: "1px solid #1F2937" }}
                  />
                )}
                <label style={{
                  padding: "8px 14px", borderRadius: 8, cursor: "pointer",
                  background: "#111827", border: "1px solid #374151",
                  color: "#9CA3AF", fontSize: 12,
                }}>
                  {imagePreview ? t("imageChange") : t("imageUpload")}
                  <input
                    type="file"
                    accept="image/*"
                    style={{ display: "none" }}
                    onChange={e => {
                      const file = e.target.files?.[0];
                      if (!file) return;
                      setImageFile(file);
                      setImagePreview(URL.createObjectURL(file));
                    }}
                  />
                </label>
                {imagePreview && (
                  <button type="button" onClick={() => { setImageFile(null); setImagePreview(null); }}
                    style={{ background: "none", border: "none", color: "#EF4444", cursor: "pointer", fontSize: 12 }}>
                    {t("imageRemove")}
                  </button>
                )}
              </div>
            </div>

            {/* Unit */}
            <div>
              <label style={labelStyle}>{t("unitLabel")}</label>
              <input {...register("unit")} placeholder={t("unitPlaceholder")} style={inputStyle} />
              {errors.unit && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 3 }}>{errors.unit.message}</p>
              )}
            </div>

            {/* Category */}
            <div>
              <label style={labelStyle}>{t("categoryLabel")}</label>
              <select {...register("categoryId")} style={{ ...inputStyle, cursor: "pointer" }}>
                <option value="">— {t("categoryNone")} —</option>
                {orphanCategoryOption && (
                  <option value={orphanCategoryOption.id}>{orphanCategoryOption.name}</option>
                )}
                {categoryOptions.map(({ category, depth }) => (
                  <option key={category.id} value={category.id}>
                    {indentLabel(category.name, depth)}
                  </option>
                ))}
              </select>
            </div>

            {/* ManagementType + ItemType */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>{t("managementTypeLabel")}</label>
                <select {...register("managementType")} style={{ ...inputStyle, cursor: "pointer" }}>
                  <option value="MTS">{t("managementTypeMts")}</option>
                  <option value="MTO">{t("managementTypeMto")}</option>
                </select>
              </div>
              <div>
                <label style={labelStyle}>{t("itemTypeLabel")}</label>
                <select {...register("itemType")} style={{ ...inputStyle, cursor: "pointer" }}>
                  {ITEM_TYPE_VALUES.map((value) => (
                    <option key={value} value={value}>{tItemTypes(value)}</option>
                  ))}
                </select>
              </div>
            </div>

            {/* ShelfLife + VatRate */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>{t("shelfLifeLabel")}</label>
                <input {...register("shelfLifeDays")} type="number" min="1" placeholder="7" style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>{t("vatRateLabel")}</label>
                <input {...register("vatRate")} type="number" step="0.01" min="0" max="100" style={inputStyle} />
              </div>
            </div>

            {/* PerishabilityClass */}
            <div>
              <label style={labelStyle}>{t("perishabilityLabel")}</label>
              <select {...register("perishabilityClass")} style={{ ...inputStyle, cursor: "pointer" }}>
                {PERISHABILITY_CLASS_VALUES.map((value) => (
                  <option key={value} value={value}>{t(`perishability.${value}`)}</option>
                ))}
              </select>
            </div>

            {/* Manufacturer + CountryOrigin */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>{t("manufacturerLabel")}</label>
                <input {...register("manufacturer")} placeholder={t("manufacturerPlaceholder")} style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>{t("countryOriginLabel")}</label>
                <input {...register("countryOrigin")} placeholder={t("countryOriginPlaceholder")} style={inputStyle} />
              </div>
            </div>

            {/* Stock levels */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>{t("minStockLabel")}</label>
                <input {...register("minStock")} type="number" step="0.01" min="0" style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>{t("maxStockLabel")}</label>
                <input {...register("maxStock")} type="number" step="0.01" min="0" style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>{t("safetyBufferLabel")}</label>
                <input {...register("safetyBuffer")} type="number" step="0.01" min="0" style={inputStyle} />
              </div>
            </div>

            {/* Prices */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>{t("pricePurchaseLabel")}</label>
                <input {...register("pricePurchase")} type="number" step="0.01" min="0" placeholder="0.00" style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>{t("priceRetailLabel")}</label>
                <input {...register("priceRetail")} type="number" step="0.01" min="0" placeholder="0.00" style={inputStyle} />
              </div>
            </div>

            {/* isActive — edit only */}
            {isEditing && (
              <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                <input
                  type="checkbox"
                  id="isActive"
                  {...register("isActive")}
                  style={{ accentColor: "#3B82F6", width: 16, height: 16, cursor: "pointer" }}
                />
                <label htmlFor="isActive" style={{ ...labelStyle, marginBottom: 0, cursor: "pointer" }}>
                  {t("isActiveLabel")}
                </label>
              </div>
            )}
          </div>

          {/* Actions */}
          <div style={{ display: "flex", gap: 10, marginTop: 22, justifyContent: "flex-end" }}>
            <Btn variant="ghost" type="button" onClick={onClose}>
              {tCommon("cancel")}
            </Btn>
            <Btn type="submit" disabled={isPending}>
              {isPending ? t("saving") : isEditing ? t("saveChanges") : t("titleCreate")}
            </Btn>
          </div>
        </form>
      </div>
    </>
  );
}
