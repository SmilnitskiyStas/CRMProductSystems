"use client";

import { useState } from "react";
import { ChevronDown, ChevronRight, ImageOff } from "lucide-react";
import { DetailDrawer, DrawerField, DrawerSection, DrawerGrid } from "@/components/ui/DetailDrawer";
import { useItemCategories } from "../hooks/useMarketplace";
import type { SupplierItemDto } from "../types";

interface Props {
  item: SupplierItemDto | null;
  onClose: () => void;
}

const KNOWN_ATTRIBUTE_LABELS: Record<string, string> = {};

function formatValue(value: unknown): string {
  if (value == null || value === "") return "—";
  if (typeof value === "object") {
    try {
      return JSON.stringify(value);
    } catch {
      return String(value);
    }
  }
  return String(value);
}

export function SupplierItemDetailDialog({ item, onClose }: Props) {
  const { data: categories = [] } = useItemCategories();
  const [extraOpen, setExtraOpen] = useState(false);

  if (!item) return null;

  const categoryDef = item.category ? categories.find((c) => c.key === item.category) : undefined;
  const categoryFieldKeys = new Set((categoryDef?.fields ?? []).map((f) => f.key));

  const images = [...item.images].sort((a, b) => a.sortOrder - b.sortOrder);
  const mainImage = images.find((i) => i.kind === "main") ?? images[0];
  const galleryImages = images.filter((i) => i !== mainImage);

  const extraAttributeEntries = Object.entries(item.attributes ?? {}).filter(
    ([key]) => !categoryFieldKeys.has(key)
  );

  const dims =
    item.heightCm != null || item.depthCm != null || item.widthCm != null
      ? `${item.heightCm ?? "—"} × ${item.depthCm ?? "—"} × ${item.widthCm ?? "—"} см`
      : "—";

  return (
    <DetailDrawer
      isOpen={!!item}
      onClose={onClose}
      title={item.customName ?? item.itemName ?? "Товар"}
      subtitle={categoryDef?.labelUa}
      width={640}
    >
      {/* Image gallery */}
      <DrawerSection title="Зображення">
        <div
          style={{
            width: "100%",
            height: 220,
            background: "#111827",
            border: "1px solid #1F2937",
            borderRadius: 10,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            overflow: "hidden",
            marginBottom: galleryImages.length > 0 ? 10 : 0,
          }}
        >
          {mainImage ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img
              src={mainImage.url}
              alt={item.customName ?? item.itemName ?? "Товар"}
              style={{ width: "100%", height: "100%", objectFit: "contain" }}
            />
          ) : (
            <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 6, color: "#4B5563" }}>
              <ImageOff size={28} />
              <span style={{ fontSize: 12 }}>Немає зображення</span>
            </div>
          )}
        </div>

        {galleryImages.length > 0 && (
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            {galleryImages.map((img, idx) => (
              // eslint-disable-next-line @next/next/no-img-element
              <img
                key={idx}
                src={img.url}
                alt=""
                style={{
                  width: 64,
                  height: 64,
                  objectFit: "cover",
                  borderRadius: 8,
                  border: "1px solid #1F2937",
                  background: "#111827",
                }}
              />
            ))}
          </div>
        )}
      </DrawerSection>

      {/* Barcodes */}
      <DrawerSection title="Штрихкоди">
        {item.barcodes.length === 0 ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>Штрихкоди не вказані.</div>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            {item.barcodes.map((code, idx) => (
              <div key={code + idx} style={{ display: "flex", alignItems: "center", gap: 8 }}>
                <span
                  style={{
                    fontFamily: "monospace",
                    fontSize: 13,
                    color: "#E8EDF5",
                  }}
                >
                  {code}
                </span>
                <span
                  style={{
                    padding: "1px 7px",
                    borderRadius: 4,
                    fontSize: 10,
                    fontWeight: 600,
                    background: idx === 0 ? "#052e16" : "#1F2937",
                    color: idx === 0 ? "#4ADE80" : "#9CA3AF",
                  }}
                >
                  {idx === 0 ? "Основний" : "Альтернативний"}
                </span>
              </div>
            ))}
          </div>
        )}
      </DrawerSection>

      {/* Characteristics */}
      <DrawerSection title="Характеристики">
        <DrawerGrid>
          <DrawerField label="Бренд" value={item.brand ?? "—"} />
          <DrawerField label="Виробник" value={item.manufacturer ?? "—"} />
          <DrawerField label="Країна виробника" value={item.manufacturerCountry ?? "—"} />
          <DrawerField label="Категорія" value={categoryDef?.labelUa ?? item.category ?? "—"} />
          <DrawerField
            label="Мін./Макс. партія замовлення"
            value={`${item.minQty ?? "—"} / ${item.maxQty ?? "—"}`}
          />
          <DrawerField label="Одиниця виміру" value={item.unit ?? "—"} />
          <DrawerField label="Вага брутто" value={item.grossWeightKg != null ? `${item.grossWeightKg} кг` : "—"} />
          <DrawerField label="Розміри (В×Г×Ш)" value={dims} />
        </DrawerGrid>

        {categoryDef && categoryDef.fields.length > 0 && (
          <div style={{ marginTop: 6 }}>
            <DrawerGrid>
              {categoryDef.fields.map((field) => (
                <DrawerField
                  key={field.key}
                  label={field.labelUa}
                  value={formatValue(item.attributes?.[field.key])}
                />
              ))}
            </DrawerGrid>
          </div>
        )}
      </DrawerSection>

      {/* Extra attributes not covered by category schema */}
      {extraAttributeEntries.length > 0 && (
        <div style={{ marginBottom: 20 }}>
          <button
            onClick={() => setExtraOpen((v) => !v)}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 6,
              background: "transparent",
              border: "none",
              color: "#9CA3AF",
              fontSize: 12,
              fontWeight: 600,
              cursor: "pointer",
              padding: 0,
              marginBottom: extraOpen ? 10 : 0,
            }}
          >
            {extraOpen ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
            Додаткова інформація
          </button>
          {extraOpen && (
            <DrawerGrid>
              {extraAttributeEntries.map(([key, value]) => (
                <DrawerField
                  key={key}
                  label={KNOWN_ATTRIBUTE_LABELS[key] ?? key}
                  value={formatValue(value)}
                />
              ))}
            </DrawerGrid>
          )}
        </div>
      )}
    </DetailDrawer>
  );
}
