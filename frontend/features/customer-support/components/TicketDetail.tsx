"use client";

import { useState } from "react";
import { X, Phone, User } from "lucide-react";
import { toast } from "sonner";
import { useTranslations, useLocale } from "next-intl";
import {
  useCustomerSupportTicket,
  useReplyToTicket,
  useUpdateTicketStatus,
} from "../hooks/useCustomerSupportTickets";
import { useUsers } from "@/features/users/hooks/useUsers";
import type { ConsumerSupportTicketMessageDto, TicketStatus } from "../types";
import { TICKET_STATUSES, getTicketStatusLabel } from "../types";
import { TicketStatusBadge } from "./TicketStatusBadge";

interface Props {
  ticketId: string;
  onClose: () => void;
}

function formatDateTime(iso: string, locale: string): string {
  return new Date(iso).toLocaleString(locale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function TicketDetail({ ticketId, onClose }: Props) {
  const t = useTranslations("Dashboard.customerSupport.ticketDetail");
  const tStatuses = useTranslations("Dashboard.customerSupport.statuses");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { data: ticket, isLoading } = useCustomerSupportTicket(ticketId);
  const { data: users } = useUsers();
  const replyMutation = useReplyToTicket();
  const statusMutation = useUpdateTicketStatus();

  const [replyBody, setReplyBody] = useState("");

  // ConsumerSupportTicketMessageDto only carries SenderUserId, not a name — resolve via the
  // same team-member list service-desk's own TicketDetail already uses for its assignee select.
  function staffName(userId: string): string {
    return users?.find((u) => u.id === userId)?.fullName ?? t("unknownStaff");
  }

  function handleReply() {
    if (!replyBody.trim()) return;
    replyMutation.mutate(
      { id: ticketId, body: replyBody.trim() },
      {
        onSuccess: () => {
          toast.success(t("toastReplySent"));
          setReplyBody("");
        },
        onError: (err) => toast.error(err.message),
      },
    );
  }

  function handleStatusChange(newStatus: TicketStatus) {
    statusMutation.mutate(
      { id: ticketId, status: newStatus },
      {
        onSuccess: () => toast.success(t("toastStatusUpdated")),
        onError: (err) => toast.error(err.message),
      },
    );
  }

  const selectStyle = {
    background: "#111827",
    border: "1px solid #1F2937",
    borderRadius: 6,
    color: "#9CA3AF",
    fontSize: 12,
    padding: "6px 10px",
    cursor: "pointer",
    outline: "none",
  };

  const labelStyle = {
    color: "#4B5563",
    fontSize: 11,
    fontWeight: 600,
    textTransform: "uppercase" as const,
    letterSpacing: "0.04em",
    marginBottom: 4,
    display: "block",
  };

  return (
    <>
      {/* Backdrop */}
      <div onClick={onClose} style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,0.5)", zIndex: 40 }} />

      {/* Sheet */}
      <div
        style={{
          position: "fixed",
          top: 0,
          right: 0,
          bottom: 0,
          width: "min(560px, 100vw)",
          background: "#0D1117",
          borderLeft: "1px solid #1F2937",
          zIndex: 50,
          display: "flex",
          flexDirection: "column",
          overflowY: "auto",
        }}
      >
        {/* Header */}
        <div
          style={{
            padding: "16px 20px",
            borderBottom: "1px solid #1F2937",
            display: "flex",
            alignItems: "flex-start",
            gap: 12,
          }}
        >
          <div style={{ flex: 1 }}>
            {!isLoading && ticket && (
              <div style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, lineHeight: 1.4 }}>
                {ticket.subject}
              </div>
            )}
          </div>
          <button
            onClick={onClose}
            style={{ background: "transparent", border: "none", color: "#4B5563", cursor: "pointer", padding: 4, flexShrink: 0 }}
          >
            <X size={18} />
          </button>
        </div>

        {isLoading && <div style={{ padding: 24, color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>}

        {ticket && (
          <div style={{ padding: 20, display: "flex", flexDirection: "column", gap: 20 }}>
            {/* Status badge */}
            <div>
              <TicketStatusBadge status={ticket.status} />
            </div>

            {/* Consumer info */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <span style={labelStyle}>
                  <User size={10} style={{ display: "inline", marginRight: 4 }} />
                  {t("consumerLabel")}
                </span>
                <div style={{ color: "#CBD5E1", fontSize: 13 }}>
                  {ticket.consumerName}
                  {ticket.customerName ? ` (${ticket.customerName})` : ""}
                </div>
              </div>
              <div>
                <span style={labelStyle}>
                  <Phone size={10} style={{ display: "inline", marginRight: 4 }} />
                  {t("phoneLabel")}
                </span>
                <div style={{ color: "#CBD5E1", fontSize: 13 }}>{ticket.consumerPhone}</div>
              </div>
            </div>

            {/* Status control */}
            <div
              style={{
                background: "#111827",
                border: "1px solid #1F2937",
                borderRadius: 10,
                padding: 16,
              }}
            >
              <span style={labelStyle}>{t("statusLabel")}</span>
              <select
                value={ticket.status}
                onChange={(e) => handleStatusChange(e.target.value as TicketStatus)}
                style={{ ...selectStyle, width: "100%" }}
                disabled={statusMutation.isPending}
              >
                {TICKET_STATUSES.map((s) => (
                  <option key={s} value={s}>{getTicketStatusLabel(tStatuses, s)}</option>
                ))}
              </select>
            </div>

            {/* Message thread */}
            <div>
              <div style={{ color: "#CBD5E1", fontSize: 14, fontWeight: 600, marginBottom: 12 }}>
                {t("messagesTitle")}
              </div>

              {(!ticket.messages || ticket.messages.length === 0) && (
                <div style={{ color: "#4B5563", fontSize: 13, marginBottom: 12 }}>{t("noMessages")}</div>
              )}

              <div style={{ display: "flex", flexDirection: "column", gap: 8, marginBottom: 16 }}>
                {ticket.messages?.map((msg) => (
                  <MessageItem
                    key={msg.id}
                    message={msg}
                    consumerName={ticket.consumerName}
                    staffName={msg.senderUserId ? staffName(msg.senderUserId) : ""}
                    locale={intlLocale}
                  />
                ))}
              </div>

              {/* Reply form */}
              <div
                style={{
                  background: "#111827",
                  border: "1px solid #1F2937",
                  borderRadius: 8,
                  padding: 12,
                  display: "flex",
                  flexDirection: "column",
                  gap: 8,
                }}
              >
                <textarea
                  value={replyBody}
                  onChange={(e) => setReplyBody(e.target.value)}
                  placeholder={t("replyPlaceholder")}
                  rows={3}
                  style={{
                    background: "#0D1117",
                    border: "1px solid #1F2937",
                    borderRadius: 6,
                    color: "#E8EDF5",
                    fontSize: 13,
                    padding: "8px 10px",
                    outline: "none",
                    resize: "vertical",
                    width: "100%",
                    boxSizing: "border-box",
                    fontFamily: "inherit",
                  }}
                />
                <button
                  onClick={handleReply}
                  disabled={!replyBody.trim() || replyMutation.isPending}
                  style={{
                    background: replyBody.trim() ? "#1D3461" : "#111827",
                    border: `1px solid ${replyBody.trim() ? "#3B82F6" : "#1F2937"}`,
                    borderRadius: 6,
                    color: replyBody.trim() ? "#93C5FD" : "#374151",
                    fontSize: 12,
                    fontWeight: 600,
                    padding: "6px 14px",
                    cursor: replyBody.trim() ? "pointer" : "not-allowed",
                    marginLeft: "auto",
                  }}
                >
                  {replyMutation.isPending ? t("sendingButton") : t("sendButton")}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </>
  );
}

function MessageItem({
  message,
  consumerName,
  staffName,
  locale,
}: {
  message: ConsumerSupportTicketMessageDto;
  consumerName: string;
  staffName: string;
  locale: string;
}) {
  const isStaff = !!message.senderUserId;
  return (
    <div
      style={{
        background: isStaff ? "#0a1628" : "#111827",
        border: `1px solid ${isStaff ? "#1D4ED8" : "#1F2937"}`,
        borderRadius: 8,
        padding: "10px 14px",
        marginLeft: isStaff ? 24 : 0,
        marginRight: isStaff ? 0 : 24,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6 }}>
        <span style={{ color: isStaff ? "#93C5FD" : "#CBD5E1", fontSize: 13, fontWeight: 600 }}>
          {isStaff ? staffName : consumerName}
        </span>
        <span style={{ color: "#4B5563", fontSize: 11, marginLeft: "auto" }}>
          {formatDateTime(message.createdAt, locale)}
        </span>
      </div>
      <div style={{ color: "#9CA3AF", fontSize: 13, lineHeight: 1.5, whiteSpace: "pre-wrap" }}>
        {message.body}
      </div>
    </div>
  );
}
