import { MessageSquare, User, MapPin } from "lucide-react";
import type { TicketDto } from "../types";
import { TICKET_CATEGORY_LABELS } from "../types";
import { TicketStatusBadge } from "./TicketStatusBadge";
import { PriorityBadge } from "./PriorityBadge";

interface Props {
  ticket: TicketDto;
  selected?: boolean;
  onClick: (ticket: TicketDto) => void;
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

export function TicketCard({ ticket, selected, onClick }: Props) {
  return (
    <div
      onClick={() => onClick(ticket)}
      style={{
        background: selected ? "#0D1B2E" : "#0D1117",
        border: `1px solid ${selected ? "#3B82F6" : "#1F2937"}`,
        borderRadius: 10,
        padding: "14px 16px",
        cursor: "pointer",
        transition: "border-color 0.15s, background 0.15s",
      }}
      onMouseEnter={(e) => {
        if (!selected) {
          (e.currentTarget as HTMLElement).style.borderColor = "#374151";
          (e.currentTarget as HTMLElement).style.background = "#111827";
        }
      }}
      onMouseLeave={(e) => {
        if (!selected) {
          (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
          (e.currentTarget as HTMLElement).style.background = "#0D1117";
        }
      }}
    >
      {/* Top row: number + badges */}
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8, flexWrap: "wrap" }}>
        <span style={{ color: "#4B5563", fontSize: 12, fontWeight: 600 }}>
          #{ticket.number}
        </span>
        <span
          style={{
            background: "#1F2937",
            color: "#9CA3AF",
            borderRadius: 4,
            padding: "1px 6px",
            fontSize: 11,
          }}
        >
          {TICKET_CATEGORY_LABELS[ticket.category]}
        </span>
        <PriorityBadge priority={ticket.priority} />
        <TicketStatusBadge status={ticket.status} />
      </div>

      {/* Title */}
      <div
        style={{
          color: "#E8EDF5",
          fontSize: 14,
          fontWeight: 600,
          marginBottom: 8,
          lineHeight: 1.4,
        }}
      >
        {ticket.title}
      </div>

      {/* Meta row */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 12,
          flexWrap: "wrap",
        }}
      >
        <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#6B7280", fontSize: 12 }}>
          <User size={12} />
          {ticket.createdByName}
        </span>

        {ticket.assignedToName && (
          <span style={{ color: "#6B7280", fontSize: 12 }}>
            → {ticket.assignedToName}
          </span>
        )}

        {ticket.locationName && (
          <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#6B7280", fontSize: 12 }}>
            <MapPin size={12} />
            {ticket.locationName}
          </span>
        )}

        <span style={{ color: "#4B5563", fontSize: 12 }}>
          {formatDate(ticket.createdAt)}
        </span>

        {ticket.commentCount > 0 && (
          <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#6B7280", fontSize: 12 }}>
            <MessageSquare size={12} />
            {ticket.commentCount}
          </span>
        )}
      </div>
    </div>
  );
}
