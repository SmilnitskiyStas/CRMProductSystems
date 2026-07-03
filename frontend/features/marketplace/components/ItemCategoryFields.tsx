"use client";

import { useItemCategories } from "../hooks/useMarketplace";
import type { SupplierItemCategoryField } from "../types";

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

interface Props {
  /** Selected category key, or "" for "Без категорії". */
  category: string;
  onCategoryChange: (category: string) => void;
  attributes: Record<string, string>;
  onAttributesChange: (attributes: Record<string, string>) => void;
}

/**
 * Category select + dynamic attribute fields (ADR-017 §4, TASK-296).
 * Shared between the supplier cabinet item modal and the provider admin
 * add-item modal so both render the same category-specific fields.
 */
export function ItemCategoryFields({
  category,
  onCategoryChange,
  attributes,
  onAttributesChange,
}: Props) {
  const { data: categories = [] } = useItemCategories();
  const selected = categories.find((c) => c.key === category);

  function setField(key: string, value: string) {
    onAttributesChange({ ...attributes, [key]: value });
  }

  function renderField(field: SupplierItemCategoryField) {
    const value = attributes[field.key] ?? "";
    const label = (
      <label style={LABEL_STYLE}>
        {field.labelUa} {field.required && <span style={{ color: "#F87171" }}>*</span>}
      </label>
    );

    if (field.type === "select") {
      return (
        <div key={field.key}>
          {label}
          <select
            value={value}
            onChange={(e) => setField(field.key, e.target.value)}
            style={INPUT_STYLE}
          >
            <option value="">— оберіть —</option>
            {(field.options ?? []).map((opt) => (
              <option key={opt} value={opt}>
                {opt}
              </option>
            ))}
          </select>
        </div>
      );
    }

    if (field.type === "bool") {
      return (
        <div key={field.key} style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <input
            type="checkbox"
            id={`attr-${field.key}`}
            checked={value === "true"}
            onChange={(e) => setField(field.key, e.target.checked ? "true" : "false")}
            style={{ width: 16, height: 16, cursor: "pointer" }}
          />
          <label htmlFor={`attr-${field.key}`} style={{ color: "#E8EDF5", fontSize: 13, cursor: "pointer" }}>
            {field.labelUa}
          </label>
        </div>
      );
    }

    const inputType = field.type === "date" ? "date" : field.type === "number" ? "number" : "text";
    return (
      <div key={field.key}>
        {label}
        <input
          type={inputType}
          value={value}
          onChange={(e) => setField(field.key, e.target.value)}
          style={INPUT_STYLE}
        />
      </div>
    );
  }

  return (
    <>
      <div>
        <label style={LABEL_STYLE}>Категорія</label>
        <select
          value={category}
          onChange={(e) => {
            onCategoryChange(e.target.value);
            onAttributesChange({});
          }}
          style={INPUT_STYLE}
        >
          <option value="">Без категорії</option>
          {categories.map((c) => (
            <option key={c.key} value={c.key}>
              {c.labelUa}
            </option>
          ))}
        </select>
      </div>

      {selected && (
        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          {selected.fields.map(renderField)}
        </div>
      )}
    </>
  );
}

/** Client-side mirror of the server's required-field check (UX only, not authoritative). */
export function findMissingRequiredField(
  categories: { key: string; labelUa: string; fields: SupplierItemCategoryField[] }[],
  category: string,
  attributes: Record<string, string>
): string | null {
  const def = categories.find((c) => c.key === category);
  if (!def) return null;
  for (const field of def.fields) {
    if (field.required && !attributes[field.key]?.trim()) {
      return `Поле «${field.labelUa}» обов'язкове для категорії «${def.labelUa}».`;
    }
  }
  return null;
}
