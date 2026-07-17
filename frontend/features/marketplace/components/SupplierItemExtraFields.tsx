"use client";

import { Plus, Trash2 } from "lucide-react";
import { useTranslations } from "next-intl";

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

export interface SupplierItemExtraFieldsState {
  brand: string;
  manufacturer: string;
  manufacturerCountry: string;
  maxQtyRaw: string;
  grossWeightKgRaw: string;
  heightCmRaw: string;
  depthCmRaw: string;
  widthCmRaw: string;
  barcodes: string[];
  imageUrls: string[];
}

export function emptyExtraFields(): SupplierItemExtraFieldsState {
  return {
    brand: "",
    manufacturer: "",
    manufacturerCountry: "",
    maxQtyRaw: "",
    grossWeightKgRaw: "",
    heightCmRaw: "",
    depthCmRaw: "",
    widthCmRaw: "",
    barcodes: [],
    imageUrls: [],
  };
}

interface Props {
  value: SupplierItemExtraFieldsState;
  onChange: (value: SupplierItemExtraFieldsState) => void;
}

function StringListEditor({
  label,
  placeholder,
  primaryHint,
  removeTitle,
  addLabel,
  items,
  onChange,
}: {
  label: string;
  placeholder: string;
  primaryHint: string;
  removeTitle: string;
  addLabel: string;
  items: string[];
  onChange: (items: string[]) => void;
}) {
  return (
    <div>
      <label style={LABEL_STYLE}>{label}</label>
      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {items.map((val, idx) => (
          <div key={idx} style={{ display: "flex", gap: 8, alignItems: "center" }}>
            <input
              type="text"
              value={val}
              onChange={(e) => {
                const next = [...items];
                next[idx] = e.target.value;
                onChange(next);
              }}
              placeholder={idx === 0 ? primaryHint : placeholder}
              style={INPUT_STYLE}
            />
            <button
              type="button"
              onClick={() => onChange(items.filter((_, i) => i !== idx))}
              title={removeTitle}
              style={{
                background: "transparent",
                border: "none",
                color: "#6B7280",
                cursor: "pointer",
                padding: 4,
                flexShrink: 0,
              }}
            >
              <Trash2 size={15} />
            </button>
          </div>
        ))}
        <button
          type="button"
          onClick={() => onChange([...items, ""])}
          style={{
            display: "inline-flex",
            alignItems: "center",
            gap: 6,
            alignSelf: "flex-start",
            padding: "5px 10px",
            background: "transparent",
            border: "1px solid #374151",
            borderRadius: 7,
            color: "#9CA3AF",
            fontSize: 12,
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          <Plus size={13} /> {addLabel}
        </button>
      </div>
    </div>
  );
}

/**
 * Universal supplier-item fields shared by AddSupplierItemModal (provider,
 * create-only) and CabinetItemModal (supplier self-service, create + edit).
 * Covers brand/manufacturer/country, dimensions/weight, and repeatable
 * barcodes/imageUrls lists (first entry = primary/main).
 */
export function SupplierItemExtraFields({ value, onChange }: Props) {
  const t = useTranslations("Dashboard.marketplace.itemExtraFields");

  function setField<K extends keyof SupplierItemExtraFieldsState>(
    key: K,
    val: SupplierItemExtraFieldsState[K]
  ) {
    onChange({ ...value, [key]: val });
  }

  return (
    <>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
        <div>
          <label style={LABEL_STYLE}>{t("brandLabel")}</label>
          <input
            type="text"
            value={value.brand}
            onChange={(e) => setField("brand", e.target.value)}
            style={INPUT_STYLE}
          />
        </div>
        <div>
          <label style={LABEL_STYLE}>{t("manufacturerLabel")}</label>
          <input
            type="text"
            value={value.manufacturer}
            onChange={(e) => setField("manufacturer", e.target.value)}
            style={INPUT_STYLE}
          />
        </div>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
        <div>
          <label style={LABEL_STYLE}>{t("countryLabel")}</label>
          <input
            type="text"
            value={value.manufacturerCountry}
            onChange={(e) => setField("manufacturerCountry", e.target.value.toUpperCase())}
            placeholder={t("countryPlaceholder")}
            maxLength={2}
            style={INPUT_STYLE}
          />
        </div>
        <div>
          <label style={LABEL_STYLE}>{t("maxQtyLabel")}</label>
          <input
            type="number"
            min="1"
            step="1"
            value={value.maxQtyRaw}
            onChange={(e) => setField("maxQtyRaw", e.target.value)}
            style={INPUT_STYLE}
          />
        </div>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
        <div>
          <label style={LABEL_STYLE}>{t("grossWeightLabel")}</label>
          <input
            type="number"
            min="0"
            step="0.01"
            value={value.grossWeightKgRaw}
            onChange={(e) => setField("grossWeightKgRaw", e.target.value)}
            style={INPUT_STYLE}
          />
        </div>
        <div>
          <label style={LABEL_STYLE}>{t("heightLabel")}</label>
          <input
            type="number"
            min="0"
            step="0.1"
            value={value.heightCmRaw}
            onChange={(e) => setField("heightCmRaw", e.target.value)}
            style={INPUT_STYLE}
          />
        </div>
        <div>
          <label style={LABEL_STYLE}>{t("depthLabel")}</label>
          <input
            type="number"
            min="0"
            step="0.1"
            value={value.depthCmRaw}
            onChange={(e) => setField("depthCmRaw", e.target.value)}
            style={INPUT_STYLE}
          />
        </div>
        <div>
          <label style={LABEL_STYLE}>{t("widthLabel")}</label>
          <input
            type="number"
            min="0"
            step="0.1"
            value={value.widthCmRaw}
            onChange={(e) => setField("widthCmRaw", e.target.value)}
            style={INPUT_STYLE}
          />
        </div>
      </div>

      <StringListEditor
        label={t("barcodesLabel")}
        placeholder={t("barcodesPlaceholder")}
        primaryHint={t("barcodesPrimaryHint")}
        removeTitle={t("removeTitle")}
        addLabel={t("addButton")}
        items={value.barcodes}
        onChange={(barcodes) => setField("barcodes", barcodes)}
      />

      <StringListEditor
        label={t("imagesLabel")}
        placeholder={t("imagesPlaceholder")}
        primaryHint={t("imagesPrimaryHint")}
        removeTitle={t("removeTitle")}
        addLabel={t("addButton")}
        items={value.imageUrls}
        onChange={(imageUrls) => setField("imageUrls", imageUrls)}
      />
    </>
  );
}

/**
 * Parses raw string state into the numeric/array request fields. Returns
 * `null` + error message if a numeric field is invalid.
 *
 * `t` is optional so this stays backward-compatible with callers that predate
 * i18n rollout Block 6 (e.g. `features/supplier-cabinet/components/CabinetItemModal.tsx`,
 * Block 7, not yet translated): omitting it reproduces the exact original
 * Ukrainian-only error messages. Pass
 * `useTranslations("Dashboard.marketplace.itemExtraFields")` from a translated
 * caller to get the bilingual message instead.
 */
export function parseExtraFields(
  state: SupplierItemExtraFieldsState,
  t?: (key: string, values?: Record<string, string>) => string
): { error: string } | {
  error: null;
  brand?: string;
  manufacturer?: string;
  manufacturerCountry?: string;
  maxQty?: number;
  grossWeightKg?: number;
  heightCm?: number;
  depthCm?: number;
  widthCm?: number;
  barcodes?: string[];
  imageUrls?: string[];
} {
  function parseNum(
    raw: string,
    labelKey: "maxQtyLabel" | "grossWeightShortLabel" | "heightShortLabel" | "depthShortLabel" | "widthShortLabel",
    fallbackLabel: string
  ): { ok: true; value?: number } | { ok: false; error: string } {
    if (!raw.trim()) return { ok: true, value: undefined };
    const n = parseFloat(raw);
    if (isNaN(n) || n < 0) {
      const label = t ? t(labelKey) : fallbackLabel;
      return {
        ok: false,
        error: t ? t("invalidFieldValue", { field: label }) : `Некоректне значення поля «${label}».`,
      };
    }
    return { ok: true, value: n };
  }

  const maxQty = parseNum(state.maxQtyRaw, "maxQtyLabel", "Макс. замовлення");
  if (!maxQty.ok) return { error: maxQty.error };
  const grossWeightKg = parseNum(state.grossWeightKgRaw, "grossWeightShortLabel", "Вага брутто");
  if (!grossWeightKg.ok) return { error: grossWeightKg.error };
  const heightCm = parseNum(state.heightCmRaw, "heightShortLabel", "Висота");
  if (!heightCm.ok) return { error: heightCm.error };
  const depthCm = parseNum(state.depthCmRaw, "depthShortLabel", "Глибина");
  if (!depthCm.ok) return { error: depthCm.error };
  const widthCm = parseNum(state.widthCmRaw, "widthShortLabel", "Ширина");
  if (!widthCm.ok) return { error: widthCm.error };

  const barcodes = state.barcodes.map((b) => b.trim()).filter(Boolean);
  const imageUrls = state.imageUrls.map((u) => u.trim()).filter(Boolean);

  return {
    error: null,
    brand: state.brand.trim() || undefined,
    manufacturer: state.manufacturer.trim() || undefined,
    manufacturerCountry: state.manufacturerCountry.trim() || undefined,
    maxQty: maxQty.value,
    grossWeightKg: grossWeightKg.value,
    heightCm: heightCm.value,
    depthCm: depthCm.value,
    widthCm: widthCm.value,
    barcodes,
    imageUrls,
  };
}

export function extraFieldsFromItem(item: {
  brand?: string | null;
  manufacturer?: string | null;
  manufacturerCountry?: string | null;
  maxQty?: number | null;
  grossWeightKg?: number | null;
  heightCm?: number | null;
  depthCm?: number | null;
  widthCm?: number | null;
  barcodes?: string[];
  images?: { url: string; kind: string; sortOrder: number }[];
} | undefined | null): SupplierItemExtraFieldsState {
  if (!item) return emptyExtraFields();
  return {
    brand: item.brand ?? "",
    manufacturer: item.manufacturer ?? "",
    manufacturerCountry: item.manufacturerCountry ?? "",
    maxQtyRaw: item.maxQty != null ? String(item.maxQty) : "",
    grossWeightKgRaw: item.grossWeightKg != null ? String(item.grossWeightKg) : "",
    heightCmRaw: item.heightCm != null ? String(item.heightCm) : "",
    depthCmRaw: item.depthCm != null ? String(item.depthCm) : "",
    widthCmRaw: item.widthCm != null ? String(item.widthCm) : "",
    barcodes: item.barcodes ? [...item.barcodes] : [],
    imageUrls: item.images ? [...item.images].sort((a, b) => a.sortOrder - b.sortOrder).map((i) => i.url) : [],
  };
}
