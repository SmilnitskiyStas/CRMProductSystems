"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Table, type TableColumn } from "@/components/ui/Table";
import { usePosStoreDetail } from "../hooks/usePosAnalytics";
import type { PosCashierDetail } from "../types";

interface Props {
  from: string;
  to: string;
  storeId?: string;
}

const card: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 10,
};

const toggleBtn: React.CSSProperties = {
  padding: "8px 16px",
  fontSize: 13,
  fontWeight: 500,
  borderRadius: 6,
  border: "1px solid #1F2937",
  cursor: "pointer",
  background: "#1D3461",
  color: "#93C5FD",
};

const muted: React.CSSProperties = { color: "#4B5563", fontSize: 13 };

// Collapsed by default: the detail query only fires (enabled) after the user opens the section.
export function PosStoreDetailPanel({ from, to, storeId }: Props) {
  const t = useTranslations("Dashboard.analytics.pos.storeDetail");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [open, setOpen] = useState(false);

  const { data, isLoading, isError } = usePosStoreDetail({ from, to, store_id: storeId }, open);

  const money = (v: number) =>
    `${v.toLocaleString(intlLocale, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₴`;
  const hours = (v: number) =>
    t("hoursUnit", { value: v.toLocaleString(intlLocale, { maximumFractionDigits: 1 }) });

  const columns: TableColumn<PosCashierDetail>[] = [
    { key: "cashier", header: t("headers.cashier"), cellStyle: { color: "#E8EDF5", fontWeight: 500 }, render: (c) => c.cashierName },
    { key: "revenue", header: t("headers.revenue"), cellStyle: { color: "#60A5FA", fontFamily: "monospace" }, render: (c) => money(c.totalRevenue) },
    { key: "receipts", header: t("headers.receipts"), cellStyle: { color: "#9CA3AF", fontFamily: "monospace" }, render: (c) => c.transactionCount.toLocaleString(intlLocale) },
    { key: "avg", header: t("headers.averageTicket"), cellStyle: { color: "#9CA3AF", fontFamily: "monospace" }, render: (c) => money(c.averageTicket) },
    { key: "shifts", header: t("headers.shifts"), cellStyle: { color: "#9CA3AF", fontFamily: "monospace" }, render: (c) => c.shiftCount.toLocaleString(intlLocale) },
    { key: "hours", header: t("headers.hours"), cellStyle: { color: "#9CA3AF", fontFamily: "monospace" }, render: (c) => hours(c.hoursWorked) },
    { key: "perHour", header: t("headers.perHour"), cellStyle: { color: "#9CA3AF", fontFamily: "monospace" }, render: (c) => (c.revenuePerHour == null ? "—" : money(c.revenuePerHour)) },
    ...(data?.costIncluded
      ? [{
          key: "profit",
          header: t("headers.profit"),
          cellStyle: { color: "#34D399", fontFamily: "monospace" },
          render: (c: PosCashierDetail) => (c.profit == null ? "—" : money(c.profit)),
        } satisfies TableColumn<PosCashierDetail>]
      : []),
  ];

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
        <button style={toggleBtn} onClick={() => setOpen((v) => !v)} aria-expanded={open}>
          {open ? t("hide") : t("show")}
        </button>
        {!open && <span style={muted}>{t("hint")}</span>}
      </div>

      {open && !storeId && <div style={{ ...card, padding: "20px 16px", ...muted }}>{t("selectStore")}</div>}

      {open && storeId && isLoading && <div style={muted}>{tCommon("loading")}</div>}
      {open && storeId && isError && <div style={{ ...muted, color: "#F87171" }}>{t("error")}</div>}

      {open && storeId && data && (
        <div style={{ ...card, overflow: "hidden" }}>
          <div style={{ padding: "16px 16px 12px", borderBottom: "1px solid #1F2937", display: "flex", flexWrap: "wrap", gap: 24, alignItems: "baseline" }}>
            <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{t("title", { store: data.storeName })}</div>
            <div style={{ color: "#9CA3AF", fontSize: 12 }}>
              {t("totalRevenue")}: <span style={{ color: "#60A5FA", fontFamily: "monospace" }}>{money(data.totalRevenue)}</span>
            </div>
            {data.totalProfit != null && (
              <div style={{ color: "#9CA3AF", fontSize: 12 }}>
                {t("totalProfit")}: <span style={{ color: "#34D399", fontFamily: "monospace" }}>{money(data.totalProfit)}</span>
              </div>
            )}
            <div style={{ color: "#9CA3AF", fontSize: 12 }}>
              {t("hoursWorked")}: <span style={{ fontFamily: "monospace" }}>{hours(data.hoursWorked)}</span>
            </div>
          </div>
          {data.cashiers.length === 0 ? (
            <div style={{ padding: "20px 16px", ...muted, textAlign: "center" }}>{t("empty")}</div>
          ) : (
            <Table columns={columns} rows={data.cashiers} rowKey={(c) => c.cashierId} />
          )}
          <div style={{ padding: "10px 16px", borderTop: "1px solid #1F2937", color: "#6B7280", fontSize: 11, display: "flex", flexDirection: "column", gap: 2 }}>
            {data.costIncluded ? (
              <>
                {data.costCoveragePercent != null && (
                  <span>{t("coverage", { value: data.costCoveragePercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 }) })}</span>
                )}
                <span>{t("profitEstimate")}</span>
              </>
            ) : (
              <span>{t("noCostNote")}</span>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
