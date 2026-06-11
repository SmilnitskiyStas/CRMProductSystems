"use client";

import { BufferFunnel } from "./BufferFunnel";
import type { OrderLine } from "../types";

const th: React.CSSProperties = {
  textAlign: "left",
  color: "#6B7280",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: 0.5,
  padding: "10px 12px",
  borderBottom: "1px solid #1F2937",
  whiteSpace: "nowrap",
};

const td: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 13,
  padding: "10px 12px",
  borderBottom: "1px solid #161B22",
  verticalAlign: "middle",
};

const num: React.CSSProperties = { ...td, textAlign: "right", fontFamily: "monospace" };

const roundingLabels: Record<OrderLine["rounding"], string> = {
  none: "",
  moq_floor: "MOQ",
  usq_rounded: "USQ",
};

export function OrderLinesTable({ lines }: { lines: OrderLine[] }) {
  if (lines.length === 0) {
    return (
      <div style={{ color: "#4B5563", fontSize: 14, padding: "48px 0", textAlign: "center" }}>
        Немає товарів з розрахованим буфером. Спочатку внесіть продажі та натисніть «Сформувати замовлення».
      </div>
    );
  }

  return (
    <table style={{ width: "100%", borderCollapse: "collapse" }}>
      <thead>
        <tr>
          <th style={th}>Товар</th>
          <th style={th}>Буфер (воронка)</th>
          <th style={{ ...th, textAlign: "right" }}>Залишок</th>
          <th style={{ ...th, textAlign: "right" }}>В дорозі</th>
          <th style={{ ...th, textAlign: "right" }}>ББ</th>
          <th style={{ ...th, textAlign: "right" }}>Розрахунок</th>
          <th style={{ ...th, textAlign: "right" }}>Замовити</th>
        </tr>
      </thead>
      <tbody>
        {lines.map((l) => {
          const ordering = l.quantityToOrder > 0;
          return (
            <tr key={l.productId} style={ordering ? undefined : { opacity: 0.45 }}>
              <td style={td}>
                <div>{l.productName}</div>
                {l.barcode && (
                  <div style={{ color: "#4B5563", fontSize: 11, fontFamily: "monospace" }}>{l.barcode}</div>
                )}
              </td>
              <td style={td}><BufferFunnel line={l} /></td>
              <td style={num}>{l.stockOnHand}</td>
              <td style={num}>{l.inTransit > 0 ? l.inTransit : "—"}</td>
              <td style={{ ...num, color: "#9CA3AF" }}>{l.safetyBuffer}</td>
              <td style={{ ...num, color: "#9CA3AF" }}>{l.quantityRaw}</td>
              <td style={{ ...num }}>
                {ordering ? (
                  <span style={{
                    background: "#1D3461", border: "1px solid #3B82F6", color: "#93C5FD",
                    borderRadius: 8, padding: "4px 12px", fontWeight: 700, whiteSpace: "nowrap",
                  }}>
                    {l.quantityToOrder}
                    {l.rounding !== "none" && (
                      <span style={{ color: "#3B82F6", fontSize: 10, marginLeft: 6 }}>
                        {roundingLabels[l.rounding]}
                      </span>
                    )}
                  </span>
                ) : (
                  <span style={{ color: "#34D399", fontSize: 12 }}>покрито</span>
                )}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
