"use client";

import { useTranslations, useLocale } from "next-intl";
import { DetailDrawer, DrawerField, DrawerSection, DrawerGrid } from "@/components/ui/DetailDrawer";
import { FiscalBadge } from "./FiscalBadge";
import { saleHasLoyaltyActivity, type SaleDto, type SaleItemDto } from "../types";
import { Table, type TableColumn } from "@/components/ui/Table";

interface Props {
  sale: SaleDto | null;
  onClose: () => void;
}

function formatDateTime(iso: string, intlLocale: string): string {
  return new Date(iso).toLocaleString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

// Items carry no own id — the original markup keyed rows on `${productId}-${idx}` to guard
// against a repeated productId across lines. Table's rowKey only sees the row, so the index
// is folded into the row itself here to preserve that exact key shape.
type IndexedSaleItem = SaleItemDto & { _rowKey: string };

export function SaleDetailDrawer({ sale, onClose }: Props) {
  const t = useTranslations("Dashboard.pos.saleDetail");
  const tPayment = useTranslations("Dashboard.pos.paymentType");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const itemColumns: TableColumn<IndexedSaleItem>[] = [
    {
      key: "product",
      header: t("headers.product"),
      cellStyle: { color: "#E8EDF5" },
      render: (item) => (
        <>
          <div style={{ fontWeight: 500 }}>{item.productName}</div>
          <div style={{ color: "#4B5563", fontSize: 10, fontFamily: "monospace" }}>
            {item.barcode}
          </div>
        </>
      ),
    },
    {
      key: "qty",
      header: t("headers.qty"),
      render: (item) => item.quantity,
    },
    {
      key: "price",
      header: t("headers.price"),
      render: (item) => `${item.unitPrice.toFixed(2)} ₴`,
    },
    {
      key: "discount",
      header: t("headers.discount"),
      render: (item) => (
        <span style={{ color: item.discountAmount > 0 ? "#fbbf24" : "#4B5563" }}>
          {item.discountAmount > 0 ? `-${item.discountAmount.toFixed(2)} ₴` : "—"}
        </span>
      ),
    },
    {
      key: "total",
      header: t("headers.total"),
      cellStyle: { fontWeight: 600, color: "#E8EDF5" },
      render: (item) => `${item.total.toFixed(2)} ₴`,
    },
  ];

  return (
    <DetailDrawer
      isOpen={sale != null}
      onClose={onClose}
      title={sale ? t("receiptTitle", { number: sale.receiptNumber }) : ""}
      subtitle={sale ? formatDateTime(sale.createdAt, intlLocale) : undefined}
      width={560}
    >
      {sale && (
        <>
          <DrawerSection title={t("generalInfo")}>
            <DrawerGrid>
              <DrawerField label={t("paymentMethod")} value={tPayment.has(sale.paymentType) ? tPayment(sale.paymentType) : sale.paymentType} />
              <DrawerField label={t("receiptTotal")} value={`${sale.subtotal.toFixed(2)} ₴`} color="#34d399" />
              <DrawerField label={t("paid")} value={`${sale.paymentAmount.toFixed(2)} ₴`} />
              <DrawerField label={t("change")} value={`${sale.change.toFixed(2)} ₴`} />
              {sale.fiscalNumber && (
                <DrawerField
                  label={t("fiscalNumber")}
                  value={
                    <span style={{ fontFamily: "monospace", fontSize: 11 }}>{sale.fiscalNumber}</span>
                  }
                />
              )}
              <DrawerField
                label={t("fiscalization")}
                value={<FiscalBadge status={sale.fiscalStatus} />}
              />
            </DrawerGrid>
          </DrawerSection>

          {/*
            TASK-408 (read-only "Лояльність" block for manager view).

            Gated on saleHasLoyaltyActivity (../types.ts) rather than a customerId check —
            SaleDto has no CustomerId/CustomerName field at all today, on either endpoint
            (confirmed: backend/ShelfGuard.Application/Features/Pos/Dtos/PosDtos.cs +
            PosService.cs). PosTransaction.CustomerId IS persisted at sale creation
            (PosService.cs:324), but it's never mapped back into SaleDto — so the customer's
            name + a link to their card cannot be shown here without a backend DTO extension
            (new task). Note also: frontend/features/customers/ has no deep-link route for a
            single customer (CustomerDetail opens as a drawer via client-side useState in
            app/(dashboard)/customers/page.tsx, no /customers/[id]) — a future CustomerId would
            still only get you to the customers list, not straight to the record, unless that
            is added too.

            The bonus amounts below (loyaltyAccrued/Redeemed/Balance) ARE real SaleDto fields,
            but only PosService.CreateSaleAsync (mobile checkout's immediate response) populates
            them — GetSalesForShiftAsync (what this web view actually calls) never does, so this
            section is always hidden today. It will start rendering with zero frontend changes
            once that mapping gap is closed on the backend. Full writeup:
            .claude/logs/tasks/408_2026-07-26_web-pos-loyalty-section_frontend-developer.md
          */}
          {saleHasLoyaltyActivity(sale) && (
            <DrawerSection title={t("loyalty.title")}>
              <DrawerGrid>
                {sale.loyaltyAccrued != null && (
                  <DrawerField
                    label={t("loyalty.accrued")}
                    value={`+${sale.loyaltyAccrued.toFixed(2)} ₴`}
                    color="#34d399"
                  />
                )}
                {sale.loyaltyRedeemed != null && (
                  <DrawerField
                    label={t("loyalty.redeemed")}
                    value={`-${sale.loyaltyRedeemed.toFixed(2)} ₴`}
                    color="#fbbf24"
                  />
                )}
                {sale.loyaltyBalance != null && (
                  <DrawerField label={t("loyalty.balance")} value={`${sale.loyaltyBalance.toFixed(2)} ₴`} />
                )}
              </DrawerGrid>
            </DrawerSection>
          )}

          <DrawerSection title={t("itemsSection", { count: sale.items.length })}>
            <Table
              columns={itemColumns}
              rows={sale.items.map((item, idx) => ({ ...item, _rowKey: `${item.productId}-${idx}` }))}
              rowKey={(item) => item._rowKey}
            />
          </DrawerSection>
        </>
      )}
    </DetailDrawer>
  );
}
