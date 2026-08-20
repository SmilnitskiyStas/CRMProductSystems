"use client";

import { useEffect, useMemo, useState } from "react";
import { Search, X } from "lucide-react";
import { useCatalogProducts, useCatalogProductsByIds } from "@/features/catalog/hooks/useCatalog";
import type { FieldProps } from "./BlockPropertyEditor";

// ── Style constants (this feature area's convention — each component re-declares its own inline
// style constants rather than importing shared ones, see AppPreviewPanel.tsx's own banner comment
// on the same pattern; BlockPropertyEditor.tsx's constants aren't exported shared infrastructure). ─

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px 9px 30px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

const hintStyle: React.CSSProperties = { color: "#4B5563", fontSize: 11, marginTop: 4 };
const errorStyle: React.CSSProperties = { ...hintStyle, color: "#EF4444" };

const rowStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: 8,
  padding: "6px 8px",
  borderRadius: 6,
};

function money(value: number): string {
  return value.toLocaleString("uk-UA", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function Thumb({ imageUrl }: { imageUrl?: string | null }) {
  if (!imageUrl) {
    return (
      <div style={{ width: 30, height: 30, borderRadius: 6, background: "#1F2937", flexShrink: 0 }} />
    );
  }
  return (
    // eslint-disable-next-line @next/next/no-img-element -- tenant-supplied product image, same opt-out as blockPreviews.tsx's PreviewImage.
    <img
      src={imageUrl}
      alt=""
      style={{ width: 30, height: 30, borderRadius: 6, objectFit: "cover", flexShrink: 0 }}
    />
  );
}

/**
 * TASK-574 (ADR-032 "Catalog Curation", Decision 1/4): async search-and-pick UI backing the new
 * `productIds` block-prop kind — `productGrid`/`productCarousel`'s curated product selection.
 * No existing `stringArray` mode fits: the valid value set here is a tenant's live catalog
 * (potentially thousands of SKUs, server-searched by name), not a static `allowedValues` list, so
 * this is a dedicated component wired into `BlockPropertyEditor.tsx`'s `PropField` switch
 * (`case "productIds"`) instead of a new `StringArrayField` mode or a name-based special-case.
 *
 * Selection order === display order — clicking a search result appends to the end of `value`; no
 * drag-reorder this phase (ADR-032 Decision 4, deliberate scope cut — remove-and-re-add is the
 * only way to reorder). Every add/remove calls `setValue(def.name, ..., { shouldValidate: true,
 * shouldDirty: true })`, the same convention `StringArrayField`'s `addTag`/`removeTag` already use,
 * so this field participates in the drawer's existing Apply/Cancel + live-preview wiring
 * (TASK-565's `onLiveChange`) with zero special-casing anywhere else.
 */
export function ProductPickerField({ def, label, setValue, value, error, t }: FieldProps) {
  const items = useMemo(() => (Array.isArray(value) ? (value as string[]) : []), [value]);
  const atMax = typeof def.maxItems === "number" && items.length >= def.maxItems;

  // Debounced (~300ms) search box — same setTimeout-in-useEffect pattern as
  // NotificationFilterDrawer.tsx's own search debounce, this feature area has no shared hook.
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  useEffect(() => {
    const handle = setTimeout(() => setDebouncedQuery(query.trim()), 300);
    return () => clearTimeout(handle);
  }, [query]);

  // Backend-searched candidates (proves `search` reaches `/api/items`, not a client-side filter
  // over a short default fetch — see TASK-574's acceptance criteria).
  const searchResults = useCatalogProducts({ search: debouncedQuery || undefined });
  // Resolves the CURRENT selection's own name/price/thumbnail regardless of whether those ids fall
  // inside `searchResults`' window — same "catalog fetch is too short" correctness fix TASK-572's
  // `ids` param exists for (ADR-032 Decision 3).
  const selectedProducts = useCatalogProductsByIds(items);

  const selectedById = useMemo(() => {
    const map = new Map<string, { name: string; priceRetail: number | null; imageUrl: string | null }>();
    for (const p of selectedProducts.data ?? []) map.set(p.id, p);
    return map;
  }, [selectedProducts.data]);

  // Mirrors PromoProductsSection.tsx's own `activeCatalogProducts` filter — an admin should never
  // be able to curate an already-inactive product. Also excludes ids already selected.
  const candidates = useMemo(
    () => (searchResults.data ?? []).filter((p) => p.isActive && !items.includes(p.id)),
    [searchResults.data, items],
  );

  function add(id: string) {
    if (atMax || items.includes(id)) return;
    setValue(def.name, [...items, id], { shouldValidate: true, shouldDirty: true });
  }

  function remove(id: string) {
    setValue(
      def.name,
      items.filter((v) => v !== id),
      { shouldValidate: true, shouldDirty: true },
    );
  }

  return (
    <div>
      <label style={labelStyle}>{label}</label>

      {items.length > 0 && (
        <div style={{ display: "flex", flexDirection: "column", gap: 6, marginBottom: 10 }}>
          {items.map((id) => {
            const product = selectedById.get(id);
            return (
              <div key={id} style={{ ...rowStyle, background: "#161B26", border: "1px solid #374151" }}>
                <Thumb imageUrl={product?.imageUrl} />
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div
                    style={{
                      color: "#E8EDF5",
                      fontSize: 12,
                      fontWeight: 600,
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {product?.name ?? id}
                  </div>
                  {product?.priceRetail != null && (
                    <div style={{ color: "#6B7280", fontSize: 11, marginTop: 1 }}>{money(product.priceRetail)} ₴</div>
                  )}
                </div>
                <button
                  type="button"
                  onClick={() => remove(id)}
                  aria-label={t("removeTag")}
                  style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 0, display: "flex", flexShrink: 0 }}
                >
                  <X size={13} />
                </button>
              </div>
            );
          })}
        </div>
      )}

      {!atMax && (
        <>
          <div style={{ position: "relative" }}>
            <Search size={13} style={{ position: "absolute", left: 10, top: "50%", transform: "translateY(-50%)", color: "#6B7280" }} />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={t("productSearchPlaceholder")}
              style={{ ...inputStyle, borderColor: error ? "#EF4444" : "#374151" }}
            />
          </div>
          <div style={{ marginTop: 6, maxHeight: 220, overflowY: "auto", display: "flex", flexDirection: "column", gap: 4 }}>
            {searchResults.isLoading && <p style={hintStyle}>{t("productSearchLoading")}</p>}
            {!searchResults.isLoading && candidates.length === 0 && (
              <p style={hintStyle}>{t("productSearchEmpty")}</p>
            )}
            {candidates.map((product) => (
              <button
                key={product.id}
                type="button"
                onClick={() => add(product.id)}
                style={{
                  ...rowStyle,
                  background: "transparent",
                  border: "1px solid #1F2937",
                  cursor: "pointer",
                  textAlign: "left",
                  width: "100%",
                }}
              >
                <Thumb imageUrl={product.imageUrl} />
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div
                    style={{
                      color: "#E8EDF5",
                      fontSize: 12,
                      fontWeight: 600,
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {product.name}
                  </div>
                  {product.priceRetail != null && (
                    <div style={{ color: "#6B7280", fontSize: 11, marginTop: 1 }}>{money(product.priceRetail)} ₴</div>
                  )}
                </div>
              </button>
            ))}
          </div>
        </>
      )}

      {error && <p style={errorStyle}>{error}</p>}
      {!error && items.length === 0 && <p style={hintStyle}>{t("productPickerEmptyHint")}</p>}
      {!error && typeof def.maxItems === "number" && (
        <p style={hintStyle}>{t("itemsHint", { min: def.minItems ?? 0, max: def.maxItems })}</p>
      )}
    </div>
  );
}
