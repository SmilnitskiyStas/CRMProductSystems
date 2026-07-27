"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Users } from "lucide-react";
import { useExplainFrequencyAudience } from "../../hooks/usePriceSegments";
import { RecommendationBlock } from "../RecommendationBlock";
import { SortableHeader, TablePaginationFooter } from "../TableControls";
import { ExportFrequencyAudienceButton, PiiUnmaskToggle } from "../ExportButtons";
import type {
  FrequencyAudienceTableDto,
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

const GRID = "minmax(150px,1fr) 120px 90px 90px 90px 90px 110px 110px 100px";

/**
 * Server-paginated/sorted table for one frequency audience (Sleeping/Declining/Growing/Other).
 * `frequencyDeltaPercent`/`typicalCheckCurrent` render "—" whenever the backend sends null —
 * NEVER "0"/"∞" (task log 420: null on `typicalCheckCurrent` is guaranteed for every Sleeping row
 * specifically, since a sleeping customer has zero current-period receipts by definition).
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
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, overflow: "auto" }}>
            <div style={{ display: "grid", gridTemplateColumns: GRID, padding: "10px 16px", borderBottom: "1px solid #1F2937", background: "#0A1020", minWidth: 900, gap: 8 }}>
              <SortableHeader label={t("headerName")} sortKey="name" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} />
              <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase" }}>{t("headerPhone")}</div>
              <SortableHeader label={t("headerPrevious")} sortKey="previous" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerCurrent")} sortKey="current" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerDelta")} sortKey="delta" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", textAlign: "right" }}>{t("headerDeltaPercent")}</div>
              <SortableHeader label={t("headerCheck")} sortKey="check" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerSpend")} sortKey="spend" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerLtv")} sortKey="ltv" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
            </div>

            {data.rows.map((r) => (
              <div
                key={r.customerId}
                style={{ display: "grid", gridTemplateColumns: GRID, padding: "10px 16px", borderBottom: "1px solid #0F1924", alignItems: "center", minWidth: 900, gap: 8 }}
              >
                <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{r.name}</div>
                <div style={{ color: r.phone ? "#9CA3AF" : "#374151", fontSize: 12 }}>{r.phone ?? "—"}</div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>{r.previousFrequency.toLocaleString(intlLocale)}</div>
                <div style={{ color: "#E8EDF5", fontSize: 13, textAlign: "right", fontFamily: "monospace", fontWeight: 600 }}>
                  {r.currentFrequency.toLocaleString(intlLocale)}
                </div>
                <div
                  style={{
                    color: r.frequencyDeltaAbsolute > 0 ? "#4ADE80" : r.frequencyDeltaAbsolute < 0 ? "#F87171" : "#9CA3AF",
                    fontSize: 12,
                    textAlign: "right",
                    fontFamily: "monospace",
                  }}
                >
                  {r.frequencyDeltaAbsolute > 0 ? "+" : ""}
                  {r.frequencyDeltaAbsolute.toLocaleString(intlLocale)}
                </div>
                <div style={{ color: "#6B7280", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>
                  {r.frequencyDeltaPercent === null
                    ? "—"
                    : `${r.frequencyDeltaPercent > 0 ? "+" : ""}${r.frequencyDeltaPercent.toLocaleString(intlLocale, { maximumFractionDigits: 0 })}%`}
                </div>
                <div style={{ color: r.typicalCheckCurrent === null ? "#374151" : "#E8EDF5", fontSize: 13, textAlign: "right", fontFamily: "monospace" }}>
                  {r.typicalCheckCurrent === null ? "—" : `${r.typicalCheckCurrent.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`}
                </div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>
                  {r.spendCurrentPeriod.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴
                </div>
                <div style={{ color: "#4ADE80", fontSize: 13, textAlign: "right", fontFamily: "monospace" }}>{r.ltv.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴</div>
              </div>
            ))}
          </div>

          <TablePaginationFooter page={page} totalPages={data.totalPages} totalLabel={t("totalCount", { count: data.totalCount })} onPageChange={onPageChange} />

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
