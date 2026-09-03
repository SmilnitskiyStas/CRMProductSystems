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
  useMarketplaceOrderReceipt,
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
import { Table, type TableColumn } from "@/components/ui/Table";
import type {
  CooperationAgreementDto,
  MarketplaceOrderDto,
  MarketplaceOrderReceiptDto,
} from "@/features/marketplace/types";

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

// Inline styles for the nested item tables rendered inside an expanded order row
// (these stay plain markup per the migration brief — only the outer order list
// becomes a shared `Table`).
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
      cellStyle: { fontWeight: 600 },
      render: (order) => order.orderNumber,
    },
    {
      key: "supplier",
      header: t("headerSupplier"),
      render: (order) => order.supplierName,
    },
    {
      key: "createdBy",
      header: t("headerCreatedBy"),
      cellStyle: { color: "#9CA3AF" },
      render: (order) => order.createdByUserName ?? "—",
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
      key: "total",
      header: t("headerTotal"),
      cellStyle: { whiteSpace: "nowrap" },
      render: (order) => money(order.totalAmount, intlLocale),
    },
    {
      key: "actions",
      header: "",
      render: (order) =>
        order.status === "new" ? (
          <div onClick={(e) => e.stopPropagation()}>
            <Btn size="sm" variant="danger" onClick={() => setCancelTarget(order)}>
              {t("cancelButton")}
            </Btn>
          </div>
        ) : null,
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

function OrderExpandedContent({
  order,
  intlLocale,
}: {
  order: MarketplaceOrderDto;
  intlLocale: string;
}) {
  const t = useTranslations("Dashboard.marketplace.ordersPage.ordersTab");
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
        expectedDeliveryDate={order.expectedDeliveryDate}
        deliveredAt={order.deliveredAt}
        delayReason={order.delayReason}
        intlLocale={intlLocale}
      />
      {order.status === "delivered" && (
        <ReceiptDetail orderId={order.id} intlLocale={intlLocale} />
      )}
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
                {item.batches.length > 0 && (
                  <div style={{ marginTop: 4, fontSize: 11, color: "#6B7280" }}>
                    <span style={{ color: "#9CA3AF", fontWeight: 600 }}>{t("batchesLabel")}</span>
                    {item.batches.map((b) => (
                      <div key={b.id}>
                        {new Date(`${b.expiryDate}T12:00:00`).toLocaleDateString(intlLocale)} ·{" "}
                        {b.batchNumber || "—"} · {b.qty}
                      </div>
                    ))}
                  </div>
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
 * delayReason (TASK-585) is read-only here — only the supplier can record
 * one, via the supplier cabinet's own order view.
 */
function ShippingDetail({
  shippedAt,
  estimatedDeliveryDays,
  expectedDeliveryDate,
  deliveredAt,
  delayReason,
  intlLocale,
}: {
  shippedAt: string | null;
  estimatedDeliveryDays: number | null;
  expectedDeliveryDate: string | null;
  deliveredAt: string | null;
  delayReason: string | null;
  intlLocale: string;
}) {
  const t = useTranslations("Dashboard.marketplace.ordersPage.ordersTab");
  if (!shippedAt) return null;
  const eta = getShippingEta(shippedAt, estimatedDeliveryDays);
  // Prefer the authoritative supplier-set date (Phase 3, plan D4); fall back to the
  // client-side derived one for orders shipped before that column was populated.
  const expectedLabel = expectedDeliveryDate
    ? new Date(`${expectedDeliveryDate}T12:00:00`).toLocaleDateString(intlLocale)
    : eta
    ? formatDate(eta.estimatedDeliveryDate.toISOString(), intlLocale)
    : null;

  return (
    <>
      <div style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 8 }}>
        {t("shippedAtLabel", { date: formatDate(shippedAt, intlLocale) })}
        {!deliveredAt && expectedLabel && (
          <>
            {" · "}
            {t("estimatedDeliveryLabel", { date: expectedLabel })}
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

/**
 * Read-only "what was actually received" block, shown once an order is
 * `delivered` (TASK-586, ADR-033). Fetches the receiving session via GET
 * .../receipt — a 404 means no receiving session exists (shouldn't normally
 * happen for a delivered order, but the endpoint documents it as a possible
 * edge case), so it's treated as "nothing to show", not an error.
 */
function ReceiptDetail({ orderId, intlLocale }: { orderId: string; intlLocale: string }) {
  const t = useTranslations("Dashboard.marketplace.ordersPage.ordersTab");
  const { data: receipt, isLoading } = useMarketplaceOrderReceipt(orderId, true);

  if (isLoading) {
    return (
      <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 8 }}>{t("loading")}</div>
    );
  }
  if (!receipt) return null;

  return (
    <div style={{ marginBottom: 14, marginTop: 4 }}>
      <div style={{ color: "#4ADE80", fontSize: 12, fontWeight: 600, marginBottom: 6 }}>
        {t("receiptTitle")}
      </div>
      <div style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 8 }}>
        {t("receiptDestinationStoreLabel", { store: receipt.destinationStoreName })}
        {receipt.receivedAt && (
          <>
            {" · "}
            {t("receiptReceivedAtLabel", { date: formatDate(receipt.receivedAt, intlLocale) })}
          </>
        )}
      </div>
      <ReceiptItemsTable receipt={receipt} t={t} intlLocale={intlLocale} />
    </div>
  );
}

function ReceiptItemsTable({
  receipt,
  t,
  intlLocale,
}: {
  receipt: MarketplaceOrderReceiptDto;
  t: ReturnType<typeof useTranslations>;
  intlLocale: string;
}) {
  return (
    <table style={{ width: "100%", borderCollapse: "collapse", marginBottom: 12 }}>
      <thead>
        <tr>
          <th style={headerCellStyle}>{t("headerProduct")}</th>
          <th style={{ ...headerCellStyle, textAlign: "right" }}>{t("receiptHeaderOrdered")}</th>
          <th style={{ ...headerCellStyle, textAlign: "right" }}>{t("receiptHeaderReceived")}</th>
          <th style={headerCellStyle}>{t("receiptHeaderBatch")}</th>
          <th style={headerCellStyle}>{t("receiptHeaderExpiry")}</th>
          <th style={headerCellStyle}>{t("receiptHeaderDiscrepancy")}</th>
        </tr>
      </thead>
      <tbody>
        {receipt.items.map((item) => (
          <tr key={item.id}>
            <td style={cellStyle}>{item.productName ?? item.itemNameSnapshot}</td>
            <td style={{ ...cellStyle, textAlign: "right", color: "#9CA3AF" }}>
              {item.quantityOrdered}
            </td>
            <td style={{ ...cellStyle, textAlign: "right" }}>
              {item.quantityReceived ?? "—"}
            </td>
            <td style={{ ...cellStyle, color: "#9CA3AF" }}>{item.batchNumber ?? "—"}</td>
            <td style={{ ...cellStyle, color: "#9CA3AF", whiteSpace: "nowrap" }}>
              {item.expiryDate ? new Date(item.expiryDate).toLocaleDateString(intlLocale) : "—"}
            </td>
            <td style={{ ...cellStyle, color: item.discrepancyNotes ? "#F87171" : "#4B5563" }}>
              {item.discrepancyNotes ?? "—"}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
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

  const columns: TableColumn<CooperationAgreementDto>[] = [
    {
      key: "supplier",
      header: t("headerSupplier"),
      cellStyle: { fontWeight: 600 },
      render: (a) => a.supplierName,
    },
    {
      key: "status",
      header: t("headerStatus"),
      render: (a) => (
        <>
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
        </>
      ),
    },
    {
      key: "contractNumber",
      header: t("headerContractNumber"),
      cellStyle: { color: "#9CA3AF" },
      render: (a) => a.contractNumber ?? "—",
    },
    {
      key: "requestDate",
      header: t("headerRequestDate"),
      cellStyle: { color: "#9CA3AF", whiteSpace: "nowrap" },
      render: (a) => formatDate(a.requestedAt, intlLocale),
    },
    {
      key: "actions",
      header: "",
      render: (a) =>
        a.hasContractFile ? (
          <Btn
            size="sm"
            variant="ghost"
            icon={<Eye size={13} />}
            onClick={() => handleViewContract(a)}
          >
            {t("viewContractButton")}
          </Btn>
        ) : null,
    },
  ];

  return (
    <Table
      columns={columns}
      rows={agreements}
      rowKey={(a) => a.id}
      isLoading={isLoading}
      emptyMessage={isLoading ? t("loading") : t("empty")}
    />
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

      {activeTab === "orders" && <OrdersTab />}
      {activeTab === "cooperation" && <CooperationTab />}
    </div>
  );
}
