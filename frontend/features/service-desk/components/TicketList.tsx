"use client";

import { useState } from "react";
import { Search } from "lucide-react";
import { useTranslations } from "next-intl";
import { useTickets } from "../hooks/useTickets";
import type { TicketDto, TicketStatus, TicketPriority } from "../types";
import {
  TICKET_STATUSES,
  TICKET_PRIORITIES,
  getTicketStatusLabel,
  getTicketPriorityLabel,
} from "../types";
import { TicketCard } from "./TicketCard";

const PAGE_SIZE = 20;

interface Props {
  selectedId?: string;
  onSelect: (ticket: TicketDto) => void;
}

export function TicketList({ selectedId, onSelect }: Props) {
  const t = useTranslations("Dashboard.serviceDesk.ticketList");
  const tStatuses = useTranslations("Dashboard.serviceDesk.statuses");
  const tPriorities = useTranslations("Dashboard.serviceDesk.priorities");
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<TicketStatus | "">("");
  const [priority, setPriority] = useState<TicketPriority | "">("");
  const [search, setSearch] = useState("");

  const { data, isLoading } = useTickets({
    status: status || undefined,
    priority: priority || undefined,
    search: search || undefined,
    page,
    pageSize: PAGE_SIZE,
  });

  const tickets = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;
  const totalCount = data?.totalCount ?? 0;

  const selectStyle = {
    background: "#111827",
    border: "1px solid #1F2937",
    borderRadius: 6,
    color: "#9CA3AF",
    fontSize: 13,
    padding: "7px 10px",
    cursor: "pointer",
    outline: "none",
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      {/* Filters */}
      <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
        {/* Search */}
        <div style={{ position: "relative", flex: 1, minWidth: 180 }}>
          <Search
            size={14}
            style={{
              position: "absolute",
              left: 10,
              top: "50%",
              transform: "translateY(-50%)",
              color: "#4B5563",
            }}
          />
          <input
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            placeholder={t("searchPlaceholder")}
            style={{
              width: "100%",
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 6,
              color: "#E8EDF5",
              fontSize: 13,
              padding: "7px 10px 7px 30px",
              outline: "none",
              boxSizing: "border-box",
            }}
          />
        </div>

        {/* Status filter */}
        <select
          value={status}
          onChange={(e) => { setStatus(e.target.value as TicketStatus | ""); setPage(1); }}
          style={selectStyle}
        >
          <option value="">{t("allStatusesOption")}</option>
          {TICKET_STATUSES.map((s) => (
            <option key={s} value={s}>{getTicketStatusLabel(tStatuses, s)}</option>
          ))}
        </select>

        {/* Priority filter */}
        <select
          value={priority}
          onChange={(e) => { setPriority(e.target.value as TicketPriority | ""); setPage(1); }}
          style={selectStyle}
        >
          <option value="">{t("allPrioritiesOption")}</option>
          {TICKET_PRIORITIES.map((p) => (
            <option key={p} value={p}>{getTicketPriorityLabel(tPriorities, p)}</option>
          ))}
        </select>
      </div>

      {/* Count */}
      {!isLoading && (
        <div style={{ color: "#4B5563", fontSize: 12 }}>
          {t("countLabel", { count: totalCount })}
        </div>
      )}

      {/* List */}
      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "20px 0" }}>
          {t("loading")}
        </div>
      ) : tickets.length === 0 ? (
        <div
          style={{
            color: "#4B5563",
            fontSize: 13,
            padding: "40px 0",
            textAlign: "center",
          }}
        >
          {t("empty")}
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          {tickets.map((ticket) => (
            <TicketCard
              key={ticket.id}
              ticket={ticket}
              selected={ticket.id === selectedId}
              onClick={onSelect}
            />
          ))}
        </div>
      )}

      {/* Pagination */}
      {totalPages > 1 && (
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginTop: 4 }}>
          <button
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page === 1}
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 6,
              color: page === 1 ? "#374151" : "#9CA3AF",
              fontSize: 12,
              padding: "5px 12px",
              cursor: page === 1 ? "not-allowed" : "pointer",
            }}
          >
            {t("prevButton")}
          </button>
          <span style={{ color: "#4B5563", fontSize: 12 }}>
            {page} / {totalPages}
          </span>
          <button
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page === totalPages}
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 6,
              color: page === totalPages ? "#374151" : "#9CA3AF",
              fontSize: 12,
              padding: "5px 12px",
              cursor: page === totalPages ? "not-allowed" : "pointer",
            }}
          >
            {t("nextButton")}
          </button>
        </div>
      )}
    </div>
  );
}
