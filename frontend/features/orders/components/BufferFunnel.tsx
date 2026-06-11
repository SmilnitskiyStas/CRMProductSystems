"use client";

import type { OrderLine } from "../types";

/**
 * Horizontal CDA buffer funnel: red | yellow | green zones with a marker
 * showing current position (stock on hand + in transit) against the target.
 */
export function BufferFunnel({ line }: { line: OrderLine }) {
  const total = line.bufferTotal;
  if (total <= 0) return <span style={{ color: "#4B5563", fontSize: 12 }}>—</span>;

  const pct = (v: number) => Math.max(0, Math.min(100, (v / total) * 100));
  const position = line.stockOnHand + line.inTransit;
  const posPct = pct(position);

  // Zone order on the bar: red (bottom of buffer) → yellow → green (top)
  const redW = pct(line.bufferRed);
  const yellowW = pct(line.bufferYellow);
  const greenW = Math.max(0, 100 - redW - yellowW);

  const positionColor =
    position <= line.bufferRed ? "#F87171"
    : position <= line.bufferRed + line.bufferYellow ? "#FBBF24"
    : "#34D399";

  return (
    <div title={`Позиція ${position} із ${total} (🔴 ${line.bufferRed} · 🟡 ${line.bufferYellow} · 🟢 ${line.bufferGreen})`}
      style={{ minWidth: 140 }}>
      <div style={{
        position: "relative", height: 10, borderRadius: 5, overflow: "hidden",
        display: "flex", background: "#111827",
      }}>
        <div style={{ width: `${redW}%`, background: "#7F1D1D" }} />
        <div style={{ width: `${yellowW}%`, background: "#78350F" }} />
        <div style={{ width: `${greenW}%`, background: "#14532D" }} />
        {/* current position marker */}
        <div style={{
          position: "absolute", left: `calc(${posPct}% - 2px)`, top: 0, bottom: 0,
          width: 3, background: positionColor, borderRadius: 2,
        }} />
      </div>
      <div style={{ display: "flex", justifyContent: "space-between", marginTop: 3 }}>
        <span style={{ color: positionColor, fontSize: 10, fontFamily: "monospace" }}>
          {position}
        </span>
        <span style={{ color: "#4B5563", fontSize: 10, fontFamily: "monospace" }}>
          {total}
        </span>
      </div>
    </div>
  );
}
