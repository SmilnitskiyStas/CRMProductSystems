"use client";

import { useState } from "react";
import { ChevronDown, ChevronRight, ImageOff } from "lucide-react";
import { useTranslations } from "next-intl";
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
  const t = useTranslations("Dashboard.marketplace.itemDetailDialog");
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
      ? `${item.heightCm ?? "—"} × ${item.depthCm ?? "—"} × ${item.widthCm ?? "—"}${t("dimensionsUnit")}`
      : "—";

  return (
    <DetailDrawer
      isOpen={!!item}
      onClose={onClose}
      title={item.customName ?? item.itemName ?? t("fallbackItemName")}
      subtitle={categoryDef?.labelUa}
      width={640}
    >
      {/* Image gallery */}
      <DrawerSection title={t("imagesTitle")}>
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
              alt={item.customName ?? item.itemName ?? t("fallbackItemName")}
              style={{ width: "100%", height: "100%", objectFit: "contain" }}
            />
          ) : (
            <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 6, color: "#4B5563" }}>
              <ImageOff size={28} />
              <span style={{ fontSize: 12 }}>{t("noImage")}</span>
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
      <DrawerSection title={t("barcodesTitle")}>
        {item.barcodes.length === 0 ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noBarcodes")}</div>
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
                  {idx === 0 ? t("primaryBarcode") : t("alternateBarcode")}
                </span>
              </div>
            ))}
          </div>
        )}
      </DrawerSection>

      {/* Characteristics */}
      <DrawerSection title={t("characteristicsTitle")}>
        <DrawerGrid>
          <DrawerField label={t("brandLabel")} value={item.brand ?? "—"} />
          <DrawerField label={t("manufacturerLabel")} value={item.manufacturer ?? "—"} />
          <DrawerField label={t("countryLabel")} value={item.manufacturerCountry ?? "—"} />
          <DrawerField label={t("categoryLabel")} value={categoryDef?.labelUa ?? item.category ?? "—"} />
          <DrawerField
            label={t("moqLabel")}
            value={`${item.minQty ?? "—"} / ${item.maxQty ?? "—"}`}
          />
          <DrawerField label={t("unitLabel")} value={item.unit ?? "—"} />
          <DrawerField label={t("grossWeightLabel")} value={item.grossWeightKg != null ? `${item.grossWeightKg}${t("grossWeightUnit")}` : "—"} />
          <DrawerField label={t("dimensionsLabel")} value={dims} />
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
            {t("extraInfoToggle")}
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
