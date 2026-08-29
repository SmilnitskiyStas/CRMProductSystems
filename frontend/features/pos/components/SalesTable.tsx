"use client";

import { useTranslations, useLocale } from "next-intl";
import { Gift } from "lucide-react";
import { FiscalBadge } from "./FiscalBadge";
import { saleHasLoyaltyActivity, type SaleDto } from "../types";
import { Table, type TableColumn } from "@/components/ui/Table";

interface Props {
  sales: SaleDto[] | undefined;
  totalAmount: number;
  isLoading: boolean;
  onSelectSale: (sale: SaleDto) => void;
}

function formatTime(iso: string, intlLocale: string): string {
  return new Date(iso).toLocaleTimeString(intlLocale, { hour: "2-digit", minute: "2-digit" });
}

export function SalesTable({ sales, totalAmount, isLoading, onSelectSale }: Props) {
  const t = useTranslations("Dashboard.pos.salesTable");
  const tCommon = useTranslations("Common");
  const tPayment = useTranslations("Dashboard.pos.paymentType");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const columns: TableColumn<SaleDto>[] = [
    {
      key: "receiptNo",
      header: t("headers.receiptNo"),
      render: (sale) => (
        <span style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
          <span style={{ fontFamily: "monospace", fontSize: 12, color: "#93C5FD" }}>
            #{sale.receiptNumber}
          </span>
          {/*
            TASK-408: proxy indicator for "has loyalty activity", not "has a linked
            customer" — SaleDto has no CustomerId field at all (see types.ts doc on
            saleHasLoyaltyActivity / SaleDetailDrawer.tsx for the full backend-gap
            writeup). Always hidden today: GetSalesForShiftAsync (the query behind
            this table's data) never populates loyaltyAccrued/Redeemed/Balance —
            only the mobile checkout's immediate creation response does.
          */}
          {saleHasLoyaltyActivity(sale) && (
            <span title={t("loyaltyIndicator")} style={{ display: "inline-flex" }}>
              <Gift size={12} color="#34d399" aria-label={t("loyaltyIndicator")} />
            </span>
          )}
        </span>
      ),
    },
    {
      key: "time",
      header: t("headers.time"),
      render: (sale) => formatTime(sale.createdAt, intlLocale),
    },
    {
      key: "items",
      header: t("headers.items"),
      cellStyle: { color: "#E8EDF5" },
      render: (sale) => sale.items.length,
    },
    {
      key: "payment",
      header: t("headers.payment"),
      render: (sale) => (tPayment.has(sale.paymentType) ? tPayment(sale.paymentType) : sale.paymentType),
    },
    {
      key: "sum",
      header: t("headers.sum"),
      cellStyle: { fontWeight: 700, color: "#34d399" },
      render: (sale) => `${sale.subtotal.toFixed(2)} ₴`,
    },
    {
      key: "fiscalization",
      header: t("headers.fiscalization"),
      render: (sale) => <FiscalBadge status={sale.fiscalStatus} />,
    },
  ];

  return (
    <div>
      <Table
        columns={columns}
        rows={sales ?? []}
        rowKey={(sale) => sale.transactionId}
        onRowClick={(sale) => onSelectSale(sale)}
        isLoading={isLoading}
        emptyMessage={isLoading ? tCommon("loading") : t("empty")}
      />

      {/* Footer total */}
      {!isLoading && !!sales?.length && (
        <div
          style={{
            display: "flex",
            justifyContent: "flex-end",
            marginTop: 12,
            color: "#9CA3AF",
            fontSize: 13,
          }}
        >
          {t("totalLabel")}{" "}
          <span style={{ color: "#34d399", fontWeight: 700, marginLeft: 6 }}>
            {totalAmount.toFixed(2)} ₴
          </span>
        </div>
      )}
    </div>
  );
}
