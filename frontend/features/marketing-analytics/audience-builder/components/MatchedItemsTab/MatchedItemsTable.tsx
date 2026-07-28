"use client";

import { useTranslations, useLocale } from "next-intl";
import { PackageSearch } from "lucide-react";
import { SortableHeader, TablePaginationFooter } from "@/features/marketing-analytics/price-segments/components/TableControls";
import { BulkSelectionActions } from "./BulkSelectionActions";
import { useAudienceBuilderStore } from "../../store/useAudienceBuilderStore";
import type { MatchedItemsSortBy, MatchedItemsTableDto } from "../../types";

interface Props {
  data: MatchedItemsTableDto | undefined;
  isLoading: boolean;
  sortBy: MatchedItemsSortBy;
  sortDescending: boolean;
  onSort: (key: MatchedItemsSortBy) => void;
  page: number;
  onPageChange: (p: number) => void;
  /** Server-computed count of matched items still counted toward the audience (the own-overview
   * query's `itemsInSelectionCount`) — the correct numerator for "Обрано X з Y": it already
   * accounts for exclusions the same way the Buyers tab's KPI card does, across the FULL matched
   * set, not just the current page. */
  selectedCount: number | undefined;
}

const GRID = "40px minmax(160px,1fr) 160px 100px 90px 100px";

/**
 * "Знайдені товари" (analysis §20) — checkbox column, ";"-joined barcodes, zero-sales SKUs
 * included (never filtered out). Unchecking a row calls `store.excludeItem`/`includeItem`; the
 * resulting request-object change refetches overview/buyers/matched-items together — the mandatory
 * "миттєвий перерахунок" requirement — see the store's own doc comment for why this table
 * deliberately does not try to dodge that refetch with a client-only optimistic flag.
 */
export function MatchedItemsTable({ data, isLoading, sortBy, sortDescending, onSort, page, onPageChange, selectedCount }: Props) {
  const t = useTranslations("Dashboard.audienceBuilder.matchedItemsTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const excludeItem = useAudienceBuilderStore((s) => s.excludeItem);
  const includeItem = useAudienceBuilderStore((s) => s.includeItem);

  const currentPageItemIds = data?.rows.map((r) => r.itemId) ?? [];

  return (
    <div style={{ background: "#0A0F1A", border: "1px solid #1F2937", borderRadius: 12, padding: 20, display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", flexWrap: "wrap", gap: 12 }}>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>{t("title")}</div>
          <div style={{ color: "#6B7280", fontSize: 12, marginTop: 3, maxWidth: 420 }}>{t("hint")}</div>
          {data && selectedCount != null && (
            <div style={{ color: "#6B7280", fontSize: 12, marginTop: 3 }}>{t("selectedCount", { selected: selectedCount, total: data.totalCount })}</div>
          )}
        </div>
        {data && data.totalCount > 0 && <BulkSelectionActions currentPageItemIds={currentPageItemIds} />}
      </div>

      {isLoading || !data ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "24px 0", textAlign: "center" }}>{t("loading")}</div>
      ) : data.rows.length === 0 ? (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            gap: 10,
            padding: "36px 20px",
            color: "#4B5563",
            textAlign: "center",
          }}
        >
          <PackageSearch size={30} strokeWidth={1.5} />
          <div style={{ color: "#9CA3AF", fontSize: 14, fontWeight: 600 }}>{t("empty")}</div>
        </div>
      ) : (
        <>
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, overflow: "auto" }}>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: GRID,
                padding: "10px 16px",
                borderBottom: "1px solid #1F2937",
                background: "#0A1020",
                minWidth: 700,
                gap: 8,
                alignItems: "center",
              }}
            >
              <div />
              <SortableHeader label={t("headerName")} sortKey="name" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} />
              <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase" }}>{t("headerBarcode")}</div>
              <SortableHeader label={t("headerSold")} sortKey="sold" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerReceipts")} sortKey="receipts" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerBuyers")} sortKey="buyers" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
            </div>

            {data.rows.map((r) => (
              <div
                key={r.itemId}
                style={{ display: "grid", gridTemplateColumns: GRID, padding: "10px 16px", borderBottom: "1px solid #0F1924", alignItems: "center", minWidth: 700, gap: 8 }}
              >
                <input
                  type="checkbox"
                  checked={!r.isExcluded}
                  onChange={() => (r.isExcluded ? includeItem(r.itemId) : excludeItem(r.itemId))}
                  style={{ accentColor: "#3B82F6", width: 15, height: 15, cursor: "pointer" }}
                />
                <div
                  style={{
                    color: r.isExcluded ? "#4B5563" : "#E8EDF5",
                    fontSize: 13,
                    fontWeight: 500,
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap",
                    textDecoration: r.isExcluded ? "line-through" : "none",
                  }}
                >
                  {r.name}
                </div>
                <div style={{ color: "#6B7280", fontSize: 12, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {r.barcodesJoined ?? "—"}
                </div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>
                  {r.quantitySold.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}
                </div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>
                  {r.receiptCount.toLocaleString(intlLocale)}
                </div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>
                  {r.buyerCount.toLocaleString(intlLocale)}
                </div>
              </div>
            ))}
          </div>

          <TablePaginationFooter page={page} totalPages={data.totalPages} totalLabel={t("totalCount", { count: data.totalCount })} onPageChange={onPageChange} />
        </>
      )}
    </div>
  );
}
