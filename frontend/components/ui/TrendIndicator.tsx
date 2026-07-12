"use client";

import { TrendingUp, TrendingDown, Minus } from "lucide-react";

export type TrendFormat = "number" | "currency" | "percent";
export type TrendSize = "sm" | "md";

interface TrendIndicatorProps {
  /** Value for the current period. */
  current: number;
  /** Value for the previous period. `null` (or `0`) → no baseline, render neutral. */
  previous: number | null;
  /** How to format the raw values shown in the hover tooltip. */
  format?: TrendFormat;
  size?: TrendSize;
  className?: string;
}

function formatValue(value: number, format: TrendFormat): string {
  switch (format) {
    case "currency":
      return `${value.toLocaleString("uk-UA", { maximumFractionDigits: 2 })} ₴`;
    case "percent":
      return `${value.toLocaleString("uk-UA", { maximumFractionDigits: 1 })}%`;
    default:
      return value.toLocaleString("uk-UA");
  }
}

/**
 * Self-contained period-over-period trend badge: computes % change from
 * `current`/`previous` and renders a colored arrow (green up / red down) or
 * a neutral dash when there's no comparable baseline.
 */
export function TrendIndicator({ current, previous, format = "number", size = "md", className }: TrendIndicatorProps) {
  const neutral = previous === null || previous === 0;
  const percentChange = neutral ? null : ((current - previous) / previous) * 100;
  const isUp = percentChange !== null && percentChange > 0;
  const isDown = percentChange !== null && percentChange < 0;

  const fontSize = size === "sm" ? 11 : 12;
  const iconSize = size === "sm" ? 12 : 14;

  const color = isUp ? "#4ADE80" : isDown ? "#F87171" : "#6B7280";
  const Icon = isUp ? TrendingUp : isDown ? TrendingDown : Minus;

  const title = neutral
    ? "Немає даних за попередній період"
    : `Поточне: ${formatValue(current, format)} · Попереднє: ${formatValue(previous as number, format)}`;

  return (
    <span
      title={title}
      className={className}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 3,
        fontSize,
        fontWeight: 600,
        color,
        fontFamily: "monospace",
        whiteSpace: "nowrap",
      }}
    >
      <Icon size={iconSize} strokeWidth={2.5} />
      {neutral ? "—" : `${percentChange! > 0 ? "+" : ""}${percentChange!.toFixed(1)}%`}
    </span>
  );
}
