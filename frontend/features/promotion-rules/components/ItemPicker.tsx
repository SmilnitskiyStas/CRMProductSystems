"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { Search } from "lucide-react";
import { promotionRulesApi } from "../api/promotionRules";
import { formatMoney } from "../lib/messages";
import type { LookupItem } from "../types";
import { inputStyle } from "./shared";

/** Debounced search over GET /api/promotion-rules/lookups/items; calls onPick for a clicked result. */
export function ItemPicker({
  onPick,
  excludeIds = [],
}: {
  onPick: (item: LookupItem) => void;
  excludeIds?: string[];
}) {
  const t = useTranslations("Dashboard.promotionRules");
  const [query, setQuery] = useState("");
  const [debounced, setDebounced] = useState("");

  useEffect(() => {
    const h = setTimeout(() => setDebounced(query.trim()), 300);
    return () => clearTimeout(h);
  }, [query]);

  const { data = [], isFetching } = useQuery({
    queryKey: ["promotion-rules", "lookup-items", debounced],
    queryFn: () => promotionRulesApi.lookupItems(debounced),
    enabled: debounced.length >= 2,
    staleTime: 30_000,
  });
  const results = data.filter((i) => !excludeIds.includes(i.id));

  return (
    <div>
      <div style={{ position: "relative" }}>
        <Search size={14} style={{ position: "absolute", left: 11, top: 12, color: "#6B7280" }} />
        <input
          style={{ ...inputStyle, paddingLeft: 32 }}
          value={query}
          placeholder={t("items.searchPlaceholder")}
          onChange={(e) => setQuery(e.target.value)}
        />
      </div>
      {debounced.length >= 2 && (
        <div
          style={{
            marginTop: 6,
            border: "1px solid #1F2937",
            borderRadius: 8,
            maxHeight: 220,
            overflowY: "auto",
            background: "#0D1117",
          }}
        >
          {isFetching && results.length === 0 && (
            <div style={{ padding: 10, color: "#6B7280", fontSize: 12 }}>{t("items.searching")}</div>
          )}
          {!isFetching && results.length === 0 && (
            <div style={{ padding: 10, color: "#6B7280", fontSize: 12 }}>{t("items.nothingFound")}</div>
          )}
          {results.map((i) => (
            <button
              key={i.id}
              type="button"
              onClick={() => onPick(i)}
              style={{
                display: "flex",
                width: "100%",
                justifyContent: "space-between",
                gap: 12,
                padding: "8px 12px",
                background: "transparent",
                border: "none",
                borderBottom: "1px solid #161d2b",
                color: "#E8EDF5",
                fontSize: 13,
                cursor: "pointer",
                textAlign: "left",
              }}
            >
              <span>
                {i.name}
                {i.categoryName && <span style={{ color: "#6B7280" }}> · {i.categoryName}</span>}
              </span>
              <span style={{ color: "#9CA3AF", whiteSpace: "nowrap" }}>
                {i.priceRetail != null ? formatMoney(i.priceRetail) : ""}
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
