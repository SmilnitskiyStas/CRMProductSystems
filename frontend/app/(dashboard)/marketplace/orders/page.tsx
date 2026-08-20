"use client";

// Мої marketplace-замовлення + угоди про співпрацю (клієнтська сторона, TASK-318).

import { useState } from "react";
import { ChevronDown, ChevronRight, Eye } from "lucide-react";
import { toast } from "sonner";
import { useTranslations, useLocale } from "next-intl";
import {
  useMyCooperation,
  useMyMarketplaceOrders,
  useCancelMarketplaceOrder,
} from "@/features/marketplace/hooks/useCooperation";
import { marketplaceApi } from "@/features/marketplace/api/marketplace-api";
import {
  AgreementStatusBadge,
  OrderStatusBadge,
} from "@/features/marketplace/components/CooperationBadges";
import { getShippingEta } from "@/features/marketplace/utils";
import { SigningMethodChoice } from "@/features/marketplace/components/SigningMethodChoice";
import { useMe } from "@/features/auth/hooks/useAuth";
import { TENANT_ROLES, type AppRole } from "@/lib/roles";
import { Btn } from "@/components/ui/Btn";
import { ReasonModal } from "@/components/ui/ReasonModal";
import type { CooperationAgreementDto, MarketplaceOrderDto } from "@/features/marketplace/types";

type ActiveTab = "orders" | "cooperation";

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

// ─── Orders tab ────────────────────────────────────────────────────────────────

function OrdersTab() {
  const t = useTranslations("Dashboard.marketplace.ordersPage.ordersTab");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: orders = [], isLoading } = useMyMarketplaceOrders();
  const cancelOrder = useCancelMarketplaceOrder();
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [cancelTarget, setCancelTarget] = useState<MarketplaceOrderDto | null>(null);

  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>{t("loading")}</div>;
  }

  if (orders.length === 0) {
    return (
      <div style={{ textAlign: "center", padding: "40px 0", color: "#4B5563", fontSize: 14 }}>
        {t("empty")}
      </div>
    );
  }

  return (
    <div style={{ overflowX: "auto" }}>
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={{ ...headerCellStyle, width: 30 }}></th>
            <th style={headerCellStyle}>{t("headerNumber")}</th>
            <th style={headerCellStyle}>{t("headerSupplier")}</th>
            <th style={headerCellStyle}>{t("headerDate")}</th>
            <th style={headerCellStyle}>{t("headerStatus")}</th>
            <th style={{ ...headerCellStyle, textAlign: "right" }}>{t("headerTotal")}</th>
            <th style={headerCellStyle}></th>
          </tr>
        </thead>
        <tbody>
          {orders.map((order) => {
            const expanded = expandedId === order.id;
            return (
              <FragmentRow
                key={order.id}
                order={order}
                expanded={expanded}
                intlLocale={intlLocale}
                onToggle={() => setExpandedId(expanded ? null : order.id)}
                onCancel={() => setCancelTarget(order)}
              />
            );
          })}
        </tbody>
      </table>

      {cancelTarget && (
        <ReasonModal
          title={t("cancelModalTitle", { number: cancelTarget.orderNumber })}
          label={t("cancelModalLabel")}
          confirmLabel={t("cancelModalConfirm")}
          required
          pending={cancelOrder.isPending}
          onConfirm={(reason) =>
            cancelOrder.mutate(
              { orderId: cancelTarget.id, reason },
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
    </div>
  );
}

function FragmentRow({
  order,
  expanded,
  intlLocale,
  onToggle,
  onCancel,
}: {
  order: MarketplaceOrderDto;
  expanded: boolean;
  intlLocale: string;
  onToggle: () => void;
  onCancel: () => void;
}) {
  const t = useTranslations("Dashboard.marketplace.ordersPage.ordersTab");
  return (
    <>
      <tr onClick={onToggle} style={{ cursor: "pointer" }}>
        <td style={{ ...cellStyle, color: "#6B7280" }}>
          {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        </td>
        <td style={{ ...cellStyle, fontWeight: 600 }}>{order.orderNumber}</td>
        <td style={cellStyle}>{order.supplierName}</td>
        <td style={{ ...cellStyle, color: "#9CA3AF", whiteSpace: "nowrap" }}>
          {formatDate(order.createdAt, intlLocale)}
        </td>
        <td style={cellStyle}>
          <OrderStatusBadge status={order.status} />
          <ShippingEtaHint order={order} />
        </td>
        <td style={{ ...cellStyle, textAlign: "right", whiteSpace: "nowrap" }}>
          {money(order.totalAmount, intlLocale)}
        </td>
        <td
          style={{ ...cellStyle, textAlign: "right" }}
          onClick={(e) => e.stopPropagation()}
        >
          {order.status === "new" && (
            <Btn size="sm" variant="danger" onClick={onCancel}>
              {t("cancelButton")}
            </Btn>
          )}
        </td>
      </tr>
      {expanded && (
        <tr>
          <td colSpan={7} style={{ padding: 0, borderBottom: "1px solid #1A2235" }}>
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
          </td>
        </tr>
      )}
    </>
  );
}

/**
 * Compact ETA label rendered directly under the status badge in the table
 * row — visible without expanding the row (TASK-584). This is the part that
 * most directly answers the original "nowhere shows it's on the way"
 * complaint: the client no longer has to click into the order to learn
 * anything about shipping timing.
 */
function ShippingEtaHint({ order }: { order: MarketplaceOrderDto }) {
  const t = useTranslations("Dashboard.marketplace.ordersPage.ordersTab");
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
 * Shipped/estimated-delivery/delivered dates in the expanded row detail
 * (TASK-584). The estimated delivery date is derived client-side via
 * getShippingEta and swapped for the actual deliveredAt once delivered.
 */
function ShippingDetail({
  shippedAt,
  estimatedDeliveryDays,
  deliveredAt,
  intlLocale,
}: {
  shippedAt: string | null;
  estimatedDeliveryDays: number | null;
  deliveredAt: string | null;
  intlLocale: string;
}) {
  const t = useTranslations("Dashboard.marketplace.ordersPage.ordersTab");
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
    </>
  );
}

// ─── Cooperation tab ───────────────────────────────────────────────────────────

function CooperationTab() {
  const t = useTranslations("Dashboard.marketplace.ordersPage.cooperationTab");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: agreements = [], isLoading } = useMyCooperation();

  function handleViewContract(agreement: CooperationAgreementDto) {
    marketplaceApi
      .downloadAgreementContract(agreement.id)
      .catch((err) => toast.error(err.message));
  }

  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>{t("loading")}</div>;
  }

  if (agreements.length === 0) {
    return (
      <div style={{ textAlign: "center", padding: "40px 0", color: "#4B5563", fontSize: 14 }}>
        {t("empty")}
      </div>
    );
  }

  return (
    <div style={{ overflowX: "auto" }}>
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={headerCellStyle}>{t("headerSupplier")}</th>
            <th style={headerCellStyle}>{t("headerStatus")}</th>
            <th style={headerCellStyle}>{t("headerContractNumber")}</th>
            <th style={headerCellStyle}>{t("headerRequestDate")}</th>
            <th style={headerCellStyle}></th>
          </tr>
        </thead>
        <tbody>
          {agreements.map((a) => (
            <tr key={a.id}>
              <td style={{ ...cellStyle, fontWeight: 600 }}>{a.supplierName}</td>
              <td style={cellStyle}>
                <AgreementStatusBadge status={a.status} />
                {a.rejectionReason && (
                  <div style={{ color: "#F87171", fontSize: 11, marginTop: 4 }}>
                    {a.rejectionReason}
                  </div>
                )}
                {a.status === "awaiting_signature" && (
                  <div style={{ marginTop: 8 }}>
                    <SigningMethodChoice agreement={a} />
                  </div>
                )}
              </td>
              <td style={{ ...cellStyle, color: "#9CA3AF" }}>{a.contractNumber ?? "—"}</td>
              <td style={{ ...cellStyle, color: "#9CA3AF", whiteSpace: "nowrap" }}>
                {formatDate(a.requestedAt, intlLocale)}
              </td>
              <td style={{ ...cellStyle, textAlign: "right" }}>
                {a.hasContractFile && (
                  <Btn
                    size="sm"
                    variant="ghost"
                    icon={<Eye size={13} />}
                    onClick={() => handleViewContract(a)}
                  >
                    {t("viewContractButton")}
                  </Btn>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ─── Page ──────────────────────────────────────────────────────────────────────

export default function MarketplaceOrdersPage() {
  const t = useTranslations("Dashboard.marketplace.ordersPage");
  const { data: me } = useMe();
  const [activeTab, setActiveTab] = useState<ActiveTab>("orders");

  if (me && !TENANT_ROLES.has(me.role as AppRole)) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        {t("roleGate")}
      </div>
    );
  }

  const tabStyle = (tab: ActiveTab): React.CSSProperties => ({
    padding: "10px 20px",
    background: "transparent",
    border: "none",
    borderBottom: activeTab === tab ? "2px solid #3B82F6" : "2px solid transparent",
    color: activeTab === tab ? "#3B82F6" : "#6B7280",
    fontSize: 13,
    fontWeight: activeTab === tab ? 600 : 400,
    cursor: "pointer",
    marginBottom: -1,
    transition: "color 0.15s",
  });

  return (
    <div style={{ padding: "28px 32px" }}>
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          {t("subtitle")}
        </p>
      </div>

      <div style={{ borderBottom: "1px solid #1F2937", marginBottom: 24, display: "flex" }}>
        <button style={tabStyle("orders")} onClick={() => setActiveTab("orders")}>
          {t("tabOrders")}
        </button>
        <button style={tabStyle("cooperation")} onClick={() => setActiveTab("cooperation")}>
          {t("tabCooperation")}
        </button>
      </div>

      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: 24,
        }}
      >
        {activeTab === "orders" && <OrdersTab />}
        {activeTab === "cooperation" && <CooperationTab />}
      </div>
    </div>
  );
}
