"use client";

import { TrendIndicator } from "@/components/ui/TrendIndicator";
import type { PosAnalyticsSummaryDto } from "../types";

interface Props {
  data: PosAnalyticsSummaryDto;
  /** Comparison period totals (ADR-016) — omit/undefined when compare mode is off. */
  previous?: PosAnalyticsSummaryDto;
}

function KpiCard({
  label,
  value,
  sub,
  color,
  current,
  previous,
  format,
}: {
  label: string;
  value: string | number;
  sub?: string;
  color?: string;
  current?: number;
  previous?: number | null;
  format?: "number" | "currency" | "percent";
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
      {current !== undefined && (
        <TrendIndicator current={current} previous={previous ?? null} format={format} size="sm" />
      )}
      {sub && (
        <div style={{ color: "#4B5563", fontSize: 11 }}>{sub}</div>
      )}
    </div>
  );
}

export function PosSummaryCards({ data, previous }: Props) {
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
        current={data.totalRevenue}
        previous={previous?.totalRevenue}
        format="currency"
      />
      <KpiCard
        label="Транзакції"
        value={data.transactionCount.toLocaleString("uk-UA")}
        color="#60A5FA"
        current={data.transactionCount}
        previous={previous?.transactionCount}
        format="number"
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
