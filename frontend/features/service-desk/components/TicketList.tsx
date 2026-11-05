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
import { Pagination } from "@/components/ui/Pagination";
import { filterInputStyle } from "@/components/ui/fieldStyles";

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
            style={{ ...filterInputStyle, width: "100%", padding: "7px 10px 7px 30px", boxSizing: "border-box" }}
          />
        </div>

        {/* Status filter */}
        <select
          value={status}
          onChange={(e) => { setStatus(e.target.value as TicketStatus | ""); setPage(1); }}
          style={filterInputStyle}
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
          style={filterInputStyle}
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
        <Pagination page={page} totalPages={totalPages} totalCount={totalCount} onPageChange={setPage} />
      )}
    </div>
  );
}
