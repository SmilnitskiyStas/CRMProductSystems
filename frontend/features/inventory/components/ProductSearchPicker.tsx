"use client";

import { useEffect, useState } from "react";
import { Search } from "lucide-react";
import { useTranslations } from "next-intl";
import { useProductSearch } from "@/features/inventory/hooks/useProducts";
import type { Product } from "@/features/inventory/types";

interface Props {
  /** Ids to filter out of the results. Pass [] when there's no reason to exclude anything —
   * e.g. the same product legitimately placed in more than one spot within the same scope. */
  excludeIds: string[];
  /** Fires when a result row is clicked. Parent owns whatever the pick actually does. */
  onPick: (product: Product) => void;
}

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px 8px 30px",
  outline: "none",
  boxSizing: "border-box",
};

const rowStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  justifyContent: "space-between",
  gap: 8,
  padding: "6px 10px",
  borderRadius: 6,
  background: "transparent",
  border: "1px solid #1F2937",
  cursor: "pointer",
  textAlign: "left",
  width: "100%",
  boxSizing: "border-box",
};

const hintStyle: React.CSSProperties = { color: "#4B5563", fontSize: 12, margin: "6px 0 0" };

/**
 * Search-driven, single-pick product finder sourced from the inventory catalog (`/api/items`).
 * Generalized (TASK-716) from `features/events/components/EventProductPicker.tsx` so any feature
 * needing a plain "search the catalog, click a row to pick" widget can reuse it instead of
 * re-implementing the debounce/search/render logic. The events feature keeps its own picker as-is
 * — it's scoped to that feature's `Dashboard.events.dayDetail` i18n keys and callers; this one
 * uses the feature-neutral `Dashboard.inventory.productPicker` namespace instead.
 * Debounced (~300ms) via the same setTimeout-in-useEffect pattern used elsewhere in this codebase
 * (no shared debounce hook yet).
 */
export function ProductSearchPicker({ excludeIds, onPick }: Props) {
  const t = useTranslations("Dashboard.inventory.productPicker");

  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  useEffect(() => {
    const handle = setTimeout(() => setDebouncedQuery(query.trim()), 300);
    return () => clearTimeout(handle);
  }, [query]);

  const { data, isLoading } = useProductSearch(debouncedQuery);
  const results = (data ?? []).filter((p) => !excludeIds.includes(p.id));

  return (
    <div>
      <div style={{ position: "relative" }}>
        <Search
          size={13}
          style={{ position: "absolute", left: 10, top: "50%", transform: "translateY(-50%)", color: "#6B7280" }}
        />
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder={t("placeholder")}
          style={inputStyle}
        />
      </div>

      <div style={{ marginTop: 6, maxHeight: 180, overflowY: "auto", display: "flex", flexDirection: "column", gap: 4 }}>
        {debouncedQuery.length === 0 && <p style={hintStyle}>{t("hint")}</p>}
        {debouncedQuery.length > 0 && isLoading && <p style={hintStyle}>{t("loading")}</p>}
        {debouncedQuery.length > 0 && !isLoading && results.length === 0 && (
          <p style={hintStyle}>{t("empty")}</p>
        )}
        {results.map((product) => (
          <button key={product.id} type="button" onClick={() => onPick(product)} style={rowStyle}>
            <span
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
            </span>
            {product.priceRetail != null && (
              <span style={{ color: "#6B7280", fontSize: 11, flexShrink: 0 }}>
                {product.priceRetail.toLocaleString("uk-UA", { maximumFractionDigits: 2 })} ₴
              </span>
            )}
          </button>
        ))}
      </div>
    </div>
  );
}
