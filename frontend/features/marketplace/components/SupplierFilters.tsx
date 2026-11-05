"use client";

import { useTranslations } from "next-intl";
import { RegionSelect } from "@/features/geo/components/RegionSelect";
import type { MarketplaceFilters } from "../types";
import { Btn } from "@/components/ui/Btn";
import { filterInputStyle } from "@/components/ui/fieldStyles";

interface Props {
  filters: MarketplaceFilters;
  onChange: (filters: MarketplaceFilters) => void;
  categories: string[];
}

export function SupplierFilters({ filters, onChange, categories }: Props) {
  const t = useTranslations("Dashboard.marketplace.filters");
  const tPlan = useTranslations("Dashboard.marketplace.planLabel");

  return (
    <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
      {/* Region filter — structured taxonomy (TASK-655) */}
      <div style={{ width: 200 }}>
        <RegionSelect
          value={filters.regionCode || null}
          onChange={(code) => onChange({ ...filters, regionCode: code ?? "" })}
          allowEmpty
          placeholder={t("regionPlaceholder")}
        />
      </div>

      {/* Category filter */}
      <select
        value={filters.category}
        onChange={(e) => onChange({ ...filters, category: e.target.value })}
        style={{ ...filterInputStyle, width: 200, cursor: "pointer" }}
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
            <Btn
              key={plan}
              variant={active ? "primary" : "ghost"}
              onClick={() => onChange({ ...filters, plan })}
              style={{ padding: "7px 14px" }}
            >
              {tPlan(plan)}
            </Btn>
          );
        })}
      </div>
    </div>
  );
}
