"use client";

import { useState } from "react";
import { MessageSquare, Send, ChevronLeft } from "lucide-react";
import {
  useAllTickets,
  useProviderTicket,
  useUpdateTicketStatus,
  useAddProviderMessage,
} from "@/features/support/hooks/useSupport";
import {
  STATUS_LABELS,
  STATUS_COLORS,
  PRIORITY_LABELS,
  PRIORITY_COLORS,
  type TicketStatus,
  type SupportTicketDto,
} from "@/features/support/types";

const STATUSES: TicketStatus[] = ["open", "in_progress", "resolved", "closed"];

export function ProviderSupportTab() {
  const [statusFilter, setStatusFilter] = useState<string | undefined>(undefined);
  const [openTicketId, setOpenTicketId]  = useState<string | null>(null);

  const { data: tickets, isLoading } = useAllTickets({ status: statusFilter });

  if (openTicketId) {
    return (
      <TicketThread
        ticketId={openTicketId}
        onBack={() => setOpenTicketId(null)}
      />
    );
  }

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "20px 24px",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <MessageSquare size={18} color="#60A5FA" />
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>Тікети підтримки</h2>
        </div>
        <div style={{ display: "flex", gap: 6 }}>
          <button
            onClick={() => setStatusFilter(undefined)}
            style={filterBtn(statusFilter === undefined)}
          >
            Усі
          </button>
          {STATUSES.map((s) => (
            <button key={s} onClick={() => setStatusFilter(s)} style={filterBtn(statusFilter === s)}>
              {STATUS_LABELS[s]}
            </button>
          ))}
        </div>
      </div>

      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 14 }}>Завантаження…</div>
      ) : !tickets?.length ? (
        <div style={{ color: "#4B5563", fontSize: 14 }}>Тікетів немає</div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          {tickets.map((t) => (
            <TicketRow key={t.id} ticket={t} onClick={() => setOpenTicketId(t.id)} />
          ))}
        </div>
      )}
    </div>
  );
}

function TicketRow({ ticket, onClick }: { ticket: SupportTicketDto; onClick: () => void }) {
  return (
    <div
      onClick={onClick}
      style={{
        display: "flex", alignItems: "center", justifyContent: "space-between",
        background: "#111827", border: "1px solid #1F2937",
        borderRadius: 10, padding: "12px 16px", cursor: "pointer",
      }}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: 3, minWidth: 0 }}>
        <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 500 }}>{ticket.subject}</span>
        <span style={{ color: "#4B5563", fontSize: 12 }}>
          {new Date(ticket.createdAt).toLocaleDateString("uk-UA")} · {ticket.messages.length} повідомлень
        </span>
      </div>
      <div style={{ display: "flex", alignItems: "center", gap: 8, flexShrink: 0 }}>
        <span style={badge(PRIORITY_COLORS[ticket.priority])}>
          {PRIORITY_LABELS[ticket.priority]}
        </span>
        <span style={badge(STATUS_COLORS[ticket.status])}>
          {STATUS_LABELS[ticket.status]}
        </span>
      </div>
    </div>
  );
}

function TicketThread({ ticketId, onBack }: { ticketId: string; onBack: () => void }) {
  const { data: ticket } = useProviderTicket(ticketId);
  const updateStatus = useUpdateTicketStatus(ticketId);
  const addMsg       = useAddProviderMessage(ticketId);
  const [text, setText] = useState("");

  async function send() {
    if (!text.trim()) return;
    await addMsg.mutateAsync(text.trim());
    setText("");
  }

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "20px 24px",
        display: "flex",
        flexDirection: "column",
        gap: 16,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <button
          onClick={onBack}
          style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}
        >
          <ChevronLeft size={18} />
        </button>
        {ticket ? (
          <div style={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "space-between" }}>
            <div>
              <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>{ticket.subject}</div>
              <div style={{ color: "#4B5563", fontSize: 12 }}>
                Тікет · {STATUS_LABELS[ticket.status]}
              </div>
            </div>
            <select
              value={ticket.status}
              onChange={(e) => updateStatus.mutate(e.target.value)}
              style={{
                background: "#111827", border: "1px solid #1F2937",
                borderRadius: 8, padding: "6px 10px",
                color: "#E8EDF5", fontSize: 12, cursor: "pointer",
              }}
            >
              {STATUSES.map((s) => (
                <option key={s} value={s}>{STATUS_LABELS[s]}</option>
              ))}
            </select>
          </div>
        ) : (
          <span style={{ color: "#4B5563", fontSize: 14 }}>Завантаження…</span>
        )}
      </div>

      {/* Messages */}
      <div
        style={{
          display: "flex", flexDirection: "column", gap: 10,
          maxHeight: 420, overflowY: "auto",
          background: "#080C10", borderRadius: 10, padding: "14px 16px",
        }}
      >
        {ticket?.messages.map((m) => (
          <div
            key={m.id}
            style={{
              alignSelf: m.isProviderMessage ? "flex-end" : "flex-start",
              maxWidth: "75%",
            }}
          >
            <div
              style={{
                background: m.isProviderMessage ? "#1D3461" : "#111827",
                border: `1px solid ${m.isProviderMessage ? "#3B82F6" : "#1F2937"}`,
                borderRadius: 10, padding: "10px 14px",
              }}
            >
              <div style={{ color: "#E8EDF5", fontSize: 13 }}>{m.body}</div>
              <div style={{ color: "#4B5563", fontSize: 11, marginTop: 4 }}>
                {m.isProviderMessage ? "Ви (провайдер)" : "Клієнт"} ·{" "}
                {new Date(m.createdAt).toLocaleTimeString("uk-UA", { hour: "2-digit", minute: "2-digit" })}
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* Input */}
      <div style={{ display: "flex", gap: 10 }}>
        <input
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && !e.shiftKey && send()}
          placeholder="Написати відповідь…"
          style={{
            flex: 1,
            background: "#111827", border: "1px solid #1F2937",
            borderRadius: 9, padding: "10px 14px",
            color: "#E8EDF5", fontSize: 13, outline: "none",
          }}
        />
        <button
          onClick={send}
          disabled={!text.trim() || addMsg.isPending}
          style={{
            background: "#1D3461", border: "1px solid #3B82F6",
            borderRadius: 9, padding: "0 16px",
            color: "#93C5FD", cursor: "pointer",
          }}
        >
          <Send size={15} />
        </button>
      </div>
    </div>
  );
}

function filterBtn(active: boolean): React.CSSProperties {
  return {
    padding: "6px 12px", borderRadius: 7, fontSize: 12,
    cursor: "pointer",
    background: active ? "#1D3461" : "transparent",
    border: `1px solid ${active ? "#3B82F6" : "transparent"}`,
    color: active ? "#93C5FD" : "#6B7280",
  };
}

function badge(cls: string): React.CSSProperties {
  const parts = cls.split(" ");
  const bg    = parts[0]?.replace("bg-", "").replace("/20", "") ?? "";
  return {
    padding: "2px 10px",
    borderRadius: 20,
    fontSize: 11,
    fontWeight: 600,
    background: cls.includes("blue")   ? "rgba(59,130,246,0.15)"  :
                cls.includes("yellow") ? "rgba(234,179,8,0.15)"   :
                cls.includes("green")  ? "rgba(74,222,128,0.15)"  :
                cls.includes("red")    ? "rgba(248,113,113,0.15)" :
                cls.includes("orange") ? "rgba(249,115,22,0.15)"  :
                "rgba(107,114,128,0.15)",
    border:     cls.includes("blue")   ? "1px solid rgba(59,130,246,0.4)"  :
                cls.includes("yellow") ? "1px solid rgba(234,179,8,0.4)"   :
                cls.includes("green")  ? "1px solid rgba(74,222,128,0.4)"  :
                cls.includes("red")    ? "1px solid rgba(248,113,113,0.4)" :
                cls.includes("orange") ? "1px solid rgba(249,115,22,0.4)"  :
                "1px solid rgba(107,114,128,0.4)",
    color:      cls.includes("blue")   ? "#93C5FD" :
                cls.includes("yellow") ? "#FDE68A" :
                cls.includes("green")  ? "#86EFAC" :
                cls.includes("red")    ? "#FCA5A5" :
                cls.includes("orange") ? "#FDBA74" :
                "#9CA3AF",
  };
}
