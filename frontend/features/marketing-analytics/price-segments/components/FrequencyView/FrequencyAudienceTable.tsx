"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Users } from "lucide-react";
import { useExplainFrequencyAudience } from "../../hooks/usePriceSegments";
import { RecommendationBlock } from "../RecommendationBlock";
import { ExportFrequencyAudienceButton, PiiUnmaskToggle } from "../ExportButtons";
import { Table, type TableColumn } from "@/components/ui/Table";
import type {
  FrequencyAudienceTableDto,
  FrequencyAudienceRowDto,
  FrequencyAudienceKey,
  FrequencySortBy,
  PriceSegmentsPeriodFilters,
  PriceSegmentKey,
} from "../../types";

interface Props {
  data: FrequencyAudienceTableDto | undefined;
  isLoading: boolean;
  sortBy: FrequencySortBy;
  sortDescending: boolean;
  onSort: (key: FrequencySortBy) => void;
  page: number;
  onPageChange: (p: number) => void;
  audience: FrequencyAudienceKey;
  filters: PriceSegmentsPeriodFilters;
  periodFrom: string;
  periodTo: string;
  declineThresholdPercent: number | undefined;
  minSpend: number | undefined;
  maxSpend: number | undefined;
  priceSegmentFilter: PriceSegmentKey | undefined;
  canExportPii: boolean;
}

/**
 * Server-paginated/sorted table for one frequency audience (Sleeping/Declining/Growing/Other).
 * `frequencyDeltaPercent`/`typicalCheckCurrent` render "—" whenever the backend sends null —
 * NEVER "0"/"∞" (task log 420: null on `typicalCheckCurrent` is guaranteed for every Sleeping row
 * specifically, since a sleeping customer has zero current-period receipts by definition).
 *
 * Migrated to the shared `Table` component (Batch C of the table-unification migration) — same
 * state/hooks as before, only the grid markup + TableControls' SortableHeader/
 * TablePaginationFooter were replaced. `deltaPercent` stays unsortable (no `sortKey`), exactly as
 * the original plain (non-button) header div. Per Table's non-negotiable alignment rule, only
 * column 0 (name) is left-aligned; every other column (including the previously right-aligned
 * numeric ones) is now center-aligned, matching ProductsTable's Batch A precedent.
 */
export function FrequencyAudienceTable({
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
  declineThresholdPercent,
  minSpend,
  maxSpend,
  priceSegmentFilter,
  canExportPii,
}: Props) {
  const t = useTranslations("Dashboard.priceSegments.frequencyTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [unmaskPii, setUnmaskPii] = useState(false);
  const { mutate, isPending, data: explainData, error } = useExplainFrequencyAudience();

  const columns: TableColumn<FrequencyAudienceRowDto>[] = [
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
      key: "previous",
      header: t("headerPrevious"),
      sortKey: "previous",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (r) => r.previousFrequency.toLocaleString(intlLocale),
    },
    {
      key: "current",
      header: t("headerCurrent"),
      sortKey: "current",
      cellStyle: { color: "#E8EDF5", fontSize: 13, fontFamily: "monospace", fontWeight: 600 },
      render: (r) => r.currentFrequency.toLocaleString(intlLocale),
    },
    {
      key: "delta",
      header: t("headerDelta"),
      sortKey: "delta",
      cellStyle: { fontSize: 12, fontFamily: "monospace" },
      render: (r) => (
        <span style={{ color: r.frequencyDeltaAbsolute > 0 ? "#4ADE80" : r.frequencyDeltaAbsolute < 0 ? "#F87171" : "#9CA3AF" }}>
          {r.frequencyDeltaAbsolute > 0 ? "+" : ""}
          {r.frequencyDeltaAbsolute.toLocaleString(intlLocale)}
        </span>
      ),
    },
    {
      key: "deltaPercent",
      header: t("headerDeltaPercent"),
      cellStyle: { color: "#6B7280", fontSize: 12, fontFamily: "monospace" },
      render: (r) =>
        r.frequencyDeltaPercent === null
          ? "—"
          : `${r.frequencyDeltaPercent > 0 ? "+" : ""}${r.frequencyDeltaPercent.toLocaleString(intlLocale, { maximumFractionDigits: 0 })}%`,
    },
    {
      key: "check",
      header: t("headerCheck"),
      sortKey: "check",
      cellStyle: { fontSize: 13, fontFamily: "monospace" },
      render: (r) => (
        <span style={{ color: r.typicalCheckCurrent === null ? "#374151" : "#E8EDF5" }}>
          {r.typicalCheckCurrent === null ? "—" : `${r.typicalCheckCurrent.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`}
        </span>
      ),
    },
    {
      key: "spend",
      header: t("headerSpend"),
      sortKey: "spend",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (r) => `${r.spendCurrentPeriod.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`,
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
            <ExportFrequencyAudienceButton
              audience={audience}
              from={periodFrom}
              to={periodTo}
              storeIds={filters.storeIds}
              declineThresholdPercent={declineThresholdPercent}
              minSpend={minSpend}
              maxSpend={maxSpend}
              priceSegmentFilter={priceSegmentFilter}
              unmaskPii={unmaskPii}
            />
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
            minWidth={900}
          />

          <div
            key={`${audience}:${filters.period}:${filters.from ?? ""}:${filters.to ?? ""}:${filters.storeIds.join(",")}:${declineThresholdPercent ?? ""}`}
          >
            <RecommendationBlock
              recommendation={data.recommendation}
              explain={{
                onExplain: () => mutate({ audience, filters, declineThresholdPercent }),
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
