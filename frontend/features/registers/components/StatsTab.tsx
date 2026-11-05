"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useRegisterStats } from "../hooks/useRegisters";
import type { SalesTotals, StatsPeriod } from "../types";
import { formatMoney } from "./shared";

const PERIODS: StatsPeriod[] = ["today", "7d", "30d"];

/** Register row followed by its per-operator rows, flattened for the shared Table. */
interface StatsRow {
  key: string;
  label: string;
  totals: SalesTotals;
  isOperator: boolean;
}

export function StatsTab({ storeId }: { storeId?: string }) {
  const t = useTranslations("Dashboard.registers.stats");
  const [period, setPeriod] = useState<StatsPeriod>("today");
  const { data = [], isLoading } = useRegisterStats(period, storeId);

  const rows: StatsRow[] = data.flatMap((r) => [
    { key: r.registerId, label: `#${r.number} ${r.name}`, totals: r.totals, isOperator: false },
    ...r.operators.map((o) => ({
      key: `${r.registerId}-${o.cashierId ?? "none"}`,
      label: o.cashierName ?? t("unknownOperator"),
      totals: o.totals,
      isOperator: true,
    })),
  ]);

  const columns: TableColumn<StatsRow>[] = [
    {
      key: "register",
      header: t("columns.register"),
      cellStyle: { color: "#E8EDF5", fontWeight: 600 },
      render: (r) => (r.isOperator ? <span style={{ paddingLeft: 18, fontSize: 12, fontWeight: 400, color: "#9CA3AF" }}>{r.label}</span> : r.label),
    },
    { key: "turnover", header: t("columns.turnover"), cellStyle: { color: "#E8EDF5", fontWeight: 600 }, render: (r) => formatMoney(r.totals.turnover) },
    { key: "receipts", header: t("columns.receipts"), render: (r) => r.totals.receipts },
    { key: "average", header: t("columns.average"), render: (r) => formatMoney(r.totals.averageReceipt) },
    { key: "cash", header: t("columns.cash"), render: (r) => formatMoney(r.totals.cash) },
    { key: "card", header: t("columns.card"), render: (r) => formatMoney(r.totals.card) },
    { key: "mixed", header: t("columns.mixed"), render: (r) => formatMoney(r.totals.mixed) },
  ];

  return (
    <div>
      <div style={{ display: "flex", gap: 8, marginBottom: 14 }}>
        {PERIODS.map((p) => (
          <Btn key={p} size="sm" variant={period === p ? "primary" : "ghost"} onClick={() => setPeriod(p)}>
            {t(`period.${p}`)}
          </Btn>
        ))}
      </div>

      <Table
        columns={columns}
        rows={rows}
        rowKey={(r) => r.key}
        rowStyle={(r) => (r.isOperator ? { background: "#0A0F1A" } : {})}
        isLoading={isLoading}
        minWidth={820}
        emptyMessage={isLoading ? t("loading") : t("empty")}
      />
      <p style={{ color: "#6B7280", fontSize: 12, marginTop: 10 }}>{t("note")}</p>
    </div>
  );
}
