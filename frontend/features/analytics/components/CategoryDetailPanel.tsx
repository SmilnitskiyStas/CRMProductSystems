"use client";

import { useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { X } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { canViewAnalyticsMargin } from "@/lib/roles";
import { Table, type TableColumn } from "@/components/ui/Table";
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
  | "marginPercent"
  | "daysOfStockRemaining";

interface Props {
  /** null = the "uncategorized" bucket — same domain convention as CategoryStatusChart's
   * onCategoryClick and the by-category table's row id. */
  categoryId: string | null;
  /** Current (never compare-period) range already resolved on the page — this panel is a
   * snapshot detail view of one category, not a trend, so it deliberately has no date filter
   * of its own and never reads the page's compare-toggle state. */
  from: string;
  to: string;
  /** Scopes the breakdown to the given stores; omitted/empty means network-wide, aggregated
   * across every store — same convention as every other storeIds filter on this page (TASK-611
   * widening of the previous singular store_id). */
  storeIds?: string[];
  onClose: () => void;
  /** TASK-488: row-click drill-down — opens ProductTrendPanel (the same panel PosTopProductsTable
   * already opens on /analytics/pos) for the clicked product. Omitted means the product name
   * renders as plain text, no click handler — same opt-in convention as PosTopProductsTable's own
   * onRowClick? (TASK-484). */
  onProductClick?: (productId: string, productName: string) => void;
}

const PAGE_SIZE = 10;

function compareRows(a: CategoryProductRowDto, b: CategoryProductRowDto, key: SortKey): number {
  if (key === "name") return a.productName.localeCompare(b.productName);
  const av = a[key] ?? -Infinity;
  const bv = b[key] ?? -Infinity;
  return av - bv;
}

/** TASK-488: product-name click target when onProductClick is provided. Deliberately not the
 * whole grid row (this row's other cells are just status/margin figures, not separate nav
 * targets) and deliberately not styled like SortableHeader's uppercase/gray sort buttons above —
 * a background chip on hover, reusing the same #111827 hover accent PosTopProductsTable's row
 * hover (and its active-row highlight) already use elsewhere in this feature, so the affordance
 * reads as "clickable" without being confused with column-sorting. */
const productNameButton: React.CSSProperties = {
  display: "block",
  width: "100%",
  textAlign: "left",
  background: "transparent",
  border: "none",
  borderRadius: 6,
  padding: "2px 6px",
  margin: "-2px -6px",
  color: "#E8EDF5",
  fontSize: 13,
  fontWeight: 500,
  fontFamily: "inherit",
  overflow: "hidden",
  textOverflow: "ellipsis",
  whiteSpace: "nowrap",
  cursor: "pointer",
  transition: "background 0.1s",
};

function marginColor(v: number | null): string {
  if (v == null || v === 0) return "#6B7280";
  return v > 0 ? "#4ADE80" : "#F87171";
}

/** Urgency color for daysOfStockRemaining (TASK-494) — reuses this table's own status-cell
 * palette (p.critical/p.warning/p.safe colors a few lines below: #F87171/#FBBF24/#4ADE80)
 * rather than inventing a new one, so "12 days left" reads with the same urgency tone as the
 * expiry-status columns already do. null (no store scope on this page, or no ADU signal yet —
 * see the field's doc comment in types.ts) renders neutral gray, matching marginColor's own
 * null/zero tone above. Thresholds (< 7 = critical, < 30 = warning) are a judgment call, not a
 * value from the spec — no existing "low stock" threshold elsewhere in this codebase to match. */
function daysOfStockColor(v: number | null): string {
  if (v == null) return "#6B7280";
  if (v < 7) return "#F87171";
  if (v < 30) return "#FBBF24";
  return "#4ADE80";
}

/**
 * Drill-down for the by-category section on /analytics (interactive-analytics-and-margin plan,
 * TASK-483) — renders below CategoryStatusChart + the by-category table when a category (or the
 * uncategorized bucket) is selected, never in place of them. Owns its own fetch; sort/pagination
 * are entirely client-side (the by-category/products endpoint returns the full product list for
 * one category in a single response, unlike the server-paginated tables elsewhere in the app).
 * Migrated to the shared `Table` component (table-unification migration, Batch B) — same
 * sortKey/sortDescending/page state as before, wired directly into Table's props instead of
 * SortableHeader/TablePaginationFooter.
 */
export function CategoryDetailPanel({ categoryId, from, to, storeIds, onClose, onProductClick }: Props) {
  const t = useTranslations("Dashboard.analytics.categoryDetailPanel");
  const tStatus = useTranslations("Dashboard.analytics.status");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { data: me } = useMe();
  const canViewMargin = canViewAnalyticsMargin(me?.role, me?.permissions);

  const { data, isLoading } = useCategoryProductBreakdown({ category_id: categoryId, from, to, storeIds });

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
  // security requirement), not just visually hidden — the array below only pushes the two extra
  // columns when they're actually going to render. daysOfStockRemaining (TASK-494) is NOT
  // margin-gated — it's operational data, visible to the same audience as every other column
  // here — so it's appended unconditionally, always the last column.
  // Status/margin column widths (100px / 150px) are carried over from this panel's pre-Table
  // CSS-grid layout, where they were needed to stop long header labels ("ПОПЕРЕДЖЕННЯ" — 12
  // chars, "Маржа % (оцінна)" — 16 chars) from bleeding into neighboring columns. A real
  // `<table>` reflows instead of overlapping, so they're no longer strictly required for that
  // reason, but they still keep these short numeric columns visually compact rather than
  // stretching to share whatever extra width the table has — kept via `column.width`.
  const columns: TableColumn<CategoryProductRowDto>[] = [
    {
      key: "name",
      header: t("headers.product"),
      sortKey: "name",
      render: (p) =>
        onProductClick ? (
          <button
            type="button"
            onClick={() => onProductClick(p.productId, p.productName)}
            title={p.productName}
            style={productNameButton}
            onMouseEnter={(e) => ((e.currentTarget as HTMLElement).style.background = "#111827")}
            onMouseLeave={(e) => ((e.currentTarget as HTMLElement).style.background = "transparent")}
          >
            {p.productName}
          </button>
        ) : (
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
            {p.productName}
          </div>
        ),
    },
    {
      key: "safe",
      header: tStatus("safe"),
      sortKey: "safe",
      width: 100,
      cellStyle: { fontFamily: "monospace", color: "#4ADE80" },
      render: (p) => p.safe,
    },
    {
      key: "warning",
      header: tStatus("warning"),
      sortKey: "warning",
      width: 100,
      cellStyle: { fontFamily: "monospace", color: "#FBBF24" },
      render: (p) => p.warning,
    },
    {
      key: "critical",
      header: tStatus("critical"),
      sortKey: "critical",
      width: 100,
      cellStyle: { fontFamily: "monospace", color: "#F87171" },
      render: (p) => p.critical,
    },
    {
      key: "expired",
      header: tStatus("expired"),
      sortKey: "expired",
      width: 100,
      cellStyle: { fontFamily: "monospace", color: "#DC2626" },
      render: (p) => p.expired,
    },
    {
      key: "totalQuantity",
      header: t("headers.totalQuantity"),
      sortKey: "totalQuantity",
      cellStyle: { fontFamily: "monospace" },
      render: (p) => p.totalQuantity.toLocaleString(intlLocale),
    },
    {
      key: "salesRevenue",
      header: t("headers.salesRevenue"),
      sortKey: "salesRevenue",
      cellStyle: { fontFamily: "monospace", color: "#E8EDF5" },
      render: (p) => `${p.salesRevenue.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`,
    },
    {
      key: "unitsSold",
      header: t("headers.unitsSold"),
      sortKey: "unitsSold",
      cellStyle: { fontFamily: "monospace" },
      render: (p) => p.unitsSold.toLocaleString(intlLocale),
    },
    ...(canViewMargin
      ? ([
          {
            key: "marginAmount",
            header: t("headers.marginAmount"),
            sortKey: "marginAmount",
            width: 150,
            render: (p) => (
              <span style={{ fontFamily: "monospace", color: marginColor(p.marginAmount) }}>
                {p.marginAmount == null ? "—" : `${p.marginAmount.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`}
              </span>
            ),
          },
          {
            key: "marginPercent",
            header: t("headers.marginPercent"),
            sortKey: "marginPercent",
            width: 150,
            render: (p) => (
              <span style={{ fontFamily: "monospace", color: marginColor(p.marginPercent) }}>
                {p.marginPercent == null ? "—" : `${p.marginPercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%`}
              </span>
            ),
          },
        ] as TableColumn<CategoryProductRowDto>[])
      : []),
    {
      key: "daysOfStockRemaining",
      header: t("headers.daysOfStockRemaining"),
      sortKey: "daysOfStockRemaining",
      width: 100,
      render: (p) => (
        <span style={{ fontFamily: "monospace", color: daysOfStockColor(p.daysOfStockRemaining) }}>
          {p.daysOfStockRemaining == null
            ? "—"
            : t("daysOfStockValue", { days: p.daysOfStockRemaining.toLocaleString(intlLocale, { maximumFractionDigits: 1 }) })}
        </span>
      ),
    },
  ];

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
        <Table
          columns={columns}
          rows={pageRows}
          rowKey={(p) => p.productId}
          sortBy={sortKey}
          sortDescending={sortDescending}
          onSort={(key) => handleSort(key as SortKey)}
          page={page}
          totalPages={totalPages}
          totalCount={sorted.length}
          onPageChange={setPage}
        />
      )}
    </div>
  );
}
