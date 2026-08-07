"use client";

import { useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { X } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { canViewAnalyticsMargin } from "@/lib/roles";
import { SortableHeader, TablePaginationFooter } from "@/features/marketing-analytics/price-segments/components/TableControls";
import { useCategoryProductBreakdown } from "../hooks/useAnalytics";
import type { CategoryProductRowDto } from "../types";

type SortKey =
  | "name"
  | "safe"
  | "warning"
  | "critical"
  | "expired"
  | "totalQuantity"
  | "salesRevenue"
  | "unitsSold"
  | "marginAmount"
  | "marginPercent";

interface Props {
  /** null = the "uncategorized" bucket — same domain convention as CategoryStatusChart's
   * onCategoryClick and the by-category table's row id. */
  categoryId: string | null;
  /** Current (never compare-period) range already resolved on the page — this panel is a
   * snapshot detail view of one category, not a trend, so it deliberately has no date filter
   * of its own and never reads the page's compare-toggle state. */
  from: string;
  to: string;
  onClose: () => void;
}

const PAGE_SIZE = 10;

function compareRows(a: CategoryProductRowDto, b: CategoryProductRowDto, key: SortKey): number {
  if (key === "name") return a.productName.localeCompare(b.productName);
  const av = a[key] ?? -Infinity;
  const bv = b[key] ?? -Infinity;
  return av - bv;
}

const numCell: React.CSSProperties = {
  color: "#9CA3AF",
  fontSize: 12,
  textAlign: "right",
  fontFamily: "monospace",
};

function marginColor(v: number | null): string {
  if (v == null || v === 0) return "#6B7280";
  return v > 0 ? "#4ADE80" : "#F87171";
}

/**
 * Drill-down for the by-category section on /analytics (interactive-analytics-and-margin plan,
 * TASK-483) — renders below CategoryStatusChart + the by-category table when a category (or the
 * uncategorized bucket) is selected, never in place of them. Owns its own fetch; sort/pagination
 * are entirely client-side (the by-category/products endpoint returns the full product list for
 * one category in a single response, unlike the server-paginated price-segments tables this
 * reuses SortableHeader/TablePaginationFooter from).
 */
export function CategoryDetailPanel({ categoryId, from, to, onClose }: Props) {
  const t = useTranslations("Dashboard.analytics.categoryDetailPanel");
  const tStatus = useTranslations("Dashboard.analytics.status");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { data: me } = useMe();
  const canViewMargin = canViewAnalyticsMargin(me?.role, me?.permissions);

  const { data, isLoading } = useCategoryProductBreakdown({ category_id: categoryId, from, to });

  const [sortKey, setSortKey] = useState<SortKey>("salesRevenue");
  const [sortDescending, setSortDescending] = useState(true);
  const [page, setPage] = useState(1);

  function handleSort(key: SortKey) {
    if (key === sortKey) {
      setSortDescending((d) => !d);
    } else {
      setSortKey(key);
      setSortDescending(true);
    }
    setPage(1);
  }

  const products = data?.products ?? [];
  const sorted = useMemo(() => {
    const copy = products.slice().sort((a, b) => compareRows(a, b, sortKey));
    return sortDescending ? copy.reverse() : copy;
  }, [products, sortKey, sortDescending]);

  const totalPages = Math.max(1, Math.ceil(sorted.length / PAGE_SIZE));
  const pageRows = sorted.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);

  // Margin columns must be entirely absent from the DOM when canViewMargin is false (ADR-027 /
  // security requirement), not just visually hidden — the grid template itself only reserves
  // the two extra columns when they're actually going to render.
  const GRID = canViewMargin
    ? "minmax(160px,1.4fr) 56px 56px 56px 56px 90px 110px 90px 110px 90px"
    : "minmax(160px,1.4fr) 56px 56px 56px 56px 90px 110px 90px";
  const gridMinWidth = canViewMargin ? 980 : 760;

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "20px 16px",
        display: "flex",
        flexDirection: "column",
        gap: 16,
        marginTop: 16,
      }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 16 }}>
        <div style={{ minWidth: 0 }}>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>
            {t("title", { category: data?.categoryName ?? "…" })}
          </div>
          {canViewMargin && (
            <div style={{ color: "#6B7280", fontSize: 11.5, marginTop: 4, fontStyle: "italic", maxWidth: 620 }}>
              {t("marginDisclaimer")}
            </div>
          )}
        </div>
        <button
          onClick={onClose}
          title={t("closeButton")}
          style={{
            background: "#111827",
            border: "1px solid #1F2937",
            borderRadius: 8,
            width: 30,
            height: 30,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            cursor: "pointer",
            color: "#9CA3AF",
            flexShrink: 0,
          }}
        >
          <X size={15} />
        </button>
      </div>

      {isLoading || !data ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "12px 0", textAlign: "center" }}>{tCommon("loading")}</div>
      ) : products.length === 0 ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "12px 0", textAlign: "center" }}>{t("empty")}</div>
      ) : (
        <>
          <div style={{ background: "#0A0F1A", border: "1px solid #1F2937", borderRadius: 10, overflow: "auto" }}>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: GRID,
                padding: "10px 14px",
                borderBottom: "1px solid #1F2937",
                background: "#0A1020",
                minWidth: gridMinWidth,
                gap: 8,
              }}
            >
              <SortableHeader label={t("headers.product")} sortKey="name" activeSort={sortKey} activeDescending={sortDescending} onSort={handleSort} />
              <SortableHeader label={tStatus("safe")} sortKey="safe" activeSort={sortKey} activeDescending={sortDescending} onSort={handleSort} align="right" />
              <SortableHeader label={tStatus("warning")} sortKey="warning" activeSort={sortKey} activeDescending={sortDescending} onSort={handleSort} align="right" />
              <SortableHeader label={tStatus("critical")} sortKey="critical" activeSort={sortKey} activeDescending={sortDescending} onSort={handleSort} align="right" />
              <SortableHeader label={tStatus("expired")} sortKey="expired" activeSort={sortKey} activeDescending={sortDescending} onSort={handleSort} align="right" />
              <SortableHeader label={t("headers.totalQuantity")} sortKey="totalQuantity" activeSort={sortKey} activeDescending={sortDescending} onSort={handleSort} align="right" />
              <SortableHeader label={t("headers.salesRevenue")} sortKey="salesRevenue" activeSort={sortKey} activeDescending={sortDescending} onSort={handleSort} align="right" />
              <SortableHeader label={t("headers.unitsSold")} sortKey="unitsSold" activeSort={sortKey} activeDescending={sortDescending} onSort={handleSort} align="right" />
              {canViewMargin && (
                <>
                  <SortableHeader label={t("headers.marginAmount")} sortKey="marginAmount" activeSort={sortKey} activeDescending={sortDescending} onSort={handleSort} align="right" />
                  <SortableHeader label={t("headers.marginPercent")} sortKey="marginPercent" activeSort={sortKey} activeDescending={sortDescending} onSort={handleSort} align="right" />
                </>
              )}
            </div>

            {pageRows.map((p) => (
              <div
                key={p.productId}
                style={{
                  display: "grid",
                  gridTemplateColumns: GRID,
                  padding: "9px 14px",
                  borderBottom: "1px solid #0F1924",
                  alignItems: "center",
                  minWidth: gridMinWidth,
                  gap: 8,
                }}
              >
                <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {p.productName}
                </div>
                <div style={{ ...numCell, color: "#4ADE80" }}>{p.safe}</div>
                <div style={{ ...numCell, color: "#FBBF24" }}>{p.warning}</div>
                <div style={{ ...numCell, color: "#F87171" }}>{p.critical}</div>
                <div style={{ ...numCell, color: "#DC2626" }}>{p.expired}</div>
                <div style={numCell}>{p.totalQuantity.toLocaleString(intlLocale)}</div>
                <div style={{ ...numCell, color: "#E8EDF5" }}>{p.salesRevenue.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴</div>
                <div style={numCell}>{p.unitsSold.toLocaleString(intlLocale)}</div>
                {canViewMargin && (
                  <>
                    <div style={{ ...numCell, color: marginColor(p.marginAmount) }}>
                      {p.marginAmount == null ? "—" : `${p.marginAmount.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`}
                    </div>
                    <div style={{ ...numCell, color: marginColor(p.marginPercent) }}>
                      {p.marginPercent == null ? "—" : `${p.marginPercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%`}
                    </div>
                  </>
                )}
              </div>
            ))}
          </div>

          <TablePaginationFooter page={page} totalPages={totalPages} totalLabel={t("totalCount", { count: sorted.length })} onPageChange={setPage} />
        </>
      )}
    </div>
  );
}
