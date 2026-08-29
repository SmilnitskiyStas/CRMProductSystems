"use client";

import { useTranslations, useLocale } from "next-intl";
import { Table, type TableColumn } from "@/components/ui/Table";
import type { PosCashierStatsDto, PosCashierStat } from "../types";

interface Props {
  data: PosCashierStatsDto;
}

export function PosCashierStatsTable({ data }: Props) {
  const t = useTranslations("Dashboard.analytics.pos.cashiers");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (!data || data.cashiers.length === 0) {
    return (
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 10,
          padding: "20px 16px",
          color: "#4B5563",
          fontSize: 13,
          textAlign: "center",
        }}
      >
        {t("empty")}
      </div>
    );
  }

  const columns: TableColumn<PosCashierStat>[] = [
    {
      key: "cashier",
      header: t("headers.cashier"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (c) => c.cashierName,
    },
    {
      key: "revenue",
      header: t("headers.revenue"),
      cellStyle: { color: "#60A5FA", fontFamily: "monospace" },
      render: (c) => `${c.totalRevenue.toLocaleString(intlLocale)} ₴`,
    },
    {
      key: "receipts",
      header: t("headers.receipts"),
      cellStyle: { color: "#9CA3AF", fontFamily: "monospace" },
      render: (c) => c.transactionCount.toLocaleString(intlLocale),
    },
    {
      key: "averageTicket",
      header: t("headers.averageTicket"),
      cellStyle: { color: "#9CA3AF", fontFamily: "monospace" },
      render: (c) =>
        `${c.averageTicket.toLocaleString(intlLocale, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₴`,
    },
    {
      key: "shifts",
      header: t("headers.shifts"),
      cellStyle: { color: "#9CA3AF", fontFamily: "monospace" },
      render: (c) => c.shiftCount.toLocaleString(intlLocale),
    },
  ];

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, overflow: "hidden" }}>
      <div style={{ padding: "16px 16px 12px", borderBottom: "1px solid #1F2937" }}>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{t("title")}</div>
      </div>
      <Table columns={columns} rows={data.cashiers} rowKey={(c) => c.cashierId} />
    </div>
  );
}
