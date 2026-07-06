"use client";

// Marketplace-замовлення в кабінеті постачальника (TASK-318): список з
// розгортанням позицій і переходами статусів (new → confirmed → shipped →
// delivered; скасування з причиною з new/confirmed).

import { useState } from "react";
import { ChevronDown, ChevronRight } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { ReasonModal } from "@/components/ui/ReasonModal";
import { OrderStatusBadge } from "@/features/marketplace/components/CooperationBadges";
import type { MarketplaceOrderDto } from "@/features/marketplace/types";
import {
  useCabinetOrders,
  useUpdateCabinetOrderStatus,
} from "../hooks/useCabinetCooperation";

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

export function CabinetOrdersTab() {
  const { data: orders = [], isLoading } = useCabinetOrders();
  const updateStatus = useUpdateCabinetOrderStatus();
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [cancelTarget, setCancelTarget] = useState<MarketplaceOrderDto | null>(null);

  function transition(order: MarketplaceOrderDto, status: "confirmed" | "shipped" | "delivered") {
    updateStatus.mutate(
      { id: order.id, body: { status } },
      {
        onSuccess: () => toast.success(`Замовлення ${order.orderNumber} оновлено`),
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
              Підтвердити
            </Btn>
            <Btn size="sm" variant="danger" onClick={() => setCancelTarget(order)}>
              Скасувати
            </Btn>
          </>
        );
      case "confirmed":
        return (
          <>
            <Btn
              size="sm"
              disabled={updateStatus.isPending}
              onClick={() => transition(order, "shipped")}
            >
              Відвантажено
            </Btn>
            <Btn size="sm" variant="danger" onClick={() => setCancelTarget(order)}>
              Скасувати
            </Btn>
          </>
        );
      case "shipped":
        return (
          <Btn
            size="sm"
            variant="success"
            disabled={updateStatus.isPending}
            onClick={() => transition(order, "delivered")}
          >
            Доставлено
          </Btn>
        );
      default:
        return null;
    }
  }

  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>Завантаження...</div>;
  }

  if (orders.length === 0) {
    return (
      <div style={{ textAlign: "center", padding: "40px 0", color: "#4B5563", fontSize: 14 }}>
        Замовлень ще немає — вони зʼявляться після активації співпраці з клієнтами.
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
            <th style={headerCellStyle}>Клієнт</th>
            <th style={headerCellStyle}>Дата</th>
            <th style={headerCellStyle}>Статус</th>
            <th style={{ ...headerCellStyle, textAlign: "right" }}>Сума</th>
            <th style={headerCellStyle}>Дії</th>
          </tr>
        </thead>
        <tbody>
          {orders.map((order) => {
            const expanded = expandedId === order.id;
            return (
              <OrderRow
                key={order.id}
                order={order}
                expanded={expanded}
                onToggle={() => setExpandedId(expanded ? null : order.id)}
                actions={actionsFor(order)}
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
          pending={updateStatus.isPending}
          onConfirm={(reason) =>
            updateStatus.mutate(
              { id: cancelTarget.id, body: { status: "cancelled", reason } },
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

function OrderRow({
  order,
  expanded,
  onToggle,
  actions,
}: {
  order: MarketplaceOrderDto;
  expanded: boolean;
  onToggle: () => void;
  actions: React.ReactNode;
}) {
  return (
    <>
      <tr onClick={onToggle} style={{ cursor: "pointer" }}>
        <td style={{ ...cellStyle, color: "#6B7280" }}>
          {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        </td>
        <td style={{ ...cellStyle, fontWeight: 600, whiteSpace: "nowrap" }}>{order.orderNumber}</td>
        <td style={cellStyle}>{order.clientName}</td>
        <td style={{ ...cellStyle, color: "#9CA3AF", whiteSpace: "nowrap" }}>
          {formatDate(order.createdAt)}
        </td>
        <td style={cellStyle}>
          <OrderStatusBadge status={order.status} />
        </td>
        <td style={{ ...cellStyle, textAlign: "right", whiteSpace: "nowrap" }}>
          {money(order.totalAmount)}
        </td>
        <td style={cellStyle} onClick={(e) => e.stopPropagation()}>
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>{actions}</div>
        </td>
      </tr>
      {expanded && (
        <tr>
          <td colSpan={7} style={{ padding: 0, borderBottom: "1px solid #1A2235" }}>
            <div style={{ background: "#0D1117", padding: "12px 24px 16px 44px" }}>
              {order.comment && (
                <div style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 8 }}>
                  Коментар клієнта: {order.comment}
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
