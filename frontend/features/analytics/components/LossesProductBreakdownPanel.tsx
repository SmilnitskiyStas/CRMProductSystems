"use client";

import { useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { X } from "lucide-react";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useLossesByProduct } from "../hooks/useAnalytics";
import type { LossByProductRowDto } from "../types";

type SortKey = "name" | "quantity" | "lossAmount" | "sharePercent";

interface Props {
  /** Rendered as-is in the header — the caller (page) composes this from whichever dimension
   * (store name or localized reason label) is currently selected, since LossByProductRowDto
   * itself carries no display name for the dimension being drilled into. */
  title: string;
  /** The already-known total for this dimension (from the parent chart/table's own data), shown
   * immediately so the header doesn't flash empty while this panel's own fetch is in flight;
   * overridden by the fetch's own `totalLoss` once it resolves (the two should always agree). */
  totalLoss: number;
  /** storeIds and reason are independent filters on the same `losses/by-product` endpoint (backend
   * applies both together when both are set — see PosAnalyticsServiceTests's
   * GetLossesByProductAsync_store_and_reason_filters_are_forwarded_unchanged). The losses-by-store
   * drill-down passes a single-element array (the clicked row's store id), overriding any
   * page-wide store selection for that one store's data. The losses-by-reason and by-day
   * drill-downs pass the page's current header-selected stores (TASK-611: full array, not just
   * the primary one) as storeIds alongside reason/from-to, so this panel stays scoped to whatever
   * store(s) the rest of /analytics is currently showing. */
  storeIds?: string[];
  reason?: string;
  /** Current (never compare-period) range — snapshot detail view, not a trend. */
  from: string;
  to: string;
  onClose: () => void;
  /** TASK-488: row-click drill-down — opens ProductTrendPanel (the same panel PosTopProductsTable
   * already opens on /analytics/pos) for the clicked product. Omitted means the product name
   * renders as plain text, no click handler — same opt-in convention as PosTopProductsTable's own
   * onRowClick? (TASK-484). */
  onProductClick?: (productId: string, productName: string) => void;
}

const PAGE_SIZE = 10;

function compareRows(a: LossByProductRowDto, b: LossByProductRowDto, key: SortKey): number {
  if (key === "name") return a.productName.localeCompare(b.productName);
  return a[key] - b[key];
}

/** TASK-488: product-name click target when onProductClick is provided — same treatment as
 * CategoryDetailPanel.tsx's own productNameButton (not the whole grid row, not styled like
 * SortableHeader's sort buttons above; a background chip on hover reusing the #111827 hover
 * accent PosTopProductsTable's row hover already uses elsewhere in this feature). */
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

/**
 * Shared drill-down for BOTH the losses-by-store and losses-by-reason sections on /analytics
 * (interactive-analytics-and-margin plan, TASK-483) — one component, parameterized by whichever
 * single filter the caller passed. No margin columns: LossByProductRowDto carries none at all
 * (ADR-027 §1 — losses aren't margin-gated, LossAmount is already shown unrestricted in
 * aggregate elsewhere on this page for every store_manager+).
 * Migrated to the shared `Table` component (table-unification migration, Batch B) — same
 * sortKey/sortDescending/page state as before, wired directly into Table's props instead of
 * SortableHeader/TablePaginationFooter.
 */
export function LossesProductBreakdownPanel({ title, totalLoss, storeIds, reason, from, to, onClose, onProductClick }: Props) {
  const t = useTranslations("Dashboard.analytics.lossesProductBreakdownPanel");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { data, isLoading } = useLossesByProduct({ storeIds, reason, from, to });

  const [sortKey, setSortKey] = useState<SortKey>("lossAmount");
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
  const displayTotalLoss = data?.totalLoss ?? totalLoss;

  const columns: TableColumn<LossByProductRowDto>[] = [
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
      key: "quantity",
      header: t("headers.quantity"),
      sortKey: "quantity",
      cellStyle: { fontFamily: "monospace" },
      render: (p) => p.quantity.toLocaleString(intlLocale),
    },
    {
      key: "lossAmount",
      header: t("headers.lossAmount"),
      sortKey: "lossAmount",
      cellStyle: { fontFamily: "monospace", color: "#F87171" },
      render: (p) => `${p.lossAmount.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`,
    },
    {
      key: "sharePercent",
      header: t("headers.sharePercent"),
      sortKey: "sharePercent",
      cellStyle: { fontFamily: "monospace" },
      render: (p) => `${p.sharePercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%`,
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
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>{title}</div>
          <div style={{ color: "#F87171", fontSize: 12, marginTop: 4 }}>
            {t("totalLossLabel", { amount: displayTotalLoss.toLocaleString(intlLocale, { maximumFractionDigits: 0 }) })}
          </div>
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
