"use client";

// Marketplace-замовлення в кабінеті постачальника (TASK-318): список з
// розгортанням позицій і переходами статусів (new → confirmed → shipped →
// delivered; скасування з причиною з new/confirmed).

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { ChevronDown, ChevronRight } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { ReasonModal } from "@/components/ui/ReasonModal";
import { Table, type TableColumn } from "@/components/ui/Table";
import { OrderStatusBadge } from "@/features/marketplace/components/CooperationBadges";
import { getShippingEta, getDeliveryOutcome } from "@/features/marketplace/utils";
import type { MarketplaceOrderDto } from "@/features/marketplace/types";
import { EstimateDeliveryModal } from "./EstimateDeliveryModal";
import {
  useCabinetOrders,
  useUpdateCabinetOrderStatus,
  useSetOrderDelayReason,
} from "../hooks/useCabinetCooperation";

// Inline styles for the nested items table rendered inside an expanded order
// row — stays plain markup per the migration brief, only the outer order list
// becomes a shared `Table`.
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

function money(v: number, locale: string): string {
  return v.toLocaleString(locale, {
    style: "currency",
    currency: "UAH",
    minimumFractionDigits: 2,
  });
}

function formatDate(iso: string, locale: string): string {
  return new Date(iso).toLocaleString(locale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function CabinetOrdersTab() {
  const t = useTranslations("Dashboard.supplierCabinet.ordersTab");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: orders = [], isLoading } = useCabinetOrders();
  const updateStatus = useUpdateCabinetOrderStatus();
  const setDelayReason = useSetOrderDelayReason();
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [cancelTarget, setCancelTarget] = useState<MarketplaceOrderDto | null>(null);
  const [shipTarget, setShipTarget] = useState<MarketplaceOrderDto | null>(null);
  const [delayReasonTarget, setDelayReasonTarget] = useState<MarketplaceOrderDto | null>(null);

  function transition(order: MarketplaceOrderDto, status: "confirmed") {
    updateStatus.mutate(
      { id: order.id, body: { status } },
      {
        onSuccess: () => toast.success(t("toastUpdated", { number: order.orderNumber })),
        onError: (err) => toast.error(err.message),
      }
    );
  }

  function actionsFor(order: MarketplaceOrderDto): React.ReactNode {
    switch (order.status) {
      case "new":
        return (
          <>
            <Btn
              size="sm"
              variant="success"
              disabled={updateStatus.isPending}
              onClick={() => transition(order, "confirmed")}
            >
              {t("confirmButton")}
            </Btn>
            <Btn size="sm" variant="danger" onClick={() => setCancelTarget(order)}>
              {t("cancelButton")}
            </Btn>
          </>
        );
      case "confirmed":
        return (
          <>
            <Btn
              size="sm"
              disabled={updateStatus.isPending}
              onClick={() => setShipTarget(order)}
            >
              {t("shipButton")}
            </Btn>
            <Btn size="sm" variant="danger" onClick={() => setCancelTarget(order)}>
              {t("cancelButton")}
            </Btn>
          </>
        );
      case "shipped": {
        const eta = getShippingEta(order.shippedAt, order.estimatedDeliveryDays);
        return (
          <>
            <span style={{ color: "#6B7280", fontSize: 12, fontStyle: "italic" }}>
              {t("awaitingClientConfirmationHint")}
            </span>
            {eta?.isOverdue && (
              <Btn
                size="sm"
                variant="ghost"
                disabled={setDelayReason.isPending}
                onClick={() => setDelayReasonTarget(order)}
              >
                {t("delayReasonButton")}
              </Btn>
            )}
          </>
        );
      }
      default:
        return null;
    }
  }

  const columns: TableColumn<MarketplaceOrderDto>[] = [
    {
      key: "expand",
      header: "",
      width: 30,
      cellStyle: { color: "#6B7280" },
      render: (order) =>
        expandedId === order.id ? <ChevronDown size={14} /> : <ChevronRight size={14} />,
    },
    {
      key: "number",
      header: t("headerNumber"),
      align: "left",
      cellStyle: { fontWeight: 600, whiteSpace: "nowrap" },
      render: (order) => order.orderNumber,
    },
    {
      key: "client",
      header: t("headerClient"),
      render: (order) => order.clientName,
    },
    {
      key: "date",
      header: t("headerDate"),
      cellStyle: { color: "#9CA3AF", whiteSpace: "nowrap" },
      render: (order) => formatDate(order.createdAt, intlLocale),
    },
    {
      key: "status",
      header: t("headerStatus"),
      render: (order) => (
        <>
          <OrderStatusBadge status={order.status} />
          <ShippingEtaHint order={order} />
        </>
      ),
    },
    {
      key: "delivery",
      header: t("headerDelivery"),
      cellStyle: { whiteSpace: "nowrap" },
      render: (order) => <DeliveryOutcomeCell order={order} />,
    },
    {
      key: "total",
      header: t("headerTotal"),
      cellStyle: { whiteSpace: "nowrap" },
      render: (order) => money(order.totalAmount, intlLocale),
    },
    {
      key: "actions",
      header: t("headerActions"),
      render: (order) => (
        <div
          onClick={(e) => e.stopPropagation()}
          style={{ display: "flex", gap: 8, flexWrap: "wrap" }}
        >
          {actionsFor(order)}
        </div>
      ),
    },
  ];

  return (
    <div>
      <Table
        columns={columns}
        rows={orders}
        rowKey={(order) => order.id}
        onRowClick={(order) => setExpandedId(expandedId === order.id ? null : order.id)}
        expandedRowKey={expandedId}
        renderExpanded={(order) => <OrderExpandedContent order={order} intlLocale={intlLocale} />}
        isLoading={isLoading}
        emptyMessage={isLoading ? t("loading") : t("empty")}
      />

      {cancelTarget && (
        <ReasonModal
          title={t("cancelModalTitle", { number: cancelTarget.orderNumber })}
          label={t("cancelModalLabel")}
          confirmLabel={t("cancelModalConfirm")}
          required
          pending={updateStatus.isPending}
          onConfirm={(reason) =>
            updateStatus.mutate(
              { id: cancelTarget.id, body: { status: "cancelled", reason } },
              {
                onSuccess: () => {
                  toast.success(t("toastCancelled"));
                  setCancelTarget(null);
                },
                onError: (err) => toast.error(err.message),
              }
            )
          }
          onClose={() => setCancelTarget(null)}
        />
      )}

      {shipTarget && (
        <EstimateDeliveryModal
          title={t("shipModalTitle", { number: shipTarget.orderNumber })}
          pending={updateStatus.isPending}
          onConfirm={(estimatedDeliveryDays) =>
            updateStatus.mutate(
              { id: shipTarget.id, body: { status: "shipped", estimatedDeliveryDays } },
              {
                onSuccess: () => {
                  toast.success(t("toastUpdated", { number: shipTarget.orderNumber }));
                  setShipTarget(null);
                },
                onError: (err) => toast.error(err.message),
              }
            )
          }
          onClose={() => setShipTarget(null)}
        />
      )}

      {delayReasonTarget && (
        <ReasonModal
          title={t("delayReasonModalTitle", { number: delayReasonTarget.orderNumber })}
          label={t("delayReasonModalLabel")}
          confirmLabel={t("delayReasonModalConfirm")}
          variant="primary"
          required
          pending={setDelayReason.isPending}
          onConfirm={(reason) =>
            setDelayReason.mutate(
              { id: delayReasonTarget.id, body: { reason } },
              {
                onSuccess: () => {
                  toast.success(t("toastDelayReasonSaved"));
                  setDelayReasonTarget(null);
                },
                onError: (err) => toast.error(err.message),
              }
            )
          }
          onClose={() => setDelayReasonTarget(null)}
        />
      )}
    </div>
  );
}

function OrderExpandedContent({
  order,
  intlLocale,
}: {
  order: MarketplaceOrderDto;
  intlLocale: string;
}) {
  const t = useTranslations("Dashboard.supplierCabinet.ordersTab");
  return (
    <div style={{ background: "#0D1117", padding: "12px 24px 16px 44px" }}>
      {order.comment && (
        <div style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 8 }}>
          {t("commentLabel", { comment: order.comment })}
        </div>
      )}
      {order.cancelReason && (
        <div style={{ color: "#F87171", fontSize: 12, marginBottom: 8 }}>
          {t("cancelReasonLabel", { reason: order.cancelReason })}
        </div>
      )}
      <ShippingDetail
        shippedAt={order.shippedAt}
        estimatedDeliveryDays={order.estimatedDeliveryDays}
        deliveredAt={order.deliveredAt}
        delayReason={order.delayReason}
        intlLocale={intlLocale}
      />
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={headerCellStyle}>{t("headerProduct")}</th>
            <th style={{ ...headerCellStyle, textAlign: "right" }}>{t("headerPrice")}</th>
            <th style={{ ...headerCellStyle, textAlign: "right" }}>{t("headerQty")}</th>
            <th style={{ ...headerCellStyle, textAlign: "right" }}>{t("headerLineTotal")}</th>
          </tr>
        </thead>
        <tbody>
          {order.items.map((item) => (
            <tr key={item.id}>
              <td style={cellStyle}>
                {item.itemName}
                {item.unit && (
                  <span style={{ color: "#4B5563", fontSize: 11 }}> · {item.unit}</span>
                )}
              </td>
              <td style={{ ...cellStyle, textAlign: "right", color: "#9CA3AF", whiteSpace: "nowrap" }}>
                {money(item.price, intlLocale)}
              </td>
              <td style={{ ...cellStyle, textAlign: "right", color: "#9CA3AF" }}>
                {item.qty}
              </td>
              <td style={{ ...cellStyle, textAlign: "right", whiteSpace: "nowrap" }}>
                {money(item.lineTotal, intlLocale)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * Compact ETA label rendered directly under the status badge in the table
 * row — mirrors the client-facing ShippingEtaHint in
 * app/(dashboard)/marketplace/orders/page.tsx (TASK-584/585) so the
 * supplier sees the same "in transit, N of M days" / overdue phrasing the
 * client sees, without expanding the row.
 */
function ShippingEtaHint({ order }: { order: MarketplaceOrderDto }) {
  const t = useTranslations("Dashboard.supplierCabinet.ordersTab");
  if (order.status !== "shipped" || order.estimatedDeliveryDays == null) return null;
  const eta = getShippingEta(order.shippedAt, order.estimatedDeliveryDays);
  if (!eta) return null;

  return (
    <div style={{ fontSize: 11, color: eta.isOverdue ? "#FBBF24" : "#6B7280", marginTop: 3 }}>
      {eta.isOverdue
        ? t("etaOverdue")
        : t("etaInTransit", {
            daysElapsed: eta.daysElapsed,
            estimatedDeliveryDays: order.estimatedDeliveryDays,
          })}
    </div>
  );
}

/**
 * Retrospective transit-duration + on-time indicator column (TASK-587) —
 * distinct from the live in-progress ShippingEtaHint above (which only
 * applies while status === "shipped", comparing today against the ETA).
 * This compares the actual deliveredAt against shippedAt/estimatedDeliveryDays
 * once an order has been delivered, so renders "—" for anything not yet
 * delivered (not shipped, still in transit, or cancelled).
 */
function DeliveryOutcomeCell({ order }: { order: MarketplaceOrderDto }) {
  const t = useTranslations("Dashboard.supplierCabinet.ordersTab");
  const outcome = getDeliveryOutcome(
    order.shippedAt,
    order.deliveredAt,
    order.estimatedDeliveryDays
  );
  if (!outcome) return <span style={{ color: "#4B5563" }}>—</span>;

  return (
    <div style={{ display: "flex", alignItems: "center", gap: 6, flexWrap: "wrap" }}>
      <span style={{ color: "#9CA3AF", fontSize: 12 }}>
        {t("deliveryTransitDays", { days: outcome.transitDays })}
      </span>
      {outcome.isOnTime === true && (
        <span style={{ color: "#4ADE80", fontSize: 11, fontWeight: 600 }}>
          {t("deliveryOnTime")}
        </span>
      )}
      {outcome.isOnTime === false && (
        <span style={{ color: "#F87171", fontSize: 11, fontWeight: 600 }}>
          {t("deliveryLate", {
            days: outcome.transitDays - (order.estimatedDeliveryDays ?? 0),
          })}
        </span>
      )}
    </div>
  );
}

/**
 * Shipped/estimated-delivery/delivered dates for the supplier's own order
 * view — symmetric with the client-facing detail in
 * app/(dashboard)/marketplace/orders/page.tsx (TASK-584). The estimated
 * delivery date is derived client-side via getShippingEta and swapped for
 * the actual deliveredAt once the order is delivered. delayReason (TASK-585)
 * is shown once the supplier has recorded one, styled like the cancelReason
 * warning above it.
 */
function ShippingDetail({
  shippedAt,
  estimatedDeliveryDays,
  deliveredAt,
  delayReason,
  intlLocale,
}: {
  shippedAt: string | null;
  estimatedDeliveryDays: number | null;
  deliveredAt: string | null;
  delayReason: string | null;
  intlLocale: string;
}) {
  const t = useTranslations("Dashboard.supplierCabinet.ordersTab");
  if (!shippedAt) return null;
  const eta = getShippingEta(shippedAt, estimatedDeliveryDays);

  return (
    <>
      <div style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 8 }}>
        {t("shippedAtLabel", { date: formatDate(shippedAt, intlLocale) })}
        {!deliveredAt && eta && (
          <>
            {" · "}
            {t("estimatedDeliveryLabel", {
              date: formatDate(eta.estimatedDeliveryDate.toISOString(), intlLocale),
            })}
          </>
        )}
      </div>
      {deliveredAt && (
        <div style={{ color: "#4ADE80", fontSize: 12, marginBottom: 8 }}>
          {t("deliveredAtLabel", { date: formatDate(deliveredAt, intlLocale) })}
        </div>
      )}
      {delayReason && (
        <div style={{ color: "#F87171", fontSize: 12, marginBottom: 8 }}>
          {t("delayReasonLabel", { reason: delayReason })}
        </div>
      )}
    </>
  );
}
