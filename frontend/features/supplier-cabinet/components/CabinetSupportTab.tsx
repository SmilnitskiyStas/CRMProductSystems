"use client";

// Тікети підтримки в кабінеті постачальника (TASK-318): список звернень
// клієнтів + тред з відповідями і зміною статусу.

import { useEffect, useRef, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Send } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { useMe } from "@/features/auth/hooks/useAuth";
import { TicketStatusBadge } from "@/features/marketplace/components/CooperationBadges";
import type { SupportTicketStatus } from "@/features/marketplace/types";
import {
  useAddCabinetTicketMessage,
  useCabinetSupportTicket,
  useCabinetSupportTickets,
  useUpdateCabinetTicketStatus,
} from "../hooks/useCabinetCooperation";

const STATUS_OPTIONS: SupportTicketStatus[] = ["open", "in_progress", "resolved", "closed"];

function formatDate(iso: string, locale: string): string {
  return new Date(iso).toLocaleString(locale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function CabinetSupportTab() {
  const t = useTranslations("Dashboard.supplierCabinet.supportTab");
  const tTicketStatus = useTranslations("Dashboard.marketplace.ticketStatus");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: me } = useMe();
  const { data: tickets = [], isLoading } = useCabinetSupportTickets();
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const { data: ticket } = useCabinetSupportTicket(selectedId);
  const sendMessage = useAddCabinetTicketMessage(selectedId);
  const updateStatus = useUpdateCabinetTicketStatus(selectedId);

  const [reply, setReply] = useState("");
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [ticket?.messages?.length]);

  function handleReply() {
    if (!reply.trim()) return;
    sendMessage.mutate(reply.trim(), {
      onSuccess: () => setReply(""),
      onError: (err) => toast.error(err.message),
    });
  }

  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>{t("loading")}</div>;
  }

  if (tickets.length === 0) {
    return (
      <div style={{ textAlign: "center", padding: "40px 0", color: "#4B5563", fontSize: 14 }}>
        {t("empty")}
      </div>
    );
  }

  return (
    <div style={{ display: "flex", gap: 20, alignItems: "stretch", minHeight: 480 }}>
      {/* Ticket list */}
      <div
        style={{
          width: 320,
          flexShrink: 0,
          display: "flex",
          flexDirection: "column",
          gap: 8,
          overflowY: "auto",
          maxHeight: 640,
        }}
      >
        {tickets.map((ticketItem) => {
          const selected = ticketItem.id === selectedId;
          return (
            <button
              key={ticketItem.id}
              onClick={() => setSelectedId(ticketItem.id)}
              style={{
                textAlign: "left",
                background: selected ? "#1D3461" : "#111827",
                border: `1px solid ${selected ? "#3B82F6" : "#1F2937"}`,
                borderRadius: 10,
                padding: "12px 14px",
                cursor: "pointer",
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  gap: 8,
                  marginBottom: 4,
                }}
              >
                <span
                  style={{
                    color: "#E8EDF5",
                    fontSize: 13,
                    fontWeight: 600,
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap",
                  }}
                >
                  {ticketItem.subject}
                </span>
                <TicketStatusBadge status={ticketItem.status} />
              </div>
              <div style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 2 }}>{ticketItem.clientName}</div>
              <div style={{ color: "#4B5563", fontSize: 11 }}>
                {t("updatedLabel", { date: formatDate(ticketItem.updatedAt, intlLocale) })}
              </div>
            </button>
          );
        })}
      </div>

      {/* Thread */}
      <div
        style={{
          flex: 1,
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          display: "flex",
          flexDirection: "column",
          overflow: "hidden",
          maxHeight: 640,
        }}
      >
        {!selectedId && (
          <div
            style={{
              flex: 1,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              color: "#4B5563",
              fontSize: 13,
            }}
          >
            {t("selectPrompt")}
          </div>
        )}

        {selectedId && ticket && (
          <>
            <div
              style={{
                padding: "12px 16px",
                borderBottom: "1px solid #1F2937",
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                gap: 12,
              }}
            >
              <div style={{ minWidth: 0 }}>
                <div
                  style={{
                    color: "#E8EDF5",
                    fontSize: 14,
                    fontWeight: 600,
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap",
                  }}
                >
                  {ticket.subject}
                </div>
                <div style={{ color: "#6B7280", fontSize: 12 }}>{ticket.clientName}</div>
                {ticket.orderNumber && (
                  <div style={{ color: "#F59E0B", fontSize: 11, marginTop: 2 }}>
                    {t("orderReference", { orderNumber: ticket.orderNumber })}
                  </div>
                )}
              </div>
              <select
                value={ticket.status}
                onChange={(e) =>
                  updateStatus.mutate(e.target.value as SupportTicketStatus, {
                    onSuccess: () => toast.success(t("statusUpdated")),
                    onError: (err) => toast.error(err.message),
                  })
                }
                disabled={updateStatus.isPending}
                style={{
                  background: "#1F2937",
                  border: "1px solid #374151",
                  borderRadius: 8,
                  color: "#E8EDF5",
                  fontSize: 12,
                  padding: "6px 10px",
                  outline: "none",
                  cursor: "pointer",
                }}
              >
                {STATUS_OPTIONS.map((s) => (
                  <option key={s} value={s}>
                    {tTicketStatus(s)}
                  </option>
                ))}
              </select>
            </div>

            <div
              style={{
                flex: 1,
                overflowY: "auto",
                padding: 16,
                display: "flex",
                flexDirection: "column",
                gap: 10,
              }}
            >
              {(ticket.messages ?? []).map((m) => {
                const isMe = me?.tenantId ? m.senderTenantId === me.tenantId : false;
                return (
                  <div
                    key={m.id}
                    style={{ display: "flex", justifyContent: isMe ? "flex-end" : "flex-start" }}
                  >
                    <div
                      style={{
                        maxWidth: "75%",
                        background: isMe ? "#1D3461" : "#1F2937",
                        border: `1px solid ${isMe ? "#3B82F6" : "#374151"}`,
                        borderRadius: isMe ? "12px 12px 2px 12px" : "12px 12px 12px 2px",
                        padding: "8px 12px",
                      }}
                    >
                      <p
                        style={{
                          color: "#E8EDF5",
                          fontSize: 13,
                          margin: 0,
                          whiteSpace: "pre-wrap",
                          wordBreak: "break-word",
                        }}
                      >
                        {m.body}
                      </p>
                      <p
                        style={{
                          color: "#4B5563",
                          fontSize: 10,
                          margin: "4px 0 0",
                          textAlign: "right",
                        }}
                      >
                        {new Date(m.createdAt).toLocaleString(intlLocale, {
                          day: "2-digit",
                          month: "2-digit",
                          hour: "2-digit",
                          minute: "2-digit",
                        })}
                      </p>
                    </div>
                  </div>
                );
              })}
              <div ref={bottomRef} />
            </div>

            <div
              style={{
                padding: "12px 16px",
                borderTop: "1px solid #1F2937",
                display: "flex",
                gap: 10,
              }}
            >
              <input
                value={reply}
                onChange={(e) => setReply(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter" && !e.shiftKey) {
                    e.preventDefault();
                    handleReply();
                  }
                }}
                placeholder={t("replyPlaceholder")}
                style={{
                  flex: 1,
                  background: "#1F2937",
                  border: "1px solid #374151",
                  borderRadius: 8,
                  color: "#E8EDF5",
                  fontSize: 13,
                  padding: "9px 12px",
                  outline: "none",
                  fontFamily: "inherit",
                }}
              />
              <Btn
                icon={<Send size={15} />}
                onClick={handleReply}
                disabled={!reply.trim() || sendMessage.isPending}
                style={{ padding: "9px 14px" }}
              >
                {""}
              </Btn>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
