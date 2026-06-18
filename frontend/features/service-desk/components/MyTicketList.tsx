"use client";

import { useMyTickets } from "../hooks/useTickets";
import type { TicketDto } from "../types";
import { TicketCard } from "./TicketCard";

interface Props {
  selectedId?: string;
  onSelect: (ticket: TicketDto) => void;
}

export function MyTicketList({ selectedId, onSelect }: Props) {
  const { data: tickets, isLoading } = useMyTickets();

  if (isLoading) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, padding: "20px 0" }}>
        Завантаження...
      </div>
    );
  }

  if (!tickets || tickets.length === 0) {
    return (
      <div
        style={{
          color: "#4B5563",
          fontSize: 13,
          padding: "40px 0",
          textAlign: "center",
        }}
      >
        У вас немає тікетів. Натисніть &quot;+ Новий тікет&quot; щоб створити перший.
      </div>
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 4 }}>
        {tickets.length} тікет{tickets.length === 1 ? "" : tickets.length < 5 ? "и" : "ів"}
      </div>
      {tickets.map((ticket) => (
        <TicketCard
          key={ticket.id}
          ticket={ticket}
          selected={ticket.id === selectedId}
          onClick={onSelect}
        />
      ))}
    </div>
  );
}
