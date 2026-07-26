"use client";

import { useTranslations, useLocale } from "next-intl";
import { DetailDrawer, DrawerField, DrawerSection, DrawerGrid } from "@/components/ui/DetailDrawer";
import { FiscalBadge } from "./FiscalBadge";
import { saleHasLoyaltyActivity, type SaleDto } from "../types";

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

const itemTd: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 12,
  padding: "9px 10px",
  borderBottom: "1px solid #1F2937",
  verticalAlign: "middle",
};

const itemTh: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 10,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: 0.5,
  padding: "8px 10px",
  borderBottom: "1px solid #1F2937",
  textAlign: "left",
};

export function SaleDetailDrawer({ sale, onClose }: Props) {
  const t = useTranslations("Dashboard.pos.saleDetail");
  const tPayment = useTranslations("Dashboard.pos.paymentType");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

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
            <div
              style={{
                background: "#0D1117",
                border: "1px solid #1F2937",
                borderRadius: 8,
                overflow: "hidden",
              }}
            >
              <table style={{ width: "100%", borderCollapse: "collapse" }}>
                <thead>
                  <tr>
                    <th style={itemTh}>{t("headers.product")}</th>
                    <th style={{ ...itemTh, textAlign: "right" }}>{t("headers.qty")}</th>
                    <th style={{ ...itemTh, textAlign: "right" }}>{t("headers.price")}</th>
                    <th style={{ ...itemTh, textAlign: "right" }}>{t("headers.discount")}</th>
                    <th style={{ ...itemTh, textAlign: "right" }}>{t("headers.total")}</th>
                  </tr>
                </thead>
                <tbody>
                  {sale.items.map((item, idx) => (
                    <tr key={`${item.productId}-${idx}`}>
                      <td style={itemTd}>
                        <div style={{ fontWeight: 500 }}>{item.productName}</div>
                        <div style={{ color: "#4B5563", fontSize: 10, fontFamily: "monospace" }}>
                          {item.barcode}
                        </div>
                      </td>
                      <td style={{ ...itemTd, textAlign: "right", color: "#9CA3AF" }}>
                        {item.quantity}
                      </td>
                      <td style={{ ...itemTd, textAlign: "right", color: "#9CA3AF" }}>
                        {item.unitPrice.toFixed(2)} ₴
                      </td>
                      <td style={{ ...itemTd, textAlign: "right", color: item.discountAmount > 0 ? "#fbbf24" : "#4B5563" }}>
                        {item.discountAmount > 0 ? `-${item.discountAmount.toFixed(2)} ₴` : "—"}
                      </td>
                      <td style={{ ...itemTd, textAlign: "right", fontWeight: 600, color: "#E8EDF5" }}>
                        {item.total.toFixed(2)} ₴
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </DrawerSection>
        </>
      )}
    </DetailDrawer>
  );
}
