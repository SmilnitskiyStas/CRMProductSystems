"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Users, Download, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { SortableHeader, TablePaginationFooter } from "@/features/marketing-analytics/price-segments/components/TableControls";
import { PiiUnmaskToggle } from "@/features/marketing-analytics/components/ExportButtons";
import { useExportPostCampaignCustomers } from "../hooks/usePostCampaign";
import type { PostCampaignCustomerSortBy, PostCampaignCustomerTableDto } from "../types";

interface Props {
  segmentId: string;
  storeIds: string[];
  data: PostCampaignCustomerTableDto | undefined;
  isLoading: boolean;
  sortBy: PostCampaignCustomerSortBy;
  sortDescending: boolean;
  onSort: (key: PostCampaignCustomerSortBy) => void;
  page: number;
  onPageChange: (p: number) => void;
  canExportPii: boolean;
}

const GRID = "minmax(140px,1fr) 120px 90px 90px 100px 100px 200px";

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

/**
 * "Споживачі сегмента" (source doc §23) — full SERVER pagination, explicitly NOT a Top-200 cap
 * (source doc §23.3/§35.3's required fix over the competitor: `PostCampaignCustomerTableDto` has
 * no row cap at all, same `SortableHeader`/`TablePaginationFooter` every sibling phase's tables
 * already reuse from `price-segments/components/TableControls.tsx`). ID itself is never sortable
 * (source doc §23.2) — only checks/turnover before/after and the RFM transition. `row.phone` is
 * rendered exactly as the server sent it: already masked/unmasked per the CALLER's own role
 * (`PostCampaignService.GetCustomersAsync`'s `canViewUnmaskedPii` gate) — never re-masked here,
 * same convention `audience-builder/components/BuyersTab/BuyersTable.tsx` documents. The export
 * button's `PiiUnmaskToggle` is a SEPARATE, export-only decision (literal reuse of the generic,
 * stateless component from the Фаза 1 root `ExportButtons.tsx`, per this task's brief).
 */
export function CustomerTable({ segmentId, storeIds, data, isLoading, sortBy, sortDescending, onSort, page, onPageChange, canExportPii }: Props) {
  const t = useTranslations("Dashboard.postCampaign.customerTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [unmaskPii, setUnmaskPii] = useState(false);
  const { mutate, isPending } = useExportPostCampaignCustomers();

  function handleExport() {
    mutate(
      { segmentId, body: { storeIds: storeIds.length > 0 ? storeIds : null, unmaskPii } },
      {
        onSuccess: () => toast.success(t("exportSuccessToast")),
        onError: (e) => toast.error(t("exportErrorToast", { message: errorMessage(e) })),
      },
    );
  }

  return (
    <div style={{ background: "#0A0F1A", border: "1px solid #1F2937", borderRadius: 12, padding: 20, display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", flexWrap: "wrap", gap: 12 }}>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>{t("title")}</div>
          <div style={{ color: "#6B7280", fontSize: 12, marginTop: 3 }}>{t("subtitle")}</div>
          {data && <div style={{ color: "#6B7280", fontSize: 12, marginTop: 3 }}>{t("totalCount", { count: data.totalCount })}</div>}
        </div>
        {data && data.totalCount > 0 && (
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <PiiUnmaskToggle checked={unmaskPii} onChange={setUnmaskPii} allowed={canExportPii} />
            <Btn
              variant="ghost"
              size="sm"
              disabled={isPending}
              icon={isPending ? <Loader2 size={13} className="animate-spin" /> : <Download size={13} />}
              onClick={handleExport}
            >
              {isPending ? t("exporting") : t("exportButton")}
            </Btn>
          </div>
        )}
      </div>

      {isLoading || !data ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "24px 0", textAlign: "center" }}>{t("loading")}</div>
      ) : data.rows.length === 0 ? (
        <div style={{ display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: 10, padding: "36px 20px", color: "#4B5563", textAlign: "center" }}>
          <Users size={30} strokeWidth={1.5} />
          <div style={{ color: "#9CA3AF", fontSize: 14, fontWeight: 600 }}>{t("empty")}</div>
        </div>
      ) : (
        <>
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, overflow: "auto" }}>
            <div style={{ display: "grid", gridTemplateColumns: GRID, padding: "10px 16px", borderBottom: "1px solid #1F2937", background: "#0A1020", minWidth: 780, gap: 8 }}>
              <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase" }}>{t("headerName")}</div>
              <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase" }}>{t("headerPhone")}</div>
              <SortableHeader label={t("headerChecksBefore")} sortKey="checksbefore" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerChecksAfter")} sortKey="checksafter" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerTurnoverBefore")} sortKey="turnoverbefore" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerTurnoverAfter")} sortKey="turnoverafter" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} align="right" />
              <SortableHeader label={t("headerTransition")} sortKey="transition" activeSort={sortBy} activeDescending={sortDescending} onSort={onSort} />
            </div>

            {data.rows.map((r) => (
              <div key={r.customerId} style={{ display: "grid", gridTemplateColumns: GRID, padding: "10px 16px", borderBottom: "1px solid #0F1924", alignItems: "center", minWidth: 780, gap: 8 }}>
                <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{r.name}</div>
                <div style={{ color: r.phone ? "#9CA3AF" : "#374151", fontSize: 12 }}>{r.phone ?? "—"}</div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>{r.checksBefore.toLocaleString(intlLocale)}</div>
                <div style={{ color: "#E8EDF5", fontSize: 13, textAlign: "right", fontFamily: "monospace", fontWeight: 600 }}>{r.checksAfter.toLocaleString(intlLocale)}</div>
                <div style={{ color: "#9CA3AF", fontSize: 12, textAlign: "right", fontFamily: "monospace" }}>{r.turnoverBefore.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴</div>
                <div style={{ color: "#4ADE80", fontSize: 13, textAlign: "right", fontFamily: "monospace" }}>{r.turnoverAfter.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴</div>
                <div style={{ color: "#9CA3AF", fontSize: 11.5, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {r.segmentBeforeLabelUa} <span style={{ color: "#4B5563" }}>→</span> <span style={{ color: "#E8EDF5" }}>{r.segmentAfterLabelUa}</span>
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
