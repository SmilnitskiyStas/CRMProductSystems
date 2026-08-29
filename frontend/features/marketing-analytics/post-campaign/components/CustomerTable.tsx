"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Users, Download, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import { PiiUnmaskToggle } from "@/features/marketing-analytics/components/ExportButtons";
import { useExportPostCampaignCustomers } from "../hooks/usePostCampaign";
import type { PostCampaignCustomerRowDto, PostCampaignCustomerSortBy, PostCampaignCustomerTableDto } from "../types";

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

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

/**
 * "Споживачі сегмента" (source doc §23) — full SERVER pagination, explicitly NOT a Top-200 cap
 * (source doc §23.3/§35.3's required fix over the competitor: `PostCampaignCustomerTableDto` has
 * no row cap at all), rendered via the shared `components/ui/Table` (sort/pagination state owned
 * here, same as every sibling phase's table). ID itself is never sortable
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

  const columns: TableColumn<PostCampaignCustomerRowDto>[] = [
    {
      key: "name",
      header: t("headerName"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (r) => r.name,
    },
    {
      key: "phone",
      header: t("headerPhone"),
      render: (r) => <span style={{ color: r.phone ? "#9CA3AF" : "#374151" }}>{r.phone ?? "—"}</span>,
    },
    {
      key: "checksBefore",
      header: t("headerChecksBefore"),
      sortKey: "checksbefore",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (r) => r.checksBefore.toLocaleString(intlLocale),
    },
    {
      key: "checksAfter",
      header: t("headerChecksAfter"),
      sortKey: "checksafter",
      cellStyle: { color: "#E8EDF5", fontSize: 13, fontFamily: "monospace", fontWeight: 600 },
      render: (r) => r.checksAfter.toLocaleString(intlLocale),
    },
    {
      key: "turnoverBefore",
      header: t("headerTurnoverBefore"),
      sortKey: "turnoverbefore",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (r) => `${r.turnoverBefore.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`,
    },
    {
      key: "turnoverAfter",
      header: t("headerTurnoverAfter"),
      sortKey: "turnoverafter",
      cellStyle: { color: "#4ADE80", fontSize: 13, fontFamily: "monospace" },
      render: (r) => `${r.turnoverAfter.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`,
    },
    {
      key: "transition",
      header: t("headerTransition"),
      sortKey: "transition",
      cellStyle: { color: "#9CA3AF", fontSize: 11.5, whiteSpace: "nowrap" },
      render: (r) => (
        <>
          {r.segmentBeforeLabelUa} <span style={{ color: "#4B5563" }}>→</span> <span style={{ color: "#E8EDF5" }}>{r.segmentAfterLabelUa}</span>
        </>
      ),
    },
  ];

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
      )}
    </div>
  );
}
