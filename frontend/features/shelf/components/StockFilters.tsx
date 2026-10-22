"use client";

import { useTranslations } from "next-intl";
import { useCategories } from "@/features/inventory/hooks/useCategories";
import { RangeFilter } from "@/components/ui/RangeFilter";
import { Btn } from "@/components/ui/Btn";
import { filterInputStyle } from "@/components/ui/fieldStyles";

interface StockFilters {
  status: string;
  search: string;
  category_id: string;
  min_quantity?: number;
  max_quantity?: number;
}

interface Props {
  filters: StockFilters;
  onChange: (f: StockFilters) => void;
}

const STATUS_VALUES = ["warning", "critical", "expired", "safe", "needs_verification"] as const;


export function StockFilters({ filters, onChange }: Props) {
  const t = useTranslations("Dashboard.shelf.stockFilters");
  const tStatus = useTranslations("Dashboard.shelf.status");
  const { data: categories = [] } = useCategories();

  const hasActiveFilters =
    filters.status ||
    filters.search ||
    filters.category_id ||
    filters.min_quantity != null ||
    filters.max_quantity != null;

  return (
    <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
      <input
        type="text"
        placeholder={t("searchPlaceholder")}
        value={filters.search}
        onChange={(e) => onChange({ ...filters, search: e.target.value })}
        style={{ ...filterInputStyle, width: 260 }}
      />

      <select
        value={filters.status}
        onChange={(e) => onChange({ ...filters, status: e.target.value })}
        style={{ ...filterInputStyle, cursor: "pointer" }}
      >
        <option value="">{t("allStatuses")}</option>
        {STATUS_VALUES.map((value) => (
          <option key={value} value={value}>
            {tStatus(value)}
          </option>
        ))}
      </select>

      <select
        value={filters.category_id}
        onChange={(e) => onChange({ ...filters, category_id: e.target.value })}
        style={{ ...filterInputStyle, cursor: "pointer" }}
      >
        <option value="">{t("allCategories")}</option>
        {categories.map((c) => (
          <option key={c.id} value={c.id}>
            {c.name}
          </option>
        ))}
      </select>

      <RangeFilter
        min={filters.min_quantity}
        max={filters.max_quantity}
        onChange={(next) => onChange({ ...filters, min_quantity: next.min, max_quantity: next.max })}
        placeholder={t("quantityRangeLabel")}
      />

      {hasActiveFilters && (
        <Btn
          variant="ghost"
          onClick={() =>
            onChange({ status: "", search: "", category_id: "", min_quantity: undefined, max_quantity: undefined })
          }
        >
          {t("reset")}
        </Btn>
      )}
    </div>
  );
}
