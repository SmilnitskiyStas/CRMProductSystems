"use client";

import { useTranslations } from "next-intl";
import type { MarketplaceFilters } from "../types";

interface Props {
  filters: MarketplaceFilters;
  onChange: (filters: MarketplaceFilters) => void;
  categories: string[];
}

export function SupplierFilters({ filters, onChange, categories }: Props) {
  const t = useTranslations("Dashboard.marketplace.filters");
  const tPlan = useTranslations("Dashboard.marketplace.planLabel");
  const inputStyle: React.CSSProperties = {
    padding: "8px 12px",
    background: "#111827",
    border: "1px solid #1F2937",
    borderRadius: 8,
    color: "#E8EDF5",
    fontSize: 13,
    outline: "none",
  };

  return (
    <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
      {/* Region filter */}
      <input
        type="text"
        placeholder={t("regionPlaceholder")}
        value={filters.region}
        onChange={(e) => onChange({ ...filters, region: e.target.value })}
        style={{ ...inputStyle, width: 200 }}
      />

      {/* Category filter */}
      <select
        value={filters.category}
        onChange={(e) => onChange({ ...filters, category: e.target.value })}
        style={{ ...inputStyle, width: 200, cursor: "pointer" }}
      >
        <option value="">{t("allCategoriesOption")}</option>
        {categories.map((cat) => (
          <option key={cat} value={cat}>
            {cat}
          </option>
        ))}
      </select>

      {/* Plan filter */}
      <div style={{ display: "flex", gap: 4 }}>
        {(["all", "free", "premium"] as const).map((plan) => {
          const active = filters.plan === plan;
          return (
            <button
              key={plan}
              onClick={() => onChange({ ...filters, plan })}
              style={{
                padding: "7px 14px",
                borderRadius: 8,
                border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
                background: active ? "#1D3461" : "transparent",
                color: active ? "#93C5FD" : "#6B7280",
                fontSize: 13,
                cursor: "pointer",
                fontWeight: active ? 600 : 400,
                transition: "all 0.1s",
              }}
            >
              {tPlan(plan)}
            </button>
          );
        })}
      </div>
    </div>
  );
}
