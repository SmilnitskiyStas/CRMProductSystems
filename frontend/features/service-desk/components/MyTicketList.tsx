"use client";

import { useTranslations } from "next-intl";
import { useMyTickets } from "../hooks/useTickets";
import type { TicketDto } from "../types";
import { TicketCard } from "./TicketCard";

interface Props {
  selectedId?: string;
  onSelect: (ticket: TicketDto) => void;
}

export function MyTicketList({ selectedId, onSelect }: Props) {
  const t = useTranslations("Dashboard.serviceDesk.myTicketList");
  const { data: tickets, isLoading } = useMyTickets();

  if (isLoading) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, padding: "20px 0" }}>
        {t("loading")}
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
        {t("emptyMessage")}
      </div>
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 4 }}>
        {t("countLabel", { count: tickets.length })}
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
