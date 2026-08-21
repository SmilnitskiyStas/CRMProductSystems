"use client";

import { useEffect, useState } from "react";
import { Search } from "lucide-react";
import { useTranslations } from "next-intl";
import { useProductSearch } from "@/features/inventory/hooks/useProducts";
import type { Product } from "@/features/inventory/types";

interface Props {
  /** Already-linked product ids — filtered out of search results. */
  excludeIds: string[];
  /** Fires when a result row is clicked. Parent owns the actual add-coefficient mutation. */
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
 * Search-driven, single-pick product finder sourced from the inventory catalog
 * (`/api/items`) — NOT the consumer-app/storefront catalog, a different bounded context.
 * Debounced (~300ms) — same setTimeout-in-useEffect pattern used elsewhere in this codebase
 * (e.g. NotificationFilterDrawer.tsx, consumer-app's ProductPickerField.tsx) since there's no
 * shared debounce hook. Each click emits one pick immediately; no accumulated draft selection.
 */
export function EventProductPicker({ excludeIds, onPick }: Props) {
  const t = useTranslations("Dashboard.events.dayDetail");

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
          placeholder={t("productPickerPlaceholder")}
          style={inputStyle}
        />
      </div>

      <div style={{ marginTop: 6, maxHeight: 180, overflowY: "auto", display: "flex", flexDirection: "column", gap: 4 }}>
        {debouncedQuery.length === 0 && <p style={hintStyle}>{t("productPickerHint")}</p>}
        {debouncedQuery.length > 0 && isLoading && <p style={hintStyle}>{t("productPickerLoading")}</p>}
        {debouncedQuery.length > 0 && !isLoading && results.length === 0 && (
          <p style={hintStyle}>{t("productPickerEmpty")}</p>
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
