"use client";

// Status badges + Ukrainian label maps for cooperation agreements, marketplace
// orders and support tickets (TASK-318). Used by both the client marketplace
// pages and the supplier cabinet.

import type {
  CooperationStatus,
  MarketplaceOrderStatus,
  SupportTicketStatus,
} from "../types";

interface BadgeColors {
  background: string;
  color: string;
}

const BLUE: BadgeColors = { background: "#1D3461", color: "#93C5FD" };
const AMBER: BadgeColors = { background: "#3b2b06", color: "#FBBF24" };
const GREEN: BadgeColors = { background: "#052e16", color: "#4ADE80" };
const RED: BadgeColors = { background: "#2d0a0a", color: "#F87171" };
const GRAY: BadgeColors = { background: "#1c1917", color: "#9CA3AF" };
const PURPLE: BadgeColors = { background: "#2e1065", color: "#C4B5FD" };

function Badge({ label, colors }: { label: string; colors: BadgeColors }) {
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 10px",
        borderRadius: 5,
        fontSize: 11,
        fontWeight: 600,
        whiteSpace: "nowrap",
        background: colors.background,
        color: colors.color,
      }}
    >
      {label}
    </span>
  );
}

// ─── Agreements ───────────────────────────────────────────────────────────────

export const AGREEMENT_STATUS_LABELS: Record<CooperationStatus, string> = {
  pending: "Заявку подано",
  awaiting_signature: "Очікує підписання",
  active: "Співпраця активна",
  rejected: "Відхилено",
  terminated: "Розірвано",
};

const AGREEMENT_STATUS_COLORS: Record<CooperationStatus, BadgeColors> = {
  pending: BLUE,
  awaiting_signature: AMBER,
  active: GREEN,
  rejected: RED,
  terminated: GRAY,
};

export function AgreementStatusBadge({ status }: { status: CooperationStatus }) {
  return (
    <Badge
      label={AGREEMENT_STATUS_LABELS[status] ?? status}
      colors={AGREEMENT_STATUS_COLORS[status] ?? GRAY}
    />
  );
}

// ─── Orders ───────────────────────────────────────────────────────────────────

export const ORDER_STATUS_LABELS: Record<MarketplaceOrderStatus, string> = {
  new: "Нове",
  confirmed: "Підтверджено",
  shipped: "Відвантажено",
  delivered: "Доставлено",
  cancelled: "Скасовано",
};

const ORDER_STATUS_COLORS: Record<MarketplaceOrderStatus, BadgeColors> = {
  new: BLUE,
  confirmed: AMBER,
  shipped: PURPLE,
  delivered: GREEN,
  cancelled: RED,
};

export function OrderStatusBadge({ status }: { status: MarketplaceOrderStatus }) {
  return (
    <Badge
      label={ORDER_STATUS_LABELS[status] ?? status}
      colors={ORDER_STATUS_COLORS[status] ?? GRAY}
    />
  );
}

// ─── Support tickets ──────────────────────────────────────────────────────────

export const TICKET_STATUS_LABELS: Record<SupportTicketStatus, string> = {
  open: "Відкритий",
  in_progress: "В роботі",
  resolved: "Вирішено",
  closed: "Закрито",
};

const TICKET_STATUS_COLORS: Record<SupportTicketStatus, BadgeColors> = {
  open: BLUE,
  in_progress: AMBER,
  resolved: GREEN,
  closed: GRAY,
};

export function TicketStatusBadge({ status }: { status: SupportTicketStatus }) {
  return (
    <Badge
      label={TICKET_STATUS_LABELS[status] ?? status}
      colors={TICKET_STATUS_COLORS[status] ?? GRAY}
    />
  );
}
