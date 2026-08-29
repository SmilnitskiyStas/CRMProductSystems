"use client";

import { useTranslations, useLocale } from "next-intl";
import { Users } from "lucide-react";
import { Table, type TableColumn } from "@/components/ui/Table";
import { ExportReceiptsButton } from "./ExportReceiptsButton";
import type { AudienceBuyerRowDto, AudienceBuyerTableDto, AudienceFilterState, BuyersSortBy } from "../../types";

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

/**
 * "Знайдені покупці" (analysis §14.2/§14.3) — ПІБ / Телефон / Куплено шт / Чеків / Сума ₴, real
 * server-side pagination + sorting via the shared `components/ui/Table`. `row.phone` is rendered
 * exactly as the server sent it: already
 * masked/unmasked server-side per the viewer's own role (see AudienceBuyerRowDto's doc comment in
 * types.ts) — never re-masked here.
 */
export function BuyersTable({ data, isLoading, sortBy, sortDescending, onSort, page, onPageChange, filter, canExportPii }: Props) {
  const t = useTranslations("Dashboard.audienceBuilder.buyersTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const columns: TableColumn<AudienceBuyerRowDto>[] = [
    {
      key: "name",
      header: t("headerName"),
      sortKey: "name",
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (r) => r.name,
    },
    {
      key: "phone",
      header: t("headerPhone"),
      render: (r) => <span style={{ color: r.phone ? "#9CA3AF" : "#374151" }}>{r.phone ?? "—"}</span>,
    },
    {
      key: "qty",
      header: t("headerQty"),
      sortKey: "qty",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (r) => r.quantityPurchased.toLocaleString(intlLocale, { maximumFractionDigits: 1 }),
    },
    {
      key: "receipts",
      header: t("headerReceipts"),
      sortKey: "receipts",
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (r) => r.receiptCount.toLocaleString(intlLocale),
    },
    {
      key: "amount",
      header: t("headerAmount"),
      sortKey: "amount",
      cellStyle: { color: "#4ADE80", fontSize: 13, fontFamily: "monospace" },
      render: (r) => `${r.totalAmount.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`,
    },
  ];

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
          minWidth={650}
        />
      )}
    </div>
  );
}
