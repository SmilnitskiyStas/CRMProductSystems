"use client";

import type { PosAnalyticsSummaryDto } from "../types";

interface Props {
  data: PosAnalyticsSummaryDto;
}

function KpiCard({
  label,
  value,
  sub,
  color,
}: {
  label: string;
  value: string | number;
  sub?: string;
  color?: string;
}) {
  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "20px 24px",
        display: "flex",
        flexDirection: "column",
        gap: 6,
      }}
    >
      <div style={{ color: "#4B5563", fontSize: 12, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.05em" }}>
        {label}
      </div>
      <div style={{ color: color ?? "#E8EDF5", fontSize: 26, fontWeight: 700, fontFamily: "monospace", lineHeight: 1.1 }}>
        {value}
      </div>
      {sub && (
        <div style={{ color: "#4B5563", fontSize: 11 }}>{sub}</div>
      )}
    </div>
  );
}

export function PosSummaryCards({ data }: Props) {
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
        gap: 12,
      }}
    >
      <KpiCard
        label="Виручка"
        value={`${data.totalRevenue.toLocaleString("uk-UA")} ₴`}
        color="#4ADE80"
        sub={`Готівка: ${data.cashRevenue.toLocaleString("uk-UA")} ₴ · Картка: ${data.cardRevenue.toLocaleString("uk-UA")} ₴`}
      />
      <KpiCard
        label="Транзакції"
        value={data.transactionCount.toLocaleString("uk-UA")}
        color="#60A5FA"
      />
      <KpiCard
        label="Середній чек"
        value={`${data.averageTicket.toLocaleString("uk-UA", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₴`}
        color="#FBBF24"
      />
      <KpiCard
        label="Зміни"
        value={data.shiftCount.toLocaleString("uk-UA")}
        color="#A78BFA"
      />
    </div>
  );
}
