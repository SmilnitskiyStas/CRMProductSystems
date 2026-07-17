"use client";

// Client-side support panel for a supplier's marketplace page (TASK-318).
// Works WITHOUT a cooperation agreement: list of my tickets with this supplier,
// creating a ticket, and a ticket thread with replies.

import { useEffect, useRef, useState } from "react";
import { ChevronLeft, LifeBuoy, Send, X } from "lucide-react";
import { toast } from "sonner";
import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { useMe } from "@/features/auth/hooks/useAuth";
import {
  useCreateSupportTicket,
  useMySupportTickets,
  useSendSupportTicketMessage,
  useSupportTicket,
} from "../hooks/useCooperation";
import { TicketStatusBadge } from "./CooperationBadges";

interface Props {
  supplierId: string;
  supplierName: string;
  onClose: () => void;
}

type View = "list" | "create" | "thread";

export function SupportTicketsPanel({ supplierId, supplierName, onClose }: Props) {
  const t = useTranslations("Dashboard.marketplace.supportPanel");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: me } = useMe();
  const [view, setView] = useState<View>("list");
  const [selectedTicketId, setSelectedTicketId] = useState<string | null>(null);

  const { data: tickets = [], isLoading } = useMySupportTickets();
  const supplierTickets = tickets.filter((t) => t.supplierName === supplierName);

  const { data: ticket } = useSupportTicket(view === "thread" ? selectedTicketId : null);
  const sendMessage = useSendSupportTicketMessage(selectedTicketId);
  const createTicket = useCreateSupportTicket(supplierId);

  const [subject, setSubject] = useState("");
  const [message, setMessage] = useState("");
  const [reply, setReply] = useState("");
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [ticket?.messages?.length]);

  function handleCreate() {
    if (!subject.trim() || !message.trim()) return;
    createTicket.mutate(
      { subject: subject.trim(), message: message.trim() },
      {
        onSuccess: (created) => {
          toast.success(t("toastCreated"));
          setSubject("");
          setMessage("");
          setSelectedTicketId(created.id);
          setView("thread");
        },
        onError: (err) => toast.error(err.message),
      }
    );
  }

  function handleReply() {
    if (!reply.trim()) return;
    sendMessage.mutate(reply.trim(), {
      onSuccess: () => setReply(""),
      onError: (err) => toast.error(err.message),
    });
  }

  const inputStyle: React.CSSProperties = {
    width: "100%",
    boxSizing: "border-box",
    background: "#1F2937",
    border: "1px solid #374151",
    borderRadius: 8,
    color: "#E8EDF5",
    fontSize: 13,
    padding: "9px 12px",
    outline: "none",
    fontFamily: "inherit",
  };

  return (
    <div
      style={{
        position: "fixed",
        bottom: 24,
        right: 24,
        width: 420,
        height: 600,
        maxHeight: "calc(100vh - 100px)",
        background: "#0F1623",
        border: "1px solid #1F2937",
        borderRadius: 14,
        boxShadow: "0 20px 60px rgba(0,0,0,0.6)",
        display: "flex",
        flexDirection: "column",
        zIndex: 1000,
        overflow: "hidden",
      }}
    >
      {/* Header */}
      <div
        style={{
          padding: "14px 18px",
          borderBottom: "1px solid #1F2937",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 8, minWidth: 0 }}>
          {view !== "list" && (
            <button
              onClick={() => {
                setView("list");
                setSelectedTicketId(null);
              }}
              style={{ background: "none", border: "none", cursor: "pointer", color: "#6B7280", padding: 2, display: "flex" }}
            >
              <ChevronLeft size={18} />
            </button>
          )}
          <LifeBuoy size={16} color="#60A5FA" />
          <span style={{ color: "#E8EDF5", fontWeight: 600, fontSize: 14, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>
            {view === "thread" && ticket
              ? ticket.subject
              : t("headerFallback", { name: supplierName })}
          </span>
          {view === "thread" && ticket && <TicketStatusBadge status={ticket.status} />}
        </div>
        <button
          onClick={onClose}
          style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563", padding: 4 }}
        >
          <X size={18} />
        </button>
      </div>

      {/* List view */}
      {view === "list" && (
        <>
          <div style={{ flex: 1, overflowY: "auto", padding: 16 }}>
            {isLoading && (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
            )}
            {!isLoading && supplierTickets.length === 0 && (
              <div style={{ textAlign: "center", padding: "40px 0", color: "#4B5563", fontSize: 13 }}>
                {t("emptyList")}
              </div>
            )}
            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              {supplierTickets.map((ticketItem) => (
                <button
                  key={ticketItem.id}
                  onClick={() => {
                    setSelectedTicketId(ticketItem.id);
                    setView("thread");
                  }}
                  style={{
                    textAlign: "left",
                    background: "#111827",
                    border: "1px solid #1F2937",
                    borderRadius: 10,
                    padding: "12px 14px",
                    cursor: "pointer",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    gap: 12,
                  }}
                >
                  <div style={{ minWidth: 0 }}>
                    <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, marginBottom: 4, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                      {ticketItem.subject}
                    </div>
                    <div style={{ color: "#4B5563", fontSize: 11 }}>
                      {t("updatedLabel", {
                        date: new Date(ticketItem.updatedAt).toLocaleString(intlLocale, { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" }),
                      })}
                    </div>
                  </div>
                  <TicketStatusBadge status={ticketItem.status} />
                </button>
              ))}
            </div>
          </div>
          <div style={{ padding: "12px 16px", borderTop: "1px solid #1F2937", display: "flex", justifyContent: "flex-end" }}>
            <Btn onClick={() => setView("create")}>{t("newTicket")}</Btn>
          </div>
        </>
      )}

      {/* Create view */}
      {view === "create" && (
        <div style={{ flex: 1, overflowY: "auto", padding: 18, display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, marginBottom: 6 }}>
              {t("subjectLabel")} <span style={{ color: "#F87171" }}>*</span>
            </label>
            <input
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              placeholder={t("subjectPlaceholder")}
              style={inputStyle}
            />
          </div>
          <div>
            <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, marginBottom: 6 }}>
              {t("messageLabel")} <span style={{ color: "#F87171" }}>*</span>
            </label>
            <textarea
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              placeholder={t("messagePlaceholder")}
              rows={6}
              style={{ ...inputStyle, resize: "vertical" }}
            />
          </div>
          <div style={{ display: "flex", justifyContent: "flex-end", gap: 10 }}>
            <Btn variant="ghost" onClick={() => setView("list")}>
              {t("back")}
            </Btn>
            <Btn
              disabled={!subject.trim() || !message.trim() || createTicket.isPending}
              onClick={handleCreate}
            >
              {createTicket.isPending ? t("sending") : t("createTicket")}
            </Btn>
          </div>
        </div>
      )}

      {/* Thread view */}
      {view === "thread" && (
        <>
          <div style={{ flex: 1, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 10 }}>
            {(ticket?.messages ?? []).map((m) => {
              const isMe = me?.tenantId ? m.senderTenantId === me.tenantId : false;
              return (
                <div key={m.id} style={{ display: "flex", justifyContent: isMe ? "flex-end" : "flex-start" }}>
                  <div
                    style={{
                      maxWidth: "75%",
                      background: isMe ? "#1D3461" : "#1F2937",
                      border: `1px solid ${isMe ? "#3B82F6" : "#374151"}`,
                      borderRadius: isMe ? "12px 12px 2px 12px" : "12px 12px 12px 2px",
                      padding: "8px 12px",
                    }}
                  >
                    <p style={{ color: "#E8EDF5", fontSize: 13, margin: 0, whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
                      {m.body}
                    </p>
                    <p style={{ color: "#4B5563", fontSize: 10, margin: "4px 0 0", textAlign: "right" }}>
                      {new Date(m.createdAt).toLocaleString(intlLocale, { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" })}
                    </p>
                  </div>
                </div>
              );
            })}
            <div ref={bottomRef} />
          </div>
          <div style={{ padding: "12px 16px", borderTop: "1px solid #1F2937", display: "flex", gap: 10 }}>
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
              style={{ ...inputStyle, flex: 1, width: "auto" }}
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
  );
}
