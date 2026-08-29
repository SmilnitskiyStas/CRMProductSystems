"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Users } from "lucide-react";
import { ExportAllTimeButton, PiiUnmaskToggle } from "../ExportButtons";
import { Table, type TableColumn } from "@/components/ui/Table";
import type { AllTimeCustomerTableDto, AllTimeCustomerRowDto, AllTimeSortBy, PriceSegmentKey } from "../../types";

interface Props {
  data: AllTimeCustomerTableDto | undefined;
  isLoading: boolean;
  sortBy: AllTimeSortBy;
  sortDescending: boolean;
  onSort: (key: AllTimeSortBy) => void;
  page: number;
  onPageChange: (p: number) => void;
  segment: PriceSegmentKey | null;
  storeIds: string[];
  canExportPii: boolean;
}

/**
 * Server-paginated/sorted "Весь час" customer table — shows the whole base when no segment is
 * selected, or one tier when SegmentDistributionChart's bar/button filter is active. No
 * recommendation block here on purpose: the recommendation lives in SegmentRecommendationCard,
 * sourced from the overview response (segment-scoped, not row-scoped), not this table's DTO —
 * `AllTimeCustomerTableDto` genuinely carries no `recommendation` field (unlike the comparison
 * and frequency tables, which do).
 *
 * Migrated to the shared `Table` component (Batch C of the table-unification migration) — same
 * state/hooks as before, only the grid markup + TableControls' SortableHeader/
 * TablePaginationFooter were replaced. Per Table's non-negotiable alignment rule, only column 0
 * (name) is left-aligned; every other column (including the previously right-aligned numeric
 * ones) is now center-aligned, matching ProductsTable's Batch A precedent.
 */
export function AllTimeCustomerTable({
  data,
  isLoading,
  sortBy,
  sortDescending,
  onSort,
  page,
  onPageChange,
  segment,
  storeIds,
  canExportPii,
}: Props) {
  const t = useTranslations("Dashboard.priceSegments.allTimeTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [unmaskPii, setUnmaskPii] = useState(false);

  const columns: TableColumn<AllTimeCustomerRowDto>[] = [
    {
      key: "name",
      header: t("headerName"),
      sortKey: "name",
      cellStyle: { color: "#E8EDF5", fontWeight: 500, whiteSpace: "nowrap" },
      render: (r) => r.name,
    },
    {
      key: "phone",
      header: t("headerPhone"),
      render: (r) => <span style={{ color: r.phone ? "#9CA3AF" : "#374151", fontSize: 12 }}>{r.phone ?? "—"}</span>,
    },
    {
      key: "segment",
      header: t("headerSegment"),
      sortKey: "segment",
      cellStyle: { color: "#E8EDF5", fontSize: 12, fontWeight: 600, whiteSpace: "nowrap" },
      render: (r) => r.segmentLabelUa,
    },
    {
      key: "items",
      header: t("headerItems"),
      sortKey: "items",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (r) => r.itemsPerReceipt.toLocaleString(intlLocale, { maximumFractionDigits: 1 }),
    },
    {
      key: "check",
      header: t("headerCheck"),
      sortKey: "check",
      cellStyle: { color: "#E8EDF5", fontSize: 13, fontFamily: "monospace" },
      render: (r) => `${r.typicalCheck.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`,
    },
    {
      key: "purchases",
      header: t("headerPurchases"),
      sortKey: "purchases",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (r) => r.purchaseCount.toLocaleString(intlLocale),
    },
    {
      key: "ltv",
      header: t("headerLtv"),
      sortKey: "ltv",
      cellStyle: { color: "#4ADE80", fontSize: 13, fontFamily: "monospace" },
      render: (r) => `${r.ltv.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`,
    },
  ];

  return (
    <div style={{ background: "#0A0F1A", border: "1px solid #1F2937", borderRadius: 12, padding: 20, display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 12 }}>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>{t("title")}</div>
          {data && <div style={{ color: "#6B7280", fontSize: 12, marginTop: 3 }}>{t("totalCount", { count: data.totalCount })}</div>}
        </div>
        {data && data.totalCount > 0 && (
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <PiiUnmaskToggle checked={unmaskPii} onChange={setUnmaskPii} allowed={canExportPii} />
            <ExportAllTimeButton segment={segment} storeIds={storeIds} unmaskPii={unmaskPii} />
          </div>
        )}
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
          <Users size={30} strokeWidth={1.5} />
          <div style={{ color: "#9CA3AF", fontSize: 14, fontWeight: 600 }}>{t("empty")}</div>
        </div>
      ) : (
        <Table
          columns={columns}
          rows={data.rows}
          rowKey={(r) => r.customerId}
          sortBy={sortBy}
          sortDescending={sortDescending}
          onSort={onSort}
          page={page}
          totalPages={data.totalPages}
          totalCount={data.totalCount}
          onPageChange={onPageChange}
          minWidth={820}
        />
      )}
    </div>
  );
}
