"use client";

import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Btn } from "@/components/ui/Btn";
import type { CreateProductPayload, Product, UpdateProductPayload } from "../types";

const productSchema = z.object({
  name:          z.string().min(1, "Обов'язкове поле").max(200),
  barcode:       z.string().max(100).optional(),
  unit:          z.string().min(1, "Обов'язкове поле").max(50),
  managementType:z.enum(["MTS", "MTO"]),
  minStock:      z.coerce.number().min(0),
  maxStock:      z.coerce.number().min(0),
  safetyBuffer:  z.coerce.number().min(0),
  shelfLifeDays: z.coerce.number().int().min(1).optional().or(z.literal("")),
  vatRate:       z.coerce.number().min(0).max(100),
  pricePurchase: z.coerce.number().min(0).optional().or(z.literal("")),
  priceRetail:   z.coerce.number().min(0).optional().or(z.literal("")),
  isActive:      z.boolean(),
});

type FormValues = z.infer<typeof productSchema>;

const defaultValues: FormValues = {
  name: "", barcode: "", unit: "шт", managementType: "MTS",
  minStock: 0, maxStock: 100, safetyBuffer: 5,
  shelfLifeDays: "", vatRate: 20,
  pricePurchase: "", priceRetail: "",
  isActive: true,
};

interface Props {
  open: boolean;
  product: Product | null;
  isPending: boolean;
  onClose: () => void;
  onCreate: (payload: CreateProductPayload) => void;
  onUpdate: (id: string, payload: UpdateProductPayload) => void;
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

export function ProductForm({ open, product, isPending, onClose, onCreate, onUpdate }: Props) {
  const isEditing = product !== null;

  const {
    register,
    handleSubmit,
    reset,
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
            barcode:        product.barcode ?? "",
            unit:           product.unit,
            managementType: (product.managementType as "MTS" | "MTO") ?? "MTS",
            minStock:       product.minStock,
            maxStock:       product.maxStock,
            safetyBuffer:   product.safetyBuffer,
            shelfLifeDays:  product.shelfLifeDays ?? "",
            vatRate:        product.vatRate,
            pricePurchase:  product.pricePurchase ?? "",
            priceRetail:    product.priceRetail ?? "",
            isActive:       product.isActive,
          }
        : defaultValues,
    );
  }, [product, reset]);

  if (!open) return null;

  const onSubmit = (values: FormValues) => {
    const payload: CreateProductPayload = {
      name:            values.name,
      barcode:         values.barcode || undefined,
      unit:            values.unit,
      managementType:  values.managementType,
      minStock:        values.minStock,
      maxStock:        values.maxStock,
      safetyBuffer:    values.safetyBuffer,
      shelfLifeDays:   values.shelfLifeDays !== "" ? Number(values.shelfLifeDays) : undefined,
      vatRate:         values.vatRate,
      pricePurchase:   values.pricePurchase !== "" ? Number(values.pricePurchase) : undefined,
      priceRetail:     values.priceRetail !== "" ? Number(values.priceRetail) : undefined,
    };

    if (isEditing) {
      onUpdate(product.id, { ...payload, isActive: values.isActive });
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

            {/* Barcode + Unit */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>Штрихкод</label>
                <input {...register("barcode")} placeholder="4820029300128" style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>Одиниця *</label>
                <input {...register("unit")} placeholder="кг, шт, л, упак…" style={inputStyle} />
                {errors.unit && (
                  <p style={{ color: "#EF4444", fontSize: 11, marginTop: 3 }}>{errors.unit.message}</p>
                )}
              </div>
            </div>

            {/* ManagementType + ShelfLife + VatRate */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>Тип управління</label>
                <select {...register("managementType")} style={{ ...inputStyle, cursor: "pointer" }}>
                  <option value="MTS">MTS — на склад</option>
                  <option value="MTO">MTO — на замовлення</option>
                </select>
              </div>
              <div>
                <label style={labelStyle}>Термін придатності (днів)</label>
                <input {...register("shelfLifeDays")} type="number" min="1" placeholder="7" style={inputStyle} />
              </div>
              <div>
                <label style={labelStyle}>ПДВ %</label>
                <input {...register("vatRate")} type="number" step="0.01" min="0" max="100" style={inputStyle} />
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
