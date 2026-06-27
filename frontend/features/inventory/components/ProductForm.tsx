"use client";

import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Btn } from "@/components/ui/Btn";
import type { CreateProductPayload, Product, UpdateProductPayload } from "../types";
import { productsApi } from "../api/products";

export const PERISHABILITY_CLASS_OPTIONS: { value: string; label: string }[] = [
  { value: "fresh",    label: "🥦 Fresh — овочі, фрукти, хліб (≤1/3 дні)" },
  { value: "chilled",  label: "🥛 Chilled — молоко, м'ясо, риба (≤2/5 дні)" },
  { value: "standard", label: "📦 Standard — пакована продукція (≤6/14 дні)" },
  { value: "durable",  label: "🥫 Durable — консерви, крупи (≤14/30 дні)" },
];

export const ITEM_TYPE_OPTIONS: { value: string; label: string }[] = [
  { value: "product",      label: "Товар" },
  { value: "service",      label: "Послуга" },
  { value: "spare_part",   label: "Запчастина" },
  { value: "consumable",   label: "Розхідник" },
  { value: "raw_material", label: "Сировина" },
  { value: "kit",          label: "Комплект" },
];

export const ITEM_TYPE_LABELS: Record<string, string> = Object.fromEntries(
  ITEM_TYPE_OPTIONS.map((o) => [o.value, o.label]),
);

const productSchema = z.object({
  name:           z.string().min(1, "Обов'язкове поле").max(200),
  unit:           z.string().min(1, "Обов'язкове поле").max(50),
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

type FormValues = z.infer<typeof productSchema>;

const defaultValues: FormValues = {
  name: "", unit: "шт", managementType: "MTS", itemType: "product",
  minStock: 0, maxStock: 100, safetyBuffer: 5,
  shelfLifeDays: "", vatRate: 20,
  pricePurchase: "", priceRetail: "",
  isActive: true,
  manufacturer: "",
  countryOrigin: "",
  perishabilityClass: "standard",
};

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
  const isEditing = product !== null;

  const [barcodes, setBarcodes] = useState<string[]>([]);
  const [barcodeInput, setBarcodeInput] = useState("");
  const [imageFile, setImageFile] = useState<File | null>(null);
  const [imagePreview, setImagePreview] = useState<string | null>(null);
  const [lookupLoading, setLookupLoading] = useState(false);
  const [lookupError, setLookupError] = useState<string | null>(null);

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
      setLookupError("Штрихкод не знайдено у базі OpenFoodFacts");
    } finally {
      setLookupLoading(false);
    }
  }

  const onSubmit = (values: FormValues) => {
    const payload: CreateProductPayload = {
      name:          values.name,
      barcodes:      barcodes.length > 0 ? barcodes : undefined,
      unit:          values.unit,
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
            {isEditing ? "Редагувати товар" : "Додати товар"}
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
              <label style={labelStyle}>Назва товару *</label>
              <input {...register("name")} placeholder="Молоко 2,5% Галичина 1л" style={inputStyle} />
              {errors.name && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 3 }}>{errors.name.message}</p>
              )}
            </div>

            {/* Штрихкоди */}
            <div>
              <label style={labelStyle}>Штрихкоди</label>

              {/* Теги */}
              {barcodes.length > 0 && (
                <div style={{ display: "flex", flexWrap: "wrap", gap: 6, marginBottom: 8 }}>
                  {barcodes.map(b => (
                    <span key={b} style={{
                      display: "flex", alignItems: "center", gap: 4,
                      background: "#0F1F3D", border: "1px solid #1E3A5F",
                      borderRadius: 6, padding: "3px 8px",
                      color: "#93C5FD", fontSize: 12,
                    }}>
                      {b}
                      <button type="button" onClick={() => removeBarcode(b)}
                        style={{ background: "none", border: "none", color: "#60A5FA", cursor: "pointer", padding: 0, fontSize: 13, lineHeight: 1 }}>
                        ×
                      </button>
                    </span>
                  ))}
                </div>
              )}

              {/* Додати */}
              <div style={{ display: "flex", gap: 6 }}>
                <input
                  value={barcodeInput}
                  onChange={e => setBarcodeInput(e.target.value)}
                  onKeyDown={e => e.key === "Enter" && (e.preventDefault(), addBarcode())}
                  placeholder="4820029300128"
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
                  {lookupLoading ? "Пошук…" : "Знайти"}
                </button>
              </div>
              {lookupError && <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{lookupError}</p>}
            </div>

            {/* Зображення */}
            <div>
              <label style={labelStyle}>Зображення товару</label>
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
                  {imagePreview ? "Змінити фото" : "Завантажити фото"}
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
                    Видалити
                  </button>
                )}
              </div>
            </div>

            {/* Unit */}
            <div>
              <label style={labelStyle}>Одиниця *</label>
              <input {...register("unit")} placeholder="кг, шт, л, упак…" style={inputStyle} />
              {errors.unit && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 3 }}>{errors.unit.message}</p>
              )}
            </div>

            {/* ManagementType + ItemType */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>Тип управління</label>
                <select {...register("managementType")} style={{ ...inputStyle, cursor: "pointer" }}>
                  <option value="MTS">MTS — на склад</option>
                  <option value="MTO">MTO — на замовлення</option>
                </select>
              </div>
              <div>
                <label style={labelStyle}>Тип товару</label>
                <select {...register("itemType")} style={{ ...inputStyle, cursor: "pointer" }}>
                  {ITEM_TYPE_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                  ))}
                </select>
              </div>
            </div>

            {/* ShelfLife + VatRate */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>Термін придатності (днів)</label>
                <input {...register("shelfLifeDays")} type="number" min="1" placeholder="7" style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>ПДВ %</label>
                <input {...register("vatRate")} type="number" step="0.01" min="0" max="100" style={inputStyle} />
              </div>
            </div>

            {/* PerishabilityClass */}
            <div>
              <label style={labelStyle}>Клас псуємості</label>
              <select {...register("perishabilityClass")} style={{ ...inputStyle, cursor: "pointer" }}>
                {PERISHABILITY_CLASS_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
              </select>
            </div>

            {/* Manufacturer + CountryOrigin */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>Виробник</label>
                <input {...register("manufacturer")} placeholder="ТОВ Молочний Дім" style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>Країна походження</label>
                <input {...register("countryOrigin")} placeholder="Україна" style={inputStyle} />
              </div>
            </div>

            {/* Stock levels */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>Мін. залишок</label>
                <input {...register("minStock")} type="number" step="0.01" min="0" style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>Макс. залишок</label>
                <input {...register("maxStock")} type="number" step="0.01" min="0" style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>Буфер безпеки</label>
                <input {...register("safetyBuffer")} type="number" step="0.01" min="0" style={inputStyle} />
              </div>
            </div>

            {/* Prices */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>Закупівельна ціна (₴)</label>
                <input {...register("pricePurchase")} type="number" step="0.01" min="0" placeholder="0.00" style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>Роздрібна ціна (₴)</label>
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
                  Активний товар
                </label>
              </div>
            )}
          </div>

          {/* Actions */}
          <div style={{ display: "flex", gap: 10, marginTop: 22, justifyContent: "flex-end" }}>
            <Btn variant="ghost" type="button" onClick={onClose}>
              Скасувати
            </Btn>
            <Btn type="submit" disabled={isPending}>
              {isPending ? "Збереження…" : isEditing ? "Зберегти зміни" : "Додати товар"}
            </Btn>
          </div>
        </form>
      </div>
    </>
  );
}
