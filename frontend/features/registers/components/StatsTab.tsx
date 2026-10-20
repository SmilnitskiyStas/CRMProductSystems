"use client";

import { Fragment, useState } from "react";
import { useTranslations } from "next-intl";
import { useRegisterStats } from "../hooks/useRegisters";
import type { SalesTotals, StatsPeriod } from "../types";
import { formatMoney } from "./shared";

const PERIODS: StatsPeriod[] = ["today", "7d", "30d"];

const th: React.CSSProperties = {
  padding: "10px 14px",
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
  textAlign: "right",
  borderBottom: "1px solid #374151",
  background: "#0A0F1A",
};
const td: React.CSSProperties = {
  padding: "10px 14px",
  color: "#9CA3AF",
  fontSize: 13,
  textAlign: "right",
  borderBottom: "1px solid #1F2937",
};

function Cells({ totals }: { totals: SalesTotals }) {
  return (
    <>
      <td style={{ ...td, color: "#E8EDF5", fontWeight: 600 }}>{formatMoney(totals.turnover)}</td>
      <td style={td}>{totals.receipts}</td>
      <td style={td}>{formatMoney(totals.averageReceipt)}</td>
      <td style={td}>{formatMoney(totals.cash)}</td>
      <td style={td}>{formatMoney(totals.card)}</td>
      <td style={td}>{formatMoney(totals.mixed)}</td>
    </>
  );
}

export function StatsTab({ storeId }: { storeId?: string }) {
  const t = useTranslations("Dashboard.registers.stats");
  const [period, setPeriod] = useState<StatsPeriod>("today");
  const { data = [], isLoading } = useRegisterStats(period, storeId);

  return (
    <div>
      <div style={{ display: "flex", gap: 8, marginBottom: 14 }}>
        {PERIODS.map((p) => (
          <button
            key={p}
            onClick={() => setPeriod(p)}
            style={{
              padding: "6px 14px",
              borderRadius: 8,
              fontSize: 12,
              fontWeight: 600,
              cursor: "pointer",
              background: period === p ? "#1D3461" : "transparent",
              border: `1px solid ${period === p ? "#3B82F6" : "#374151"}`,
              color: period === p ? "#93C5FD" : "#9CA3AF",
            }}
          >
            {t(`period.${p}`)}
          </button>
        ))}
      </div>

      <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12, overflow: "auto" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 820 }}>
          <thead>
            <tr>
              <th style={{ ...th, textAlign: "left" }}>{t("columns.register")}</th>
              <th style={th}>{t("columns.turnover")}</th>
              <th style={th}>{t("columns.receipts")}</th>
              <th style={th}>{t("columns.average")}</th>
              <th style={th}>{t("columns.cash")}</th>
              <th style={th}>{t("columns.card")}</th>
              <th style={th}>{t("columns.mixed")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={7} style={{ ...td, textAlign: "center" }}>{t("loading")}</td></tr>
            )}
            {!isLoading && data.length === 0 && (
              <tr><td colSpan={7} style={{ ...td, textAlign: "center" }}>{t("empty")}</td></tr>
            )}
            {data.map((r) => (
              <Fragment key={r.registerId}>
                <tr>
                  <td style={{ ...td, textAlign: "left", color: "#E8EDF5", fontWeight: 600 }}>
                    #{r.number} {r.name}
                  </td>
                  <Cells totals={r.totals} />
                </tr>
                {r.operators.map((o) => (
                  <tr key={`${r.registerId}-${o.cashierId ?? "none"}`} style={{ background: "#0A0F1A" }}>
                    <td style={{ ...td, textAlign: "left", paddingLeft: 32, fontSize: 12 }}>
                      {o.cashierName ?? t("unknownOperator")}
                    </td>
                    <Cells totals={o.totals} />
                  </tr>
                ))}
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>
      <p style={{ color: "#6B7280", fontSize: 12, marginTop: 10 }}>{t("note")}</p>
    </div>
  );
}
