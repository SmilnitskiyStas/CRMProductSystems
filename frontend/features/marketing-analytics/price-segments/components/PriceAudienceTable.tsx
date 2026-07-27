"use client";

import { useTranslations, useLocale } from "next-intl";
import { Users } from "lucide-react";
import { useExplainPriceAudience } from "../hooks/usePriceSegments";
import { RecommendationBlock } from "./RecommendationBlock";
import { SortableHeader, TablePaginationFooter } from "./TableControls";
import { ExportPriceAudienceButton, PiiUnmaskToggle } from "./ExportButtons";
import type { PriceAudienceTableDto, PriceAudienceKey, PriceAudienceSortBy, PriceSegmentsPeriodFilters } from "../types";

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

const GRID = "minmax(150px,1fr) 120px 190px 130px 110px 110px";

/**
 * Server-paginated/sorted table for one comparison-mode audience (RealGrowth/PriceGrowth/
 * Declining/Stable). Segment columns show CURRENCY RANGE labels ("was → now"), not audience
 * names — `previousSegmentLabelUa`/`currentSegmentLabelUa` come straight from the DTO.
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
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, overflow: "auto" }}>
            <div style={{ display: "grid", gridTemplateColumns: GRID, padding: "10px 16px", borderBottom: "1px solid #1F2937", background: "#0A1020", minWidth: 780, gap: 8 }}>
              <SortableHeader label={t("headerName")} sortKey="name" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} />
              <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase" }}>{t("headerPhone")}</div>
              <SortableHeader label={t("headerSegment")} sortKey="segment" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} />
              <SortableHeader label={t("headerItems")} sortKey="items" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerCheck")} sortKey="check" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerLtv")} sortKey="ltv" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
            </div>

            {data.rows.map((r) => (
              <div
                key={r.customerId}
                style={{ display: "grid", gridTemplateColumns: GRID, padding: "10px 16px", borderBottom: "1px solid #0F1924", alignItems: "center", minWidth: 780, gap: 8 }}
              >
                <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{r.name}</div>
                <div style={{ color: r.phone ? "#9CA3AF" : "#374151", fontSize: 12 }}>{r.phone ?? "—"}</div>
                <div style={{ color: "#D1D5DB", fontSize: 12, display: "flex", alignItems: "center", gap: 4, whiteSpace: "nowrap" }}>
                  <span>{r.previousSegmentLabelUa}</span>
                  <span style={{ color: "#4B5563" }}>→</span>
                  <span style={{ color: "#E8EDF5", fontWeight: 600 }}>{r.currentSegmentLabelUa}</span>
                </div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace", whiteSpace: "nowrap" }}>
                  {r.itemsPerReceiptPrevious.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}
                  {" → "}
                  {r.itemsPerReceiptCurrent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}
                </div>
                <div style={{ color: "#E8EDF5", fontSize: 13, textAlign: "right", fontFamily: "monospace" }}>
                  {r.typicalCheckCurrent.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴
                </div>
                <div style={{ color: "#4ADE80", fontSize: 13, textAlign: "right", fontFamily: "monospace" }}>
                  {r.ltv.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴
                </div>
              </div>
            ))}
          </div>

          <TablePaginationFooter
            page={page}
            totalPages={data.totalPages}
            totalLabel={t("totalCount", { count: data.totalCount })}
            onPageChange={onPageChange}
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

import { useState } from "react";
