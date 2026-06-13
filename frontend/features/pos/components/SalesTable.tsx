"use client";

import { FiscalBadge } from "./FiscalBadge";
import type { SaleDto } from "../types";

interface Props {
  sales: SaleDto[] | undefined;
  totalAmount: number;
  isLoading: boolean;
  onSelectSale: (sale: SaleDto) => void;
}

const th: React.CSSProperties = {
  textAlign: "left",
  color: "#6B7280",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: 0.5,
  padding: "10px 14px",
  borderBottom: "1px solid #1F2937",
  whiteSpace: "nowrap",
};

const td: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 13,
  padding: "12px 14px",
  borderBottom: "1px solid #161B26",
  verticalAlign: "middle",
};

const PAYMENT_LABEL: Record<string, string> = {
  Cash: "Готівка",
  Card: "Картка",
};

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString("uk-UA", { hour: "2-digit", minute: "2-digit" });
}

export function SalesTable({ sales, totalAmount, isLoading, onSelectSale }: Props) {
  if (isLoading) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: 32 }}>
        Завантаження…
      </div>
    );
  }

  if (!sales?.length) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: 40 }}>
        Продажів у цій зміні ще немає
      </div>
    );
  }

  return (
    <div>
      <div
        style={{
          background: "#161B26",
          border: "1px solid #1F2937",
          borderRadius: 12,
          overflow: "auto",
        }}
      >
        <table style={{ width: "100%", borderCollapse: "collapse" }}>
          <thead>
            <tr>
              <th style={th}>Чек №</th>
              <th style={th}>Час</th>
              <th style={th}>Товарів</th>
              <th style={th}>Оплата</th>
              <th style={{ ...th, textAlign: "right" }}>Сума</th>
              <th style={th}>Фіскалізація</th>
            </tr>
          </thead>
          <tbody>
            {sales.map((sale) => (
              <tr
                key={sale.transactionId}
                onClick={() => onSelectSale(sale)}
                style={{ cursor: "pointer" }}
                onMouseEnter={(e) => {
                  (e.currentTarget as HTMLElement).style.background = "#1a2035";
                }}
                onMouseLeave={(e) => {
                  (e.currentTarget as HTMLElement).style.background = "transparent";
                }}
              >
                <td style={td}>
                  <span style={{ fontFamily: "monospace", fontSize: 12, color: "#93C5FD" }}>
                    #{sale.receiptNumber}
                  </span>
                </td>
                <td style={{ ...td, color: "#9CA3AF" }}>{formatTime(sale.createdAt)}</td>
                <td style={td}>{sale.items.length}</td>
                <td style={{ ...td, color: "#9CA3AF" }}>
                  {PAYMENT_LABEL[sale.paymentType] ?? sale.paymentType}
                </td>
                <td style={{ ...td, textAlign: "right", fontWeight: 700, color: "#34d399" }}>
                  {sale.subtotal.toFixed(2)} ₴
                </td>
                <td style={td}>
                  <FiscalBadge status={sale.fiscalStatus} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Footer total */}
      <div
        style={{
          display: "flex",
          justifyContent: "flex-end",
          marginTop: 12,
          color: "#9CA3AF",
          fontSize: 13,
        }}
      >
        Загальна сума:{" "}
        <span style={{ color: "#34d399", fontWeight: 700, marginLeft: 6 }}>
          {totalAmount.toFixed(2)} ₴
        </span>
      </div>
    </div>
  );
}
