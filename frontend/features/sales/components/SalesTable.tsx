"use client";

import type { DailySale } from "../types";
import { ProductAnalyticsLink } from "@/components/ui/ProductAnalyticsLink";

interface Props {
  sales: DailySale[];
  onToggleAnomaly: (id: string, isAnomaly: boolean) => void;
}

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
};

const sourceLabels: Record<string, string> = {
  manual: "вручну",
  pos: "каса",
  import: "імпорт",
};

export function SalesTable({ sales, onToggleAnomaly }: Props) {
  if (sales.length === 0) {
    return (
      <div style={{ color: "#4B5563", fontSize: 14, padding: "48px 0", textAlign: "center" }}>
        Немає даних про продажі за обраний період. Внесіть вручну або імпортуйте CSV.
      </div>
    );
  }

  return (
    <table style={{ width: "100%", borderCollapse: "collapse" }}>
      <thead>
        <tr>
          <th style={th}>Дата</th>
          <th style={th}>Товар</th>
          <th style={th}>Штрихкод</th>
          <th style={th}>Магазин</th>
          <th style={{ ...th, textAlign: "right" }}>Продано</th>
          <th style={{ ...th, textAlign: "right" }}>Залишок EOD</th>
          <th style={th}>Джерело</th>
          <th style={th}>Позначки</th>
          <th style={th}></th>
        </tr>
      </thead>
      <tbody>
        {sales.map((s) => (
          <tr key={s.id} style={s.isAnomaly ? { opacity: 0.45 } : undefined}>
            <td style={{ ...td, fontFamily: "monospace" }}>{s.date}</td>
            <td style={td}>{s.productName}</td>
            <td style={{ ...td, color: "#6B7280", fontFamily: "monospace", fontSize: 12 }}>
              {s.barcode ?? "—"}
            </td>
            <td style={{ ...td, color: "#9CA3AF" }}>{s.storeName}</td>
            <td style={{ ...td, textAlign: "right", fontWeight: 600 }}>{s.quantitySold}</td>
            <td style={{ ...td, textAlign: "right", color: "#9CA3AF" }}>
              {s.quantityEndOfDay ?? "—"}
            </td>
            <td style={{ ...td, color: "#6B7280", fontSize: 12 }}>
              {sourceLabels[s.source] ?? s.source}
            </td>
            <td style={td}>
              {s.isPromoDay && (
                <span style={{
                  background: "#7C2D12", color: "#FDBA74", fontSize: 11,
                  borderRadius: 6, padding: "2px 8px", marginRight: 6,
                }}>акція</span>
              )}
              {s.isAnomaly && (
                <span style={{
                  background: "#7F1D1D", color: "#FCA5A5", fontSize: 11,
                  borderRadius: 6, padding: "2px 8px",
                }}>аномалія</span>
              )}
            </td>
            <td style={{ ...td, textAlign: "right" }}>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "flex-end", gap: 6 }}>
              {s.productId && <ProductAnalyticsLink productId={s.productId} />}
              <button
                onClick={() => onToggleAnomaly(s.id, !s.isAnomaly)}
                title={s.isAnomaly
                  ? "Повернути до розрахунку ADU"
                  : "Виключити з розрахунку ADU (оптовий продаж, помилка даних)"}
                style={{
                  background: "transparent",
                  border: "1px solid #1F2937",
                  borderRadius: 6,
                  color: s.isAnomaly ? "#34D399" : "#6B7280",
                  fontSize: 12,
                  padding: "4px 10px",
                  cursor: "pointer",
                }}
              >
                {s.isAnomaly ? "Включити" : "Аномалія"}
              </button>
              </div>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
