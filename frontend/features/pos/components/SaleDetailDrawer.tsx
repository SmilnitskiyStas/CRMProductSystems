"use client";

import { DetailDrawer, DrawerField, DrawerSection, DrawerGrid } from "@/components/ui/DetailDrawer";
import { FiscalBadge } from "./FiscalBadge";
import type { SaleDto } from "../types";

interface Props {
  sale: SaleDto | null;
  onClose: () => void;
}

const PAYMENT_LABEL: Record<string, string> = {
  Cash: "Готівка",
  Card: "Картка",
};

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("uk-UA", {
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
  return (
    <DetailDrawer
      isOpen={sale != null}
      onClose={onClose}
      title={sale ? `Чек #${sale.receiptNumber}` : ""}
      subtitle={sale ? formatDateTime(sale.createdAt) : undefined}
      width={560}
    >
      {sale && (
        <>
          <DrawerSection title="Загальна інформація">
            <DrawerGrid>
              <DrawerField label="Спосіб оплати" value={PAYMENT_LABEL[sale.paymentType] ?? sale.paymentType} />
              <DrawerField label="Сума чеку" value={`${sale.subtotal.toFixed(2)} ₴`} color="#34d399" />
              <DrawerField label="Оплачено" value={`${sale.paymentAmount.toFixed(2)} ₴`} />
              <DrawerField label="Решта" value={`${sale.change.toFixed(2)} ₴`} />
              {sale.fiscalNumber && (
                <DrawerField
                  label="Фіскальний номер"
                  value={
                    <span style={{ fontFamily: "monospace", fontSize: 11 }}>{sale.fiscalNumber}</span>
                  }
                />
              )}
              <DrawerField
                label="Фіскалізація"
                value={<FiscalBadge status={sale.fiscalStatus} />}
              />
            </DrawerGrid>
          </DrawerSection>

          <DrawerSection title={`Товари (${sale.items.length})`}>
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
                    <th style={itemTh}>Товар</th>
                    <th style={{ ...itemTh, textAlign: "right" }}>К-ть</th>
                    <th style={{ ...itemTh, textAlign: "right" }}>Ціна</th>
                    <th style={{ ...itemTh, textAlign: "right" }}>Знижка</th>
                    <th style={{ ...itemTh, textAlign: "right" }}>Сума</th>
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
