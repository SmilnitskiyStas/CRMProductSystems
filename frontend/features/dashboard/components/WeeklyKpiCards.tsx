"use client";

import { TrendIndicator, type TrendFormat } from "@/components/ui/TrendIndicator";
import type { PeriodMetric, WeeklyKpiDto } from "../types";

interface Props {
  data: WeeklyKpiDto | undefined;
  isLoading: boolean;
}

interface Card {
  key: keyof WeeklyKpiDto;
  label: string;
  color: string;
  format: TrendFormat;
  render: (v: number) => string;
}

const CARDS: Card[] = [
  {
    key: "sales",
    label: "Продажі",
    color: "#60A5FA",
    format: "number",
    render: (v) => v.toLocaleString("uk-UA"),
  },
  {
    key: "revenue",
    label: "Виручка",
    color: "#4ADE80",
    format: "currency",
    render: (v) => `${v.toLocaleString("uk-UA", { maximumFractionDigits: 0 })} ₴`,
  },
  {
    key: "writeOffLoss",
    label: "Списання",
    color: "#F87171",
    format: "currency",
    render: (v) => `${v.toLocaleString("uk-UA", { maximumFractionDigits: 0 })} ₴`,
  },
];

export function WeeklyKpiCards({ data, isLoading }: Props) {
  return (
    <div>
      <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0, marginBottom: 12 }}>
        За тиждень
      </h2>
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        {CARDS.map((card) => {
          const metric: PeriodMetric | undefined = data?.[card.key];
          return (
            <div
              key={card.key}
              style={{
                background: "#0D1117",
                border: "1px solid #1F2937",
                borderRadius: 12,
                padding: "20px 24px",
                display: "flex",
                flexDirection: "column",
                gap: 6,
              }}
            >
              <span
                style={{
                  color: "#8A94A8",
                  fontSize: 12,
                  fontWeight: 500,
                  textTransform: "uppercase",
                  letterSpacing: "0.06em",
                }}
              >
                {card.label}
              </span>
              <span style={{ color: card.color, fontSize: 28, fontWeight: 700, fontFamily: "monospace" }}>
                {isLoading || !metric ? (
                  <span style={{ color: "#374151", fontSize: 20 }}>—</span>
                ) : (
                  card.render(metric.current)
                )}
              </span>
              {!isLoading && metric && (
                <TrendIndicator current={metric.current} previous={metric.previous} format={card.format} size="sm" />
              )}
              <span style={{ color: "#4B5563", fontSize: 11 }}>останні 7 днів vs попередні 7 днів</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}
