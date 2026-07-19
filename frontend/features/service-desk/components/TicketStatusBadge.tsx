"use client";

import { useTranslations } from "next-intl";
import type { TicketStatus } from "../types";
import { getTicketStatusLabel } from "../types";

interface Props {
  status: TicketStatus;
}

const STATUS_STYLES: Record<TicketStatus, { background: string; color: string; border: string }> = {
  open:        { background: "#1D3461", color: "#93C5FD", border: "#3B82F6" },
  in_progress: { background: "#1C1A07", color: "#FCD34D", border: "#B45309" },
  waiting:     { background: "#1C100A", color: "#FDBA74", border: "#C2410C" },
  resolved:    { background: "#052E16", color: "#86EFAC", border: "#15803D" },
  closed:      { background: "#111827", color: "#9CA3AF", border: "#374151" },
};

export function TicketStatusBadge({ status }: Props) {
  const t = useTranslations("Dashboard.serviceDesk.statuses");
  const s = STATUS_STYLES[status];
  return (
    <span
      style={{
        display: "inline-flex",
        alignItems: "center",
        padding: "2px 8px",
        borderRadius: 9999,
        fontSize: 11,
        fontWeight: 600,
        background: s.background,
        color: s.color,
        border: `1px solid ${s.border}`,
        whiteSpace: "nowrap",
      }}
    >
      {getTicketStatusLabel(t, status)}
    </span>
  );
}
