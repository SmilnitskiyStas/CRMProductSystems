"use client";

import Link from "next/link";
import { useTranslations, useLocale } from "next-intl";
import { Table, type TableColumn } from "@/components/ui/Table";
import type { PromoNoSalesDto, PromoNoSalesItemDto } from "../types";

interface Props {
  data: PromoNoSalesDto;
}

/**
 * "Promo items without sales" banner for /analytics/pos: a promo item the store holds in stock but
 * that does not sell is most likely not on the shelf (or misplaced). Renders nothing when the list
 * is empty, so it only shows up when there is something to check. Each row deep-links to the
 * item's shelf zone (planogram zone, else the zone holding its stock) where the AI photo check
 * lives; items without a known zone link to the store floor plan.
 */
export function PromoNoSalesBanner({ data }: Props) {
  const t = useTranslations("Dashboard.analytics.pos.promoNoSales");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (!data || data.items.length === 0) return null;

  const num = (n: number) => n.toLocaleString(intlLocale, { maximumFractionDigits: 1 });

  const columns: TableColumn<PromoNoSalesItemDto>[] = [
    {
      key: "product",
      align: "left",
      header: t("headers.product"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (item) => (
        <div>
          <div>{item.productName}</div>
          <div style={{ color: "#FBBF24", fontSize: 11, fontWeight: 400, marginTop: 2 }}>
            {t(`hint.${item.reason}`)}
          </div>
        </div>
      ),
    },
    {
      key: "store",
      align: "left",
      header: t("headers.store"),
      render: (item) => item.storeName,
    },
    {
      key: "promo",
      align: "left",
      header: t("headers.promo"),
      render: (item) =>
        item.discountPercent != null ? `${item.promoName} (−${num(item.discountPercent)}%)` : item.promoName,
    },
    {
      key: "sold",
      header: t("headers.sold"),
      cellStyle: { fontFamily: "monospace" },
      render: (item) => (
        <span>
          <span style={{ color: item.unitsSold === 0 ? "#F87171" : "#FBBF24" }}>{num(item.unitsSold)}</span>
          {item.expectedUnits != null ? ` / ${num(item.expectedUnits)}` : ""}{" "}
          <span style={{ color: "#4B5563" }}>{t("days", { days: item.periodDays })}</span>
        </span>
      ),
    },
    {
      key: "stock",
      header: t("headers.stock"),
      cellStyle: { fontFamily: "monospace" },
      render: (item) => `${num(item.onShelfStock)} / ${num(item.storeStock)} / ${num(item.warehouseStock)}`,
    },
    {
      key: "action",
      header: t("headers.action"),
      render: (item) => {
        const href = item.zoneId
          ? `/locations/${item.storeId}/zones/${item.zoneId}/shelves`
          : `/locations/${item.storeId}/floor-plan`;
        return (
          <Link href={href} style={{ color: "#93C5FD", fontSize: 12, whiteSpace: "nowrap" }}>
            {item.zoneId ? t("revise") : t("reviseFloorPlan")}
          </Link>
        );
      },
    },
  ];

  return (
    <div style={{ background: "#0D1117", border: "1px solid #78350F", borderRadius: 10, overflow: "hidden" }}>
      <div style={{ padding: "16px 16px 12px", borderBottom: "1px solid #1F2937" }}>
        <div style={{ color: "#FBBF24", fontSize: 14, fontWeight: 600 }}>
          ⚠ {t("title")} ({data.items.length})
        </div>
        <div style={{ color: "#6B7280", fontSize: 12, marginTop: 4 }}>{t("subtitle")}</div>
      </div>
      <Table
        columns={columns}
        rows={data.items}
        rowKey={(item) => `${item.storeId}:${item.productId}:${item.promoSourceId}`}
      />
    </div>
  );
}
