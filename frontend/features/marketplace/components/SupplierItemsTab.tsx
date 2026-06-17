"use client";

import { useSupplierItems } from "../hooks/useMarketplace";

interface Props {
  supplierId: string;
}

export function SupplierItemsTab({ supplierId }: Props) {
  const { data, isLoading, isError } = useSupplierItems(supplierId);

  if (isLoading) {
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {[...Array(5)].map((_, i) => (
          <div
            key={i}
            style={{
              height: 44,
              background: "#111827",
              borderRadius: 8,
              animation: "pulse 1.5s infinite",
            }}
          />
        ))}
      </div>
    );
  }

  if (isError) {
    return (
      <div style={{ color: "#F87171", fontSize: 13, padding: "16px 0" }}>
        Не вдалося завантажити каталог постачальника.
      </div>
    );
  }

  if (!data || data.length === 0) {
    return (
      <div
        style={{
          textAlign: "center",
          padding: "40px 0",
          color: "#4B5563",
          fontSize: 14,
        }}
      >
        Каталог порожній — постачальник ще не додав товари.
      </div>
    );
  }

  const headerCellStyle: React.CSSProperties = {
    padding: "10px 14px",
    color: "#4B5563",
    fontSize: 11,
    fontWeight: 600,
    textTransform: "uppercase",
    letterSpacing: "0.05em",
    textAlign: "left",
    borderBottom: "1px solid #1F2937",
  };

  const cellStyle: React.CSSProperties = {
    padding: "12px 14px",
    color: "#E8EDF5",
    fontSize: 13,
    borderBottom: "1px solid #1A2235",
  };

  return (
    <div style={{ overflowX: "auto" }}>
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={headerCellStyle}>Назва</th>
            <th style={{ ...headerCellStyle, textAlign: "right" }}>Ціна</th>
            <th style={{ ...headerCellStyle, textAlign: "right" }}>Мін. замовл.</th>
            <th style={headerCellStyle}>Од. вим.</th>
            <th style={{ ...headerCellStyle, textAlign: "center" }}>Наявність</th>
          </tr>
        </thead>
        <tbody>
          {data.map((item) => (
            <tr key={item.id}>
              <td style={cellStyle}>{item.customName}</td>
              <td style={{ ...cellStyle, textAlign: "right" }}>
                {item.price.toLocaleString("uk-UA", {
                  style: "currency",
                  currency: "UAH",
                  minimumFractionDigits: 2,
                })}
              </td>
              <td style={{ ...cellStyle, textAlign: "right" }}>{item.minQty}</td>
              <td style={{ ...cellStyle, color: "#9CA3AF" }}>{item.unit}</td>
              <td style={{ ...cellStyle, textAlign: "center" }}>
                <span
                  style={{
                    display: "inline-block",
                    padding: "2px 8px",
                    borderRadius: 4,
                    fontSize: 11,
                    fontWeight: 600,
                    background: item.isAvailable ? "#052e16" : "#1c1917",
                    color: item.isAvailable ? "#4ADE80" : "#6B7280",
                  }}
                >
                  {item.isAvailable ? "В наявності" : "Відсутній"}
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
