"use client";

import { useState } from "react";
import { ImageOff, Barcode } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useSupplierItems } from "../hooks/useMarketplace";
import { SupplierItemDetailDialog } from "./SupplierItemDetailDialog";
import type { SupplierItemDto } from "../types";

interface Props {
  supplierId: string;
  /** Коли задано (активна угода про співпрацю) — рядки отримують поле
   * кількості та кнопку «Додати» до кошика (TASK-318). */
  onAddToCart?: (item: SupplierItemDto, qty: number) => void;
}

/** Qty input + add-to-cart button — per-row local UI state (respects minQty/maxQty). */
function AddToCartCell({
  item,
  addLabel,
  onAdd,
}: {
  item: SupplierItemDto;
  addLabel: string;
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
        {addLabel}
      </button>
    </span>
  );
}

export function SupplierItemsTab({ supplierId, onAddToCart }: Props) {
  const t = useTranslations("Dashboard.marketplace.itemsTab");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
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
        {t("errorLoad")}
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
        {t("emptyCatalog")}
      </div>
    );
  }

  function moqRange(item: SupplierItemDto): string {
    if (item.minQty == null && item.maxQty == null) return "—";
    if (item.maxQty != null) return `${item.minQty ?? 1}–${item.maxQty}`;
    return `${item.minQty ?? 1}+`;
  }

  const columns: TableColumn<SupplierItemDto>[] = [
    {
      key: "image",
      header: "",
      width: 40,
      render: (item) => {
        const mainImage = item.images.find((i) => i.kind === "main") ?? item.images[0];
        return (
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
        );
      },
    },
    {
      // Displaced from column 0 by the leading image thumbnail — kept left-aligned
      // as the row's real label column (same exception StockTable.tsx documents
      // for its checkbox+name pair).
      key: "name",
      header: t("headerName"),
      align: "left",
      render: (item) => item.customName ?? item.itemName ?? "—",
    },
    {
      key: "price",
      header: t("headerPrice"),
      render: (item) =>
        item.price != null
          ? item.price.toLocaleString(intlLocale, {
              style: "currency",
              currency: "UAH",
              minimumFractionDigits: 2,
            })
          : "—",
    },
    {
      key: "moq",
      header: t("headerMoq"),
      render: (item) => moqRange(item),
    },
    {
      key: "unit",
      header: t("headerUnit"),
      cellStyle: { color: "#9CA3AF" },
      render: (item) => item.unit ?? "—",
    },
    {
      key: "barcodes",
      header: t("headerBarcodes"),
      cellStyle: { color: "#9CA3AF" },
      render: (item) =>
        item.barcodes.length > 0 ? (
          <span style={{ display: "inline-flex", alignItems: "center", gap: 4 }}>
            <Barcode size={13} /> {item.barcodes.length}
          </span>
        ) : (
          "—"
        ),
    },
    {
      key: "availability",
      header: t("headerAvailability"),
      render: (item) => (
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
          {item.isAvailable ? t("available") : t("unavailable")}
        </span>
      ),
    },
    {
      key: "actions",
      header: "",
      cellStyle: { whiteSpace: "nowrap" },
      render: (item) => (
        <>
          {onAddToCart && (
            <span style={{ marginRight: 8 }}>
              <AddToCartCell item={item} addLabel={t("addButton")} onAdd={onAddToCart} />
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
            {t("detailsButton")}
          </button>
        </>
      ),
    },
  ];

  return (
    <div>
      <Table columns={columns} rows={data} rowKey={(item) => item.id} />

      <SupplierItemDetailDialog item={detailItem} onClose={() => setDetailItem(null)} />
    </div>
  );
}
