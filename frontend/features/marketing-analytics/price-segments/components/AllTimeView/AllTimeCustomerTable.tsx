"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Users } from "lucide-react";
import { SortableHeader, TablePaginationFooter } from "../TableControls";
import { ExportAllTimeButton, PiiUnmaskToggle } from "../ExportButtons";
import type { AllTimeCustomerTableDto, AllTimeSortBy, PriceSegmentKey } from "../../types";

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

const GRID = "minmax(150px,1fr) 120px 150px 110px 110px 90px 110px";

/**
 * Server-paginated/sorted "Весь час" customer table — shows the whole base when no segment is
 * selected, or one tier when SegmentDistributionChart's bar/button filter is active. No
 * recommendation block here on purpose: the recommendation lives in SegmentRecommendationCard,
 * sourced from the overview response (segment-scoped, not row-scoped), not this table's DTO —
 * `AllTimeCustomerTableDto` genuinely carries no `recommendation` field (unlike the comparison
 * and frequency tables, which do).
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
        <>
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, overflow: "auto" }}>
            <div style={{ display: "grid", gridTemplateColumns: GRID, padding: "10px 16px", borderBottom: "1px solid #1F2937", background: "#0A1020", minWidth: 820, gap: 8 }}>
              <SortableHeader label={t("headerName")} sortKey="name" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} />
              <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase" }}>{t("headerPhone")}</div>
              <SortableHeader label={t("headerSegment")} sortKey="segment" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} />
              <SortableHeader label={t("headerItems")} sortKey="items" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerCheck")} sortKey="check" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerPurchases")} sortKey="purchases" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerLtv")} sortKey="ltv" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
            </div>

            {data.rows.map((r) => (
              <div
                key={r.customerId}
                style={{ display: "grid", gridTemplateColumns: GRID, padding: "10px 16px", borderBottom: "1px solid #0F1924", alignItems: "center", minWidth: 820, gap: 8 }}
              >
                <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{r.name}</div>
                <div style={{ color: r.phone ? "#9CA3AF" : "#374151", fontSize: 12 }}>{r.phone ?? "—"}</div>
                <div style={{ color: "#E8EDF5", fontSize: 12, fontWeight: 600, whiteSpace: "nowrap" }}>{r.segmentLabelUa}</div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>
                  {r.itemsPerReceipt.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}
                </div>
                <div style={{ color: "#E8EDF5", fontSize: 13, textAlign: "right", fontFamily: "monospace" }}>
                  {r.typicalCheck.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴
                </div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>{r.purchaseCount.toLocaleString(intlLocale)}</div>
                <div style={{ color: "#4ADE80", fontSize: 13, textAlign: "right", fontFamily: "monospace" }}>{r.ltv.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴</div>
              </div>
            ))}
          </div>

          <TablePaginationFooter page={page} totalPages={data.totalPages} totalLabel={t("totalCount", { count: data.totalCount })} onPageChange={onPageChange} />
        </>
      )}
    </div>
  );
}
