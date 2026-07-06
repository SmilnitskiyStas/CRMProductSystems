"use client";

import { useState } from "react";
import { ImageOff, Barcode } from "lucide-react";
import { useSupplierItems } from "../hooks/useMarketplace";
import { SupplierItemDetailDialog } from "./SupplierItemDetailDialog";
import type { SupplierItemDto } from "../types";

interface Props {
  supplierId: string;
  /** Коли задано (активна угода про співпрацю) — рядки отримують поле
   * кількості та кнопку «Додати» до кошика (TASK-318). */
  onAddToCart?: (item: SupplierItemDto, qty: number) => void;
}

/** Qty input + «Додати» — per-row local UI state (respects minQty/maxQty). */
function AddToCartCell({
  item,
  onAdd,
}: {
  item: SupplierItemDto;
  onAdd: (item: SupplierItemDto, qty: number) => void;
}) {
  const [qty, setQty] = useState(item.minQty ?? 1);

  function clamp(v: number): number {
    let q = Math.max(1, Math.round(v));
    if (item.minQty != null && q < item.minQty) q = item.minQty;
    if (item.maxQty != null && q > item.maxQty) q = item.maxQty;
    return q;
  }

  if (!item.isAvailable) return null;

  return (
    <span style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
      <input
        type="number"
        value={qty}
        min={item.minQty ?? 1}
        max={item.maxQty ?? undefined}
        onChange={(e) => setQty(Number(e.target.value) || 1)}
        onBlur={() => setQty((q) => clamp(q))}
        style={{
          width: 62,
          background: "#1F2937",
          border: "1px solid #374151",
          borderRadius: 6,
          color: "#E8EDF5",
          fontSize: 12,
          padding: "4px 8px",
          outline: "none",
          textAlign: "right",
        }}
      />
      <button
        onClick={() => onAdd(item, clamp(qty))}
        style={{
          background: "#1D3461",
          border: "1px solid #3B82F6",
          borderRadius: 7,
          color: "#93C5FD",
          fontSize: 12,
          fontWeight: 600,
          padding: "4px 10px",
          cursor: "pointer",
          whiteSpace: "nowrap",
        }}
      >
        Додати
      </button>
    </span>
  );
}

export function SupplierItemsTab({ supplierId, onAddToCart }: Props) {
  const { data, isLoading, isError } = useSupplierItems(supplierId);
  const [detailItem, setDetailItem] = useState<SupplierItemDto | null>(null);

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

  function moqRange(item: SupplierItemDto): string {
    if (item.minQty == null && item.maxQty == null) return "—";
    if (item.maxQty != null) return `${item.minQty ?? 1}–${item.maxQty}`;
    return `${item.minQty ?? 1}+`;
  }

  return (
    <div style={{ overflowX: "auto" }}>
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={headerCellStyle}></th>
            <th style={headerCellStyle}>Назва</th>
            <th style={{ ...headerCellStyle, textAlign: "right" }}>Ціна</th>
            <th style={{ ...headerCellStyle, textAlign: "right" }}>Мін./Макс.</th>
            <th style={headerCellStyle}>Од. вим.</th>
            <th style={{ ...headerCellStyle, textAlign: "center" }}>Штрихкоди</th>
            <th style={{ ...headerCellStyle, textAlign: "center" }}>Наявність</th>
            <th style={headerCellStyle}></th>
          </tr>
        </thead>
        <tbody>
          {data.map((item) => {
            const mainImage = item.images.find((i) => i.kind === "main") ?? item.images[0];
            return (
              <tr key={item.id}>
                <td style={{ ...cellStyle, width: 40 }}>
                  <div
                    style={{
                      width: 32,
                      height: 32,
                      borderRadius: 6,
                      background: "#111827",
                      border: "1px solid #1F2937",
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "center",
                      overflow: "hidden",
                    }}
                  >
                    {mainImage ? (
                      // eslint-disable-next-line @next/next/no-img-element
                      <img
                        src={mainImage.url}
                        alt=""
                        style={{ width: "100%", height: "100%", objectFit: "cover" }}
                      />
                    ) : (
                      <ImageOff size={14} color="#4B5563" />
                    )}
                  </div>
                </td>
                <td style={cellStyle}>{item.customName ?? item.itemName ?? "—"}</td>
                <td style={{ ...cellStyle, textAlign: "right" }}>
                  {item.price != null
                    ? item.price.toLocaleString("uk-UA", {
                        style: "currency",
                        currency: "UAH",
                        minimumFractionDigits: 2,
                      })
                    : "—"}
                </td>
                <td style={{ ...cellStyle, textAlign: "right" }}>{moqRange(item)}</td>
                <td style={{ ...cellStyle, color: "#9CA3AF" }}>{item.unit ?? "—"}</td>
                <td style={{ ...cellStyle, textAlign: "center", color: "#9CA3AF" }}>
                  {item.barcodes.length > 0 ? (
                    <span style={{ display: "inline-flex", alignItems: "center", gap: 4 }}>
                      <Barcode size={13} /> {item.barcodes.length}
                    </span>
                  ) : (
                    "—"
                  )}
                </td>
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
                <td style={{ ...cellStyle, textAlign: "right", whiteSpace: "nowrap" }}>
                  {onAddToCart && (
                    <span style={{ marginRight: 8 }}>
                      <AddToCartCell item={item} onAdd={onAddToCart} />
                    </span>
                  )}
                  <button
                    onClick={() => setDetailItem(item)}
                    style={{
                      background: "transparent",
                      border: "1px solid #374151",
                      borderRadius: 7,
                      color: "#9CA3AF",
                      fontSize: 12,
                      fontWeight: 600,
                      padding: "4px 10px",
                      cursor: "pointer",
                    }}
                  >
                    Детальніше
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      <SupplierItemDetailDialog item={detailItem} onClose={() => setDetailItem(null)} />
    </div>
  );
}
