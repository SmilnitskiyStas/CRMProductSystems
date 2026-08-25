"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { useCustomerSupportTickets } from "../hooks/useCustomerSupportTickets";
import type { ConsumerSupportTicketDto, TicketStatus } from "../types";
import { TICKET_STATUSES, getTicketStatusLabel } from "../types";
import { TicketStatusBadge } from "./TicketStatusBadge";

const PAGE_SIZE = 30;
// GetInboxAsync (TASK-616) has no customerId filter param — when a `?customerId=` deep link is
// active, this widens the fetched page instead and filters client-side (see TicketList's
// customerIdFilter prop below). 200 is the backend's own PagedQuery.ClampedPageSize ceiling.
const CUSTOMER_FILTER_PAGE_SIZE = 200;

interface Props {
  selectedId?: string;
  onSelect: (ticket: ConsumerSupportTicketDto) => void;
  /** From the page's `?customerId=` query param (set by CustomerTicketsTab's deep link). */
  customerIdFilter?: string;
  onClearCustomerFilter?: () => void;
}

export function TicketList({ selectedId, onSelect, customerIdFilter, onClearCustomerFilter }: Props) {
  const t = useTranslations("Dashboard.customerSupport.ticketList");
  const tStatuses = useTranslations("Dashboard.customerSupport.statuses");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<TicketStatus | "">("");

  const { data, isLoading } = useCustomerSupportTickets({
    status: status || undefined,
    page: customerIdFilter ? 1 : page,
    pageSize: customerIdFilter ? CUSTOMER_FILTER_PAGE_SIZE : PAGE_SIZE,
  });

  const allTickets = data?.items ?? [];
  const tickets = customerIdFilter
    ? allTickets.filter((tk) => tk.customerId === customerIdFilter)
    : allTickets;
  const totalPages = customerIdFilter ? 1 : data?.totalPages ?? 1;
  const totalCount = customerIdFilter ? tickets.length : data?.totalCount ?? 0;

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
      {customerIdFilter && (
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            background: "#0a1628",
            border: "1px solid #1D4ED8",
            borderRadius: 8,
            padding: "8px 12px",
            color: "#60A5FA",
            fontSize: 12,
          }}
        >
          {t("customerFilterActive")}
          {onClearCustomerFilter && (
            <button
              onClick={onClearCustomerFilter}
              style={{
                marginLeft: "auto",
                background: "transparent",
                border: "none",
                color: "#60A5FA",
                cursor: "pointer",
                display: "flex",
                alignItems: "center",
                gap: 4,
                fontSize: 12,
              }}
            >
              <X size={12} />
              {t("clearFilter")}
            </button>
          )}
        </div>
      )}

      {/* Filters */}
      <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
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
      </div>

      {/* Count */}
      {!isLoading && (
        <div style={{ color: "#4B5563", fontSize: 12 }}>
          {t("countLabel", { count: totalCount })}
        </div>
      )}

      {/* List */}
      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "20px 0" }}>{t("loading")}</div>
      ) : tickets.length === 0 ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "40px 0", textAlign: "center" }}>
          {t("empty")}
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          {tickets.map((ticket) => {
            const selected = ticket.id === selectedId;
            return (
              <div
                key={ticket.id}
                onClick={() => onSelect(ticket)}
                style={{
                  background: selected ? "#111827" : "#0A1020",
                  border: `1px solid ${selected ? "#3B82F6" : "#1F2937"}`,
                  borderRadius: 10,
                  padding: "12px 14px",
                  cursor: "pointer",
                  display: "flex",
                  flexDirection: "column",
                  gap: 6,
                }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, flex: 1 }}>
                    {ticket.subject}
                  </span>
                  <TicketStatusBadge status={ticket.status} />
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                  <span style={{ color: "#9CA3AF", fontSize: 12 }}>
                    {ticket.consumerName}
                    {ticket.customerName ? ` · ${ticket.customerName}` : ""}
                  </span>
                  <span style={{ color: "#4B5563", fontSize: 11, marginLeft: "auto" }}>
                    {new Date(ticket.updatedAt).toLocaleString(intlLocale, {
                      day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit",
                    })}
                  </span>
                </div>
              </div>
            );
          })}
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
          <span style={{ color: "#4B5563", fontSize: 12 }}>{page} / {totalPages}</span>
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
