"use client";

import { useRouter } from "next/navigation";
import { BarChart2 } from "lucide-react";
import { useTranslations } from "next-intl";
import { BufferFunnel } from "./BufferFunnel";
import type { OrderLine } from "../types";
import { ActionMenu } from "@/components/ui/ActionMenu";
import { Table, type TableColumn } from "@/components/ui/Table";

const roundingLabels: Record<OrderLine["rounding"], string> = {
  none: "",
  moq_floor: "MOQ",
  usq_rounded: "USQ",
};

export function OrderLinesTable({ lines }: { lines: OrderLine[] }) {
  const router = useRouter();
  const t = useTranslations("Dashboard.orders.table");

  const columns: TableColumn<OrderLine>[] = [
    {
      key: "product",
      header: t("headers.product"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (l) => (
        <div style={{ minWidth: 0 }}>
          <div>{l.productName}</div>
          {l.barcode && (
            <div style={{ color: "#4B5563", fontSize: 11, fontFamily: "monospace" }}>{l.barcode}</div>
          )}
        </div>
      ),
    },
    {
      key: "buffer",
      header: t("headers.buffer"),
      render: (l) => <BufferFunnel line={l} />,
    },
    {
      key: "stock",
      header: t("headers.stock"),
      cellStyle: { fontFamily: "monospace" },
      render: (l) => l.stockOnHand,
    },
    {
      key: "inTransit",
      header: t("headers.inTransit"),
      cellStyle: { fontFamily: "monospace" },
      // inTransit now combines draft supplier receipts + open B2B marketplace orders (Phase 4,
      // plan D5). When any of it comes from the marketplace, expose the split on hover; for
      // tenants not using the marketplace (inTransitFromMarketplace === 0) the cell is unchanged.
      render: (l) => {
        if (l.inTransit <= 0) return "—";
        if (l.inTransitFromMarketplace <= 0) return l.inTransit;
        const title = [
          t("inTransitTooltip.supplierReceipts", {
            qty: l.inTransit - l.inTransitFromMarketplace,
          }),
          t("inTransitTooltip.marketplaceOrders", { qty: l.inTransitFromMarketplace }),
        ].join("\n");
        return (
          <span title={title} style={{ cursor: "help", borderBottom: "1px dotted #4B5563" }}>
            {l.inTransit}
          </span>
        );
      },
    },
    {
      key: "safetyBuffer",
      header: t("headers.safetyBuffer"),
      cellStyle: { fontFamily: "monospace" },
      render: (l) => l.safetyBuffer,
    },
    {
      key: "calculation",
      header: t("headers.calculation"),
      cellStyle: { fontFamily: "monospace" },
      render: (l) => l.quantityRaw,
    },
    {
      key: "order",
      header: t("headers.order"),
      render: (l) =>
        l.quantityToOrder > 0 ? (
          <span
            style={{
              background: "#1D3461",
              border: "1px solid #3B82F6",
              color: "#93C5FD",
              borderRadius: 8,
              padding: "4px 12px",
              fontWeight: 700,
              whiteSpace: "nowrap",
            }}
          >
            {l.quantityToOrder}
            {l.rounding !== "none" && (
              <span style={{ color: "#3B82F6", fontSize: 10, marginLeft: 6 }}>
                {roundingLabels[l.rounding]}
              </span>
            )}
          </span>
        ) : (
          <span style={{ color: "#34D399", fontSize: 12 }}>{t("covered")}</span>
        ),
    },
    {
      key: "actions",
      header: t("headers.actions"),
      render: (l) => (
        <ActionMenu
          items={[
            {
              label: t("actionMenu.productAnalytics"),
              icon: <BarChart2 size={13} />,
              onClick: () => router.push(`/inventory/${l.productId}?tab=analytics`),
            },
          ]}
        />
      ),
    },
  ];

  return (
    <Table
      columns={columns}
      rows={lines}
      rowKey={(l) => l.productId}
      rowStyle={(l) => (l.quantityToOrder > 0 ? {} : { opacity: 0.45 })}
      emptyMessage={t("empty")}
    />
  );
}
