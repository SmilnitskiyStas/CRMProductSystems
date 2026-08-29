"use client";

import { useTranslations, useLocale } from "next-intl";
import { PackageSearch } from "lucide-react";
import { Table, type TableColumn } from "@/components/ui/Table";
import { BulkSelectionActions } from "./BulkSelectionActions";
import { useAudienceBuilderStore } from "../../store/useAudienceBuilderStore";
import type { MatchedItemRowDto, MatchedItemsSortBy, MatchedItemsTableDto } from "../../types";

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

  const columns: TableColumn<MatchedItemRowDto>[] = [
    {
      key: "select",
      width: 40,
      align: "center",
      header: null,
      render: (r) => (
        <input
          type="checkbox"
          checked={!r.isExcluded}
          onChange={() => (r.isExcluded ? includeItem(r.itemId) : excludeItem(r.itemId))}
          style={{ accentColor: "#3B82F6", width: 15, height: 15, cursor: "pointer" }}
        />
      ),
    },
    {
      key: "name",
      align: "left",
      header: t("headerName"),
      sortKey: "name",
      render: (r) => (
        <div
          title={r.name}
          style={{
            overflow: "hidden",
            textOverflow: "ellipsis",
            whiteSpace: "nowrap",
            fontWeight: 500,
            color: r.isExcluded ? "#4B5563" : "#E8EDF5",
            textDecoration: r.isExcluded ? "line-through" : "none",
          }}
        >
          {r.name}
        </div>
      ),
    },
    {
      key: "barcode",
      header: t("headerBarcode"),
      cellStyle: { color: "#6B7280", fontSize: 12, whiteSpace: "nowrap" },
      render: (r) => r.barcodesJoined ?? "—",
    },
    {
      key: "sold",
      header: t("headerSold"),
      sortKey: "sold",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (r) => r.quantitySold.toLocaleString(intlLocale, { maximumFractionDigits: 1 }),
    },
    {
      key: "receipts",
      header: t("headerReceipts"),
      sortKey: "receipts",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (r) => r.receiptCount.toLocaleString(intlLocale),
    },
    {
      key: "buyers",
      header: t("headerBuyers"),
      sortKey: "buyers",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (r) => r.buyerCount.toLocaleString(intlLocale),
    },
  ];

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
        <Table
          columns={columns}
          rows={data.rows}
          rowKey={(r) => r.itemId}
          sortBy={sortBy}
          sortDescending={sortDescending}
          onSort={onSort}
          page={page}
          totalPages={data.totalPages}
          totalCount={data.totalCount}
          onPageChange={onPageChange}
          minWidth={700}
        />
      )}
    </div>
  );
}
