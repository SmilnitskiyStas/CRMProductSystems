"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Users, Download, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { SortableHeader, TablePaginationFooter } from "@/features/marketing-analytics/price-segments/components/TableControls";
import { useExportCompetitorBuyers } from "../../hooks/useAudienceBuilder";
import { buildExportCompetitorRequest } from "../../api/audienceBuilder";
import type { BuyersSortBy, CompetitorBuyerTableDto, CompetitorFilterState } from "../../types";

interface Props {
  data: CompetitorBuyerTableDto | undefined;
  isLoading: boolean;
  sortBy: BuyersSortBy;
  sortDescending: boolean;
  onSort: (key: BuyersSortBy) => void;
  page: number;
  onPageChange: (p: number) => void;
  filter: CompetitorFilterState;
  canExportPii: boolean;
}

const GRID = "minmax(160px,1fr) 140px 120px 100px 130px";

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

/**
 * "Покупці конкурента" (analysis §19) — same ПІБ/Телефон/Куплено шт/Чеків/Сума ₴ shape as the own-
 * product buyers table, server-paginated/sorted via the same shared TableControls. Export here is
 * CUSTOMER-level, not receipt-level (task log 429: "the competitor tab is not a raffle/draw
 * scenario") — no separate ExportButton file was named for this tab in the brief, so the export
 * mutation + PII toggle live inline here rather than in their own file (unlike the Buyers tab,
 * which explicitly gets its own `ExportReceiptsButton.tsx`).
 */
export function CompetitorBuyersTable({ data, isLoading, sortBy, sortDescending, onSort, page, onPageChange, filter, canExportPii }: Props) {
  const t = useTranslations("Dashboard.audienceBuilder.competitorTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { mutate, isPending } = useExportCompetitorBuyers();
  const [unmaskPii, setUnmaskPii] = useState(false);

  function handleExport() {
    mutate(buildExportCompetitorRequest(filter, unmaskPii), {
      onSuccess: () => toast.success(t("exportSuccessToast")),
      onError: (e) => toast.error(t("exportErrorToast", { message: errorMessage(e) })),
    });
  }

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
        {data && data.totalCount > 0 && (
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            {canExportPii && (
              <label style={{ display: "flex", alignItems: "center", gap: 7, cursor: "pointer" }}>
                <input
                  type="checkbox"
                  checked={unmaskPii}
                  onChange={(e) => setUnmaskPii(e.target.checked)}
                  style={{ accentColor: "#3B82F6", width: 14, height: 14, cursor: "pointer" }}
                />
                <span style={{ color: "#9CA3AF", fontSize: 12 }}>{t("unmaskPiiLabel")}</span>
              </label>
            )}
            <Btn
              variant="ghost"
              size="sm"
              disabled={isPending}
              icon={isPending ? <Loader2 size={13} className="animate-spin" /> : <Download size={13} />}
              onClick={handleExport}
            >
              {isPending ? t("downloading") : t("exportButton")}
            </Btn>
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
                <div style={{ color: "#FBBF24", fontSize: 13, textAlign: "right", fontFamily: "monospace" }}>
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
