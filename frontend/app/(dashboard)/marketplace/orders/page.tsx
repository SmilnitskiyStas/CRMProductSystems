"use client";

// Мої marketplace-замовлення + угоди про співпрацю (клієнтська сторона, TASK-318).

import { useState } from "react";
import { ChevronDown, ChevronRight, Eye } from "lucide-react";
import { toast } from "sonner";
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
import { useMe } from "@/features/auth/hooks/useAuth";
import { TENANT_ROLES, type AppRole } from "@/lib/roles";
import { Btn } from "@/components/ui/Btn";
import { ReasonModal } from "@/components/ui/ReasonModal";
import type { CooperationAgreementDto, MarketplaceOrderDto } from "@/features/marketplace/types";

type ActiveTab = "orders" | "cooperation";

function money(v: number): string {
  return v.toLocaleString("uk-UA", {
    style: "currency",
    currency: "UAH",
    minimumFractionDigits: 2,
  });
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString("uk-UA", {
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
  const { data: orders = [], isLoading } = useMyMarketplaceOrders();
  const cancelOrder = useCancelMarketplaceOrder();
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [cancelTarget, setCancelTarget] = useState<MarketplaceOrderDto | null>(null);

  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>Завантаження...</div>;
  }

  if (orders.length === 0) {
    return (
      <div style={{ textAlign: "center", padding: "40px 0", color: "#4B5563", fontSize: 14 }}>
        Замовлень ще немає. Оформіть перше замовлення на сторінці постачальника
        з активною співпрацею.
      </div>
    );
  }

  return (
    <div style={{ overflowX: "auto" }}>
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={{ ...headerCellStyle, width: 30 }}></th>
            <th style={headerCellStyle}>Номер</th>
            <th style={headerCellStyle}>Постачальник</th>
            <th style={headerCellStyle}>Дата</th>
            <th style={headerCellStyle}>Статус</th>
            <th style={{ ...headerCellStyle, textAlign: "right" }}>Сума</th>
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
                onToggle={() => setExpandedId(expanded ? null : order.id)}
                onCancel={() => setCancelTarget(order)}
              />
            );
          })}
        </tbody>
      </table>

      {cancelTarget && (
        <ReasonModal
          title={`Скасувати замовлення ${cancelTarget.orderNumber}?`}
          label="Причина скасування"
          confirmLabel="Скасувати замовлення"
          required
          pending={cancelOrder.isPending}
          onConfirm={(reason) =>
            cancelOrder.mutate(
              { orderId: cancelTarget.id, reason },
              {
                onSuccess: () => {
                  toast.success("Замовлення скасовано");
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
  onToggle,
  onCancel,
}: {
  order: MarketplaceOrderDto;
  expanded: boolean;
  onToggle: () => void;
  onCancel: () => void;
}) {
  return (
    <>
      <tr onClick={onToggle} style={{ cursor: "pointer" }}>
        <td style={{ ...cellStyle, color: "#6B7280" }}>
          {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        </td>
        <td style={{ ...cellStyle, fontWeight: 600 }}>{order.orderNumber}</td>
        <td style={cellStyle}>{order.supplierName}</td>
        <td style={{ ...cellStyle, color: "#9CA3AF", whiteSpace: "nowrap" }}>
          {formatDate(order.createdAt)}
        </td>
        <td style={cellStyle}>
          <OrderStatusBadge status={order.status} />
        </td>
        <td style={{ ...cellStyle, textAlign: "right", whiteSpace: "nowrap" }}>
          {money(order.totalAmount)}
        </td>
        <td
          style={{ ...cellStyle, textAlign: "right" }}
          onClick={(e) => e.stopPropagation()}
        >
          {order.status === "new" && (
            <Btn size="sm" variant="danger" onClick={onCancel}>
              Скасувати
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
                  Коментар: {order.comment}
                </div>
              )}
              {order.cancelReason && (
                <div style={{ color: "#F87171", fontSize: 12, marginBottom: 8 }}>
                  Причина скасування: {order.cancelReason}
                </div>
              )}
              <table style={{ width: "100%", borderCollapse: "collapse" }}>
                <thead>
                  <tr>
                    <th style={headerCellStyle}>Товар</th>
                    <th style={{ ...headerCellStyle, textAlign: "right" }}>Ціна</th>
                    <th style={{ ...headerCellStyle, textAlign: "right" }}>К-сть</th>
                    <th style={{ ...headerCellStyle, textAlign: "right" }}>Сума</th>
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
                        {money(item.price)}
                      </td>
                      <td style={{ ...cellStyle, textAlign: "right", color: "#9CA3AF" }}>
                        {item.qty}
                      </td>
                      <td style={{ ...cellStyle, textAlign: "right", whiteSpace: "nowrap" }}>
                        {money(item.lineTotal)}
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

// ─── Cooperation tab ───────────────────────────────────────────────────────────

function CooperationTab() {
  const { data: agreements = [], isLoading } = useMyCooperation();

  function handleViewContract(agreement: CooperationAgreementDto) {
    marketplaceApi
      .downloadAgreementContract(agreement.id)
      .catch((err) => toast.error(err.message));
  }

  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>Завантаження...</div>;
  }

  if (agreements.length === 0) {
    return (
      <div style={{ textAlign: "center", padding: "40px 0", color: "#4B5563", fontSize: 14 }}>
        Угод про співпрацю ще немає. Подайте заявку на сторінці постачальника
        в маркетплейсі.
      </div>
    );
  }

  return (
    <div style={{ overflowX: "auto" }}>
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={headerCellStyle}>Постачальник</th>
            <th style={headerCellStyle}>Статус</th>
            <th style={headerCellStyle}>№ договору</th>
            <th style={headerCellStyle}>Дата заявки</th>
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
              </td>
              <td style={{ ...cellStyle, color: "#9CA3AF" }}>{a.contractNumber ?? "—"}</td>
              <td style={{ ...cellStyle, color: "#9CA3AF", whiteSpace: "nowrap" }}>
                {formatDate(a.requestedAt)}
              </td>
              <td style={{ ...cellStyle, textAlign: "right" }}>
                {a.hasContractFile && (
                  <Btn
                    size="sm"
                    variant="ghost"
                    icon={<Eye size={13} />}
                    onClick={() => handleViewContract(a)}
                  >
                    Переглянути договір
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
  const { data: me } = useMe();
  const [activeTab, setActiveTab] = useState<ActiveTab>("orders");

  if (me && !TENANT_ROLES.has(me.role as AppRole)) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 14 }}>
        Сторінка доступна лише клієнтським компаніям.
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
          Мої замовлення
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          Замовлення у постачальників маркетплейсу та угоди про співпрацю
        </p>
      </div>

      <div style={{ borderBottom: "1px solid #1F2937", marginBottom: 24, display: "flex" }}>
        <button style={tabStyle("orders")} onClick={() => setActiveTab("orders")}>
          Замовлення
        </button>
        <button style={tabStyle("cooperation")} onClick={() => setActiveTab("cooperation")}>
          Співпраця
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
