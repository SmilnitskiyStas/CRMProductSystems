"use client";

import { useTranslations, useLocale } from "next-intl";
import { Users } from "lucide-react";
import { SortableHeader, TablePaginationFooter } from "@/features/marketing-analytics/price-segments/components/TableControls";
import { ExportReceiptsButton } from "./ExportReceiptsButton";
import type { AudienceBuyerTableDto, AudienceFilterState, BuyersSortBy } from "../../types";

interface Props {
  data: AudienceBuyerTableDto | undefined;
  isLoading: boolean;
  sortBy: BuyersSortBy;
  sortDescending: boolean;
  onSort: (key: BuyersSortBy) => void;
  page: number;
  onPageChange: (p: number) => void;
  filter: AudienceFilterState;
  canExportPii: boolean;
}

const GRID = "minmax(160px,1fr) 140px 120px 100px 130px";

/**
 * "Знайдені покупці" (analysis §14.2/§14.3) — ПІБ / Телефон / Куплено шт / Чеків / Сума ₴, real
 * server-side pagination + sorting via the SAME SortableHeader/TablePaginationFooter price-
 * segments already built (`price-segments/components/TableControls.tsx`) — reused directly per
 * the brief, not duplicated. `row.phone` is rendered exactly as the server sent it: already
 * masked/unmasked server-side per the viewer's own role (see AudienceBuyerRowDto's doc comment in
 * types.ts) — never re-masked here.
 */
export function BuyersTable({ data, isLoading, sortBy, sortDescending, onSort, page, onPageChange, filter, canExportPii }: Props) {
  const t = useTranslations("Dashboard.audienceBuilder.buyersTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  return (
    <div style={{ background: "#0A0F1A", border: "1px solid #1F2937", borderRadius: 12, padding: 20, display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", flexWrap: "wrap", gap: 12 }}>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>{t("title")}</div>
          <div style={{ color: "#6B7280", fontSize: 12, marginTop: 3, maxWidth: 420 }}>{t("subtitle")}</div>
          {data && (
            <div style={{ color: "#6B7280", fontSize: 12, marginTop: 3 }}>
              {t("totalCount", { count: data.totalCount })} · {t("withPhoneCount", { count: data.withPhoneCount })}
            </div>
          )}
        </div>
        {data && data.totalCount > 0 && <ExportReceiptsButton filter={filter} canExportPii={canExportPii} />}
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
            <div style={{ display: "grid", gridTemplateColumns: GRID, padding: "10px 16px", borderBottom: "1px solid #1F2937", background: "#0A1020", minWidth: 650, gap: 8 }}>
              <SortableHeader label={t("headerName")} sortKey="name" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} />
              <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase" }}>{t("headerPhone")}</div>
              <SortableHeader label={t("headerQty")} sortKey="qty" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerReceipts")} sortKey="receipts" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerAmount")} sortKey="amount" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
            </div>

            {data.rows.map((r) => (
              <div
                key={r.customerId}
                style={{ display: "grid", gridTemplateColumns: GRID, padding: "10px 16px", borderBottom: "1px solid #0F1924", alignItems: "center", minWidth: 650, gap: 8 }}
              >
                <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {r.name}
                </div>
                <div style={{ color: r.phone ? "#9CA3AF" : "#374151", fontSize: 12 }}>{r.phone ?? "—"}</div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>
                  {r.quantityPurchased.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}
                </div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>
                  {r.receiptCount.toLocaleString(intlLocale)}
                </div>
                <div style={{ color: "#4ADE80", fontSize: 13, textAlign: "right", fontFamily: "monospace" }}>
                  {r.totalAmount.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴
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
