"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Users } from "lucide-react";
import { useExplainPriceAudience } from "../hooks/usePriceSegments";
import { RecommendationBlock } from "./RecommendationBlock";
import { ExportPriceAudienceButton, PiiUnmaskToggle } from "./ExportButtons";
import { Table, type TableColumn } from "@/components/ui/Table";
import type { PriceAudienceTableDto, PriceAudienceRowDto, PriceAudienceKey, PriceAudienceSortBy, PriceSegmentsPeriodFilters } from "../types";

interface Props {
  data: PriceAudienceTableDto | undefined;
  isLoading: boolean;
  sortBy: PriceAudienceSortBy;
  sortDescending: boolean;
  onSort: (key: PriceAudienceSortBy) => void;
  page: number;
  onPageChange: (p: number) => void;
  audience: PriceAudienceKey;
  filters: PriceSegmentsPeriodFilters;
  /** Concrete resolved YYYY-MM-DD range for the CURRENT filters (from the already-loaded
   * overview's periodFrom/periodTo) — exports need real dates even when the UI filter is a
   * preset like "30" — re-deriving "30 days ago" client-side could subtly disagree with the
   * backend's own resolution, so the server's own resolved range is reused instead (same
   * convention as RFM's SegmentDetailPanel). */
  periodFrom: string;
  periodTo: string;
  canExportPii: boolean;
}

/**
 * Server-paginated/sorted table for one comparison-mode audience (RealGrowth/PriceGrowth/
 * Declining/Stable). Segment columns show CURRENCY RANGE labels ("was → now"), not audience
 * names — `previousSegmentLabelUa`/`currentSegmentLabelUa` come straight from the DTO.
 *
 * Migrated to the shared `Table` component (Batch C of the table-unification migration) — same
 * state/hooks as before, only the grid markup + TableControls' SortableHeader/
 * TablePaginationFooter were replaced. Per Table's non-negotiable alignment rule, only column 0
 * (name) is left-aligned; every other column (including the previously right-aligned numeric
 * ones) is now center-aligned, matching ProductsTable's Batch A precedent.
 */
export function PriceAudienceTable({
  data,
  isLoading,
  sortBy,
  sortDescending,
  onSort,
  page,
  onPageChange,
  audience,
  filters,
  periodFrom,
  periodTo,
  canExportPii,
}: Props) {
  const t = useTranslations("Dashboard.priceSegments.priceAudienceTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [unmaskPii, setUnmaskPii] = useState(false);
  const { mutate, isPending, data: explainData, error } = useExplainPriceAudience();

  const columns: TableColumn<PriceAudienceRowDto>[] = [
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
      cellStyle: { color: "#D1D5DB", fontSize: 12 },
      render: (r) => (
        <span style={{ display: "inline-flex", alignItems: "center", gap: 4, whiteSpace: "nowrap" }}>
          <span>{r.previousSegmentLabelUa}</span>
          <span style={{ color: "#4B5563" }}>→</span>
          <span style={{ color: "#E8EDF5", fontWeight: 600 }}>{r.currentSegmentLabelUa}</span>
        </span>
      ),
    },
    {
      key: "items",
      header: t("headerItems"),
      sortKey: "items",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace", whiteSpace: "nowrap" },
      render: (r) =>
        `${r.itemsPerReceiptPrevious.toLocaleString(intlLocale, { maximumFractionDigits: 1 })} → ${r.itemsPerReceiptCurrent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}`,
    },
    {
      key: "check",
      header: t("headerCheck"),
      sortKey: "check",
      cellStyle: { color: "#E8EDF5", fontSize: 13, fontFamily: "monospace" },
      render: (r) => `${r.typicalCheckCurrent.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`,
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
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>{data?.labelUa ?? "…"}</div>
          {data && (
            <div style={{ color: "#6B7280", fontSize: 12, marginTop: 3 }}>
              {t("totalCount", { count: data.totalCount })} · {t("withPhoneCount", { count: data.withPhoneCount })}
            </div>
          )}
        </div>
        {data && data.totalCount > 0 && (
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <PiiUnmaskToggle checked={unmaskPii} onChange={setUnmaskPii} allowed={canExportPii} />
            <ExportPriceAudienceButton audience={audience} from={periodFrom} to={periodTo} storeIds={filters.storeIds} unmaskPii={unmaskPii} />
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
        <>
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
            minWidth={780}
          />

          <div key={`${audience}:${filters.period}:${filters.from ?? ""}:${filters.to ?? ""}:${filters.storeIds.join(",")}`}>
            <RecommendationBlock
              recommendation={data.recommendation}
              explain={{
                onExplain: () => mutate({ audience, filters }),
                isPending,
                explanationUa: explainData?.explanationUa,
                error,
              }}
            />
          </div>
        </>
      )}
    </div>
  );
}
