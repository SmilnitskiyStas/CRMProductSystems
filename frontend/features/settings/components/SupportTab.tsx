"use client";

import { useState } from "react";
import { Plus, ChevronLeft, Send } from "lucide-react";
import { useMyTickets, useMyTicket, useCreateTicket, useAddClientMessage, useMarkTicketRead } from "@/features/support/hooks/useSupport";
import {
  STATUS_LABELS,
  STATUS_COLORS,
  PRIORITY_LABELS,
  PRIORITY_COLORS,
  type TicketStatus,
  type SupportTicketDto,
} from "@/features/support/types";
import { Btn } from "@/components/ui/Btn";

export function SupportTab() {
  const [openTicketId, setOpenTicketId] = useState<string | null>(null);
  const [showCreate,   setShowCreate]   = useState(false);

  const { data: tickets, isLoading } = useMyTickets();
  const markRead = useMarkTicketRead();

  function openTicket(id: string, isUnread: boolean) {
    setOpenTicketId(id);
    if (isUnread) markRead.mutate(id);
  }

  if (showCreate) {
    return <CreateTicketForm onBack={() => setShowCreate(false)} onCreated={(id) => { setShowCreate(false); setOpenTicketId(id); }} />;
  }

  if (openTicketId) {
    return <ClientTicketThread ticketId={openTicketId} onBack={() => setOpenTicketId(null)} />;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>Служба підтримки</div>
          <div style={{ color: "#4B5563", fontSize: 12, marginTop: 2 }}>Відправте запит і отримайте відповідь від нашої команди</div>
        </div>
        <Btn icon={<Plus size={14} />} onClick={() => setShowCreate(true)}>
          Новий запит
        </Btn>
      </div>

      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 14 }}>Завантаження…</div>
      ) : !tickets?.length ? (
        <div
          style={{
            padding: "28px 0", textAlign: "center",
            color: "#4B5563", fontSize: 13,
          }}
        >
          Ще немає запитів. Натисніть «Новий запит» щоб звернутися до підтримки.
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          {tickets.map((t) => (
            <ClientTicketRow key={t.id} ticket={t} onClick={() => openTicket(t.id, t.isUnread)} />
          ))}
        </div>
      )}
    </div>
  );
}

function ClientTicketRow({ ticket, onClick }: { ticket: SupportTicketDto; onClick: () => void }) {
  return (
    <div
      onClick={onClick}
      style={{
        display: "flex", alignItems: "center", justifyContent: "space-between",
        background: ticket.isUnread ? "#0D1B2A" : "#111827",
        border: `1px solid ${ticket.isUnread ? "#1D4ED8" : "#1F2937"}`,
        borderRadius: 10, padding: "12px 16px", cursor: "pointer",
        transition: "border-color 0.15s",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        {ticket.isUnread && (
          <span style={{
            width: 8, height: 8, borderRadius: "50%",
            background: "#3B82F6", flexShrink: 0, display: "inline-block",
          }} />
        )}
        <div style={{ display: "flex", flexDirection: "column", gap: 3 }}>
          <span style={{
            color: "#E8EDF5", fontSize: 14,
            fontWeight: ticket.isUnread ? 700 : 500,
          }}>
            {ticket.subject}
          </span>
          <span style={{ color: "#4B5563", fontSize: 12 }}>
            {new Date(ticket.updatedAt).toLocaleDateString("uk-UA")} · {ticket.messages.length} повідомлень
          </span>
        </div>
      </div>
      <div style={{ display: "flex", alignItems: "center", gap: 8, flexShrink: 0 }}>
        {ticket.isUnread && (
          <span style={{ color: "#60A5FA", fontSize: 11, fontWeight: 600 }}>Нова відповідь</span>
        )}
        <StatusBadge status={ticket.status} />
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: TicketStatus }) {
  const color = STATUS_COLORS[status];
  return (
    <span
      style={{
        padding: "2px 10px", borderRadius: 20, fontSize: 11, fontWeight: 600,
        background: color.includes("blue")   ? "rgba(59,130,246,0.15)"  :
                    color.includes("yellow") ? "rgba(234,179,8,0.15)"   :
                    color.includes("green")  ? "rgba(74,222,128,0.15)"  :
                    "rgba(107,114,128,0.15)",
        border:     color.includes("blue")   ? "1px solid rgba(59,130,246,0.4)"  :
                    color.includes("yellow") ? "1px solid rgba(234,179,8,0.4)"   :
                    color.includes("green")  ? "1px solid rgba(74,222,128,0.4)"  :
                    "1px solid rgba(107,114,128,0.4)",
        color:      color.includes("blue")   ? "#93C5FD" :
                    color.includes("yellow") ? "#FDE68A" :
                    color.includes("green")  ? "#86EFAC" :
                    "#9CA3AF",
      }}
    >
      {STATUS_LABELS[status]}
    </span>
  );
}

function CreateTicketForm({ onBack, onCreated }: { onBack: () => void; onCreated: (id: string) => void }) {
  const create = useCreateTicket();
  const [subject,  setSubject]  = useState("");
  const [body,     setBody]     = useState("");
  const [priority, setPriority] = useState("normal");
  const [error,    setError]    = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      const ticket = await create.mutateAsync({ subject, body, priority });
      onCreated(ticket.id);
    } catch {
      setError("Помилка створення запиту");
    }
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <button onClick={onBack} style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}>
          <ChevronLeft size={18} />
        </button>
        <span style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>Новий запит до підтримки</span>
      </div>

      <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 12 }}>
        <input
          required
          placeholder="Тема"
          value={subject}
          onChange={(e) => setSubject(e.target.value)}
          style={fieldStyle}
        />
        <textarea
          required
          placeholder="Опишіть проблему детально…"
          value={body}
          onChange={(e) => setBody(e.target.value)}
          rows={5}
          style={{ ...fieldStyle, resize: "vertical" }}
        />
        <select value={priority} onChange={(e) => setPriority(e.target.value)} style={{ ...fieldStyle, appearance: "none" }}>
          <option value="low">Низький пріоритет</option>
          <option value="normal">Нормальний пріоритет</option>
          <option value="high">Високий пріоритет</option>
          <option value="urgent">Терміново</option>
        </select>

        {error && <div style={{ color: "#F87171", fontSize: 13 }}>{error}</div>}

        <button
          type="submit"
          disabled={create.isPending}
          style={{
            padding: "11px 0", borderRadius: 9,
            background: "linear-gradient(135deg, #3B82F6, #6366F1)",
            border: "none", color: "#fff", fontSize: 14, fontWeight: 600,
            cursor: create.isPending ? "not-allowed" : "pointer",
          }}
        >
          {create.isPending ? "Відправка…" : "Відправити"}
        </button>
      </form>
    </div>
  );
}

function ClientTicketThread({ ticketId, onBack }: { ticketId: string; onBack: () => void }) {
  const { data: ticket } = useMyTicket(ticketId);
  const addMsg           = useAddClientMessage(ticketId);
  const [text, setText]  = useState("");

  async function send() {
    if (!text.trim()) return;
    await addMsg.mutateAsync(text.trim());
    setText("");
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <button onClick={onBack} style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}>
          <ChevronLeft size={18} />
        </button>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{ticket?.subject}</div>
          {ticket && <StatusBadge status={ticket.status} />}
        </div>
      </div>

      <div
        style={{
          display: "flex", flexDirection: "column", gap: 10,
          maxHeight: 380, overflowY: "auto",
          background: "#080C10", borderRadius: 10, padding: "14px 16px",
        }}
      >
        {ticket?.messages.map((m) => (
          <div key={m.id} style={{ alignSelf: m.isProviderMessage ? "flex-start" : "flex-end", maxWidth: "75%" }}>
            <div
              style={{
                background: m.isProviderMessage ? "#111827" : "#1D3461",
                border: `1px solid ${m.isProviderMessage ? "#1F2937" : "#3B82F6"}`,
                borderRadius: 10, padding: "10px 14px",
              }}
            >
              <div style={{ color: "#E8EDF5", fontSize: 13 }}>{m.body}</div>
              <div style={{ color: "#4B5563", fontSize: 11, marginTop: 4 }}>
                {m.isProviderMessage ? "Підтримка" : "Ви"} ·{" "}
                {new Date(m.createdAt).toLocaleTimeString("uk-UA", { hour: "2-digit", minute: "2-digit" })}
              </div>
            </div>
          </div>
        ))}
      </div>

      {ticket?.status !== "resolved" && ticket?.status !== "closed" && (
        <div style={{ display: "flex", gap: 10 }}>
          <input
            value={text}
            onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && !e.shiftKey && send()}
            placeholder="Написати повідомлення…"
            style={{
              flex: 1, background: "#111827", border: "1px solid #1F2937",
              borderRadius: 9, padding: "10px 14px", color: "#E8EDF5", fontSize: 13, outline: "none",
            }}
          />
          <Btn icon={<Send size={15} />} onClick={send} disabled={!text.trim() || addMsg.isPending} style={{ padding: "0 16px" }}>
            {""}
          </Btn>
        </div>
      )}
    </div>
  );
}

const fieldStyle: React.CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "10px 14px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  width: "100%",
  boxSizing: "border-box",
};
