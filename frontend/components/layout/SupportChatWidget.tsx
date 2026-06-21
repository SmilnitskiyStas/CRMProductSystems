"use client";

import { useState } from "react";
import { X, LifeBuoy, Send, CheckCircle, ChevronRight, Clock, AlertCircle } from "lucide-react";
import { useMyTickets, useCreateTicket } from "@/features/service-desk/hooks/useTickets";
import { TICKET_STATUS_LABELS, TICKET_PRIORITY_LABELS } from "@/features/service-desk/types";
import type { TicketCategory, TicketPriority, TicketStatus } from "@/features/service-desk/types";
import { PROVIDER_TEAM } from "@/lib/roles";
import type { AppRole } from "@/lib/roles";

interface Props {
  userRole: AppRole | "";
  onClose: () => void;
}

type View = "inbox" | "new" | "success";

const STATUS_COLOR: Record<TicketStatus, string> = {
  open: "#60A5FA",
  in_progress: "#A78BFA",
  waiting: "#FBBF24",
  resolved: "#4ADE80",
  closed: "#4B5563",
};

const PRIORITY_DOT: Record<TicketPriority, string> = {
  low: "#4ADE80",
  medium: "#FBBF24",
  high: "#F97316",
  critical: "#EF4444",
};

const CATEGORIES: { value: TicketCategory; label: string }[] = [
  { value: "general", label: "Загальне" },
  { value: "technical", label: "Технічне" },
  { value: "billing", label: "Оплата" },
  { value: "feature_request", label: "Запит функції" },
  { value: "bug", label: "Помилка" },
];

const PRIORITIES: { value: TicketPriority; label: string }[] = [
  { value: "low", label: "Низький" },
  { value: "medium", label: "Середній" },
  { value: "high", label: "Високий" },
  { value: "critical", label: "Критичний" },
];

export function SupportChatWidget({ userRole, onClose }: Props) {
  const isProvider = PROVIDER_TEAM.has(userRole as AppRole);
  const [view, setView] = useState<View>("inbox");
  const [createdNumber, setCreatedNumber] = useState<number | null>(null);

  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [category, setCategory] = useState<TicketCategory>("general");
  const [priority, setPriority] = useState<TicketPriority>("medium");

  const { data: myTickets, isLoading } = useMyTickets();
  const createTicket = useCreateTicket();

  const openTickets = (myTickets ?? []).filter(
    (t) => t.status !== "closed" && t.status !== "resolved",
  );

  async function handleSubmit() {
    if (!title.trim() || !description.trim()) return;
    const result = await createTicket.mutateAsync({ title, description, category, priority });
    setCreatedNumber(result.number);
    setView("success");
  }

  function resetForm() {
    setTitle("");
    setDescription("");
    setCategory("general");
    setPriority("medium");
    setCreatedNumber(null);
  }

  const inputStyle: React.CSSProperties = {
    width: "100%",
    background: "#0D1117",
    border: "1px solid #1F2937",
    borderRadius: 8,
    padding: "9px 12px",
    color: "#E8EDF5",
    fontSize: 13,
    outline: "none",
    boxSizing: "border-box",
  };

  const labelStyle: React.CSSProperties = {
    color: "#9CA3AF",
    fontSize: 12,
    marginBottom: 5,
    display: "block",
  };

  return (
    <div
      style={{
        position: "fixed",
        bottom: 24,
        right: 24,
        width: 360,
        maxHeight: "calc(100vh - 100px)",
        background: "#111827",
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
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "14px 16px",
          borderBottom: "1px solid #1F2937",
          flexShrink: 0,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <div
            style={{
              width: 30,
              height: 30,
              borderRadius: 8,
              background: "linear-gradient(135deg, #1D3461, #1e3a8a)",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
            }}
          >
            <LifeBuoy size={15} color="#60A5FA" />
          </div>
          <div>
            <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>Підтримка</div>
            <div style={{ color: "#4B5563", fontSize: 11 }}>
              {isProvider ? "Ви — команда підтримки" : "Служба підтримки ShelfGuard"}
            </div>
          </div>
        </div>
        <button
          onClick={onClose}
          style={{
            background: "transparent",
            border: "none",
            color: "#4B5563",
            cursor: "pointer",
            padding: 4,
            borderRadius: 6,
            display: "flex",
            alignItems: "center",
          }}
        >
          <X size={16} />
        </button>
      </div>

      {/* Body */}
      <div style={{ flex: 1, overflowY: "auto", padding: 16 }}>

        {/* ── PROVIDER VIEW ── */}
        {isProvider && (
          <div style={{ textAlign: "center", padding: "24px 0" }}>
            <div
              style={{
                width: 56,
                height: 56,
                borderRadius: "50%",
                background: "#1D3461",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                margin: "0 auto 14px",
              }}
            >
              <LifeBuoy size={26} color="#60A5FA" />
            </div>
            <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 8 }}>
              Ви — агент підтримки
            </div>
            <div style={{ color: "#4B5563", fontSize: 12, lineHeight: 1.6, marginBottom: 20 }}>
              Всі тікети клієнтів доступні в Service Desk. Там ви можете призначати виконавців,
              відповідати та змінювати статуси.
            </div>
            <a
              href="/service-desk"
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: 6,
                background: "#1D3461",
                border: "1px solid #3B82F6",
                borderRadius: 8,
                padding: "9px 16px",
                color: "#93C5FD",
                fontSize: 13,
                fontWeight: 600,
                textDecoration: "none",
              }}
            >
              Відкрити Service Desk
              <ChevronRight size={14} />
            </a>
          </div>
        )}

        {/* ── TENANT: INBOX ── */}
        {!isProvider && view === "inbox" && (
          <>
            {isLoading ? (
              <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "20px 0" }}>
                Завантаження…
              </div>
            ) : openTickets.length === 0 ? (
              <div style={{ textAlign: "center", padding: "20px 0" }}>
                <CheckCircle size={32} style={{ color: "#4ADE80", margin: "0 auto 10px", display: "block" }} />
                <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, marginBottom: 4 }}>
                  Активних запитів немає
                </div>
                <div style={{ color: "#4B5563", fontSize: 12 }}>
                  Якщо виникне проблема — ми тут
                </div>
              </div>
            ) : (
              <>
                <div style={{ color: "#6B7280", fontSize: 11, fontWeight: 600, marginBottom: 10, textTransform: "uppercase", letterSpacing: "0.05em" }}>
                  Ваші активні запити ({openTickets.length})
                </div>
                <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                  {openTickets.slice(0, 5).map((ticket) => (
                    <div
                      key={ticket.id}
                      style={{
                        background: "#0D1117",
                        border: "1px solid #1F2937",
                        borderRadius: 10,
                        padding: "10px 12px",
                      }}
                    >
                      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 8, marginBottom: 6 }}>
                        <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, lineHeight: 1.4 }}>
                          {ticket.title}
                        </div>
                        <span
                          style={{
                            background: STATUS_COLOR[ticket.status] + "22",
                            color: STATUS_COLOR[ticket.status],
                            borderRadius: 6,
                            padding: "2px 7px",
                            fontSize: 11,
                            fontWeight: 600,
                            whiteSpace: "nowrap",
                            flexShrink: 0,
                          }}
                        >
                          {TICKET_STATUS_LABELS[ticket.status]}
                        </span>
                      </div>
                      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                        <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#4B5563", fontSize: 11 }}>
                          <span
                            style={{
                              width: 6,
                              height: 6,
                              borderRadius: "50%",
                              background: PRIORITY_DOT[ticket.priority],
                              display: "inline-block",
                            }}
                          />
                          {TICKET_PRIORITY_LABELS[ticket.priority]}
                        </span>
                        <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#4B5563", fontSize: 11 }}>
                          <Clock size={10} />
                          {new Date(ticket.createdAt).toLocaleDateString("uk-UA")}
                        </span>
                        {ticket.commentCount > 0 && (
                          <span style={{ color: "#4B5563", fontSize: 11 }}>
                            💬 {ticket.commentCount}
                          </span>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              </>
            )}
          </>
        )}

        {/* ── TENANT: NEW TICKET FORM ── */}
        {!isProvider && view === "new" && (
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
            <div>
              <label style={labelStyle}>Тема *</label>
              <input
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="Коротко опишіть проблему"
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>Опис *</label>
              <textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Деталі проблеми або запиту…"
                rows={4}
                style={{ ...inputStyle, resize: "vertical", minHeight: 90 }}
              />
            </div>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
              <div>
                <label style={labelStyle}>Категорія</label>
                <select
                  value={category}
                  onChange={(e) => setCategory(e.target.value as TicketCategory)}
                  style={{ ...inputStyle, cursor: "pointer" }}
                >
                  {CATEGORIES.map((c) => (
                    <option key={c.value} value={c.value}>{c.label}</option>
                  ))}
                </select>
              </div>
              <div>
                <label style={labelStyle}>Пріоритет</label>
                <select
                  value={priority}
                  onChange={(e) => setPriority(e.target.value as TicketPriority)}
                  style={{ ...inputStyle, cursor: "pointer" }}
                >
                  {PRIORITIES.map((p) => (
                    <option key={p.value} value={p.value}>{p.label}</option>
                  ))}
                </select>
              </div>
            </div>
            {createTicket.isError && (
              <div style={{ display: "flex", alignItems: "center", gap: 6, color: "#EF4444", fontSize: 12 }}>
                <AlertCircle size={13} />
                Не вдалося надіслати. Спробуйте ще раз.
              </div>
            )}
          </div>
        )}

        {/* ── TENANT: SUCCESS ── */}
        {!isProvider && view === "success" && (
          <div style={{ textAlign: "center", padding: "20px 0" }}>
            <CheckCircle
              size={40}
              style={{ color: "#4ADE80", margin: "0 auto 14px", display: "block" }}
            />
            <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, marginBottom: 6 }}>
              Запит надіслано!
            </div>
            <div style={{ color: "#4B5563", fontSize: 13, marginBottom: 4 }}>
              Тікет <span style={{ color: "#60A5FA", fontWeight: 600 }}>#{createdNumber}</span> створено
            </div>
            <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 24 }}>
              Ми відповімо якомога швидше
            </div>
            <a
              href="/service-desk"
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: 6,
                color: "#60A5FA",
                fontSize: 13,
                textDecoration: "none",
              }}
            >
              Переглянути всі запити <ChevronRight size={13} />
            </a>
          </div>
        )}
      </div>

      {/* Footer */}
      {!isProvider && (
        <div
          style={{
            borderTop: "1px solid #1F2937",
            padding: "12px 16px",
            flexShrink: 0,
          }}
        >
          {view === "inbox" && (
            <button
              onClick={() => { resetForm(); setView("new"); }}
              style={{
                width: "100%",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                gap: 7,
                background: "#1D3461",
                border: "1px solid #3B82F6",
                borderRadius: 8,
                padding: "10px 0",
                color: "#93C5FD",
                fontSize: 13,
                fontWeight: 600,
                cursor: "pointer",
              }}
            >
              <Send size={14} />
              Новий запит
            </button>
          )}

          {view === "new" && (
            <div style={{ display: "flex", gap: 8 }}>
              <button
                onClick={() => setView("inbox")}
                style={{
                  flex: "0 0 auto",
                  background: "transparent",
                  border: "1px solid #1F2937",
                  borderRadius: 8,
                  padding: "9px 14px",
                  color: "#6B7280",
                  fontSize: 13,
                  cursor: "pointer",
                }}
              >
                Назад
              </button>
              <button
                onClick={handleSubmit}
                disabled={!title.trim() || !description.trim() || createTicket.isPending}
                style={{
                  flex: 1,
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  gap: 7,
                  background: !title.trim() || !description.trim() ? "#1a2035" : "#1D3461",
                  border: `1px solid ${!title.trim() || !description.trim() ? "#1F2937" : "#3B82F6"}`,
                  borderRadius: 8,
                  padding: "9px 0",
                  color: !title.trim() || !description.trim() ? "#374151" : "#93C5FD",
                  fontSize: 13,
                  fontWeight: 600,
                  cursor: !title.trim() || !description.trim() ? "not-allowed" : "pointer",
                }}
              >
                <Send size={14} />
                {createTicket.isPending ? "Надсилання…" : "Надіслати"}
              </button>
            </div>
          )}

          {view === "success" && (
            <button
              onClick={() => { resetForm(); setView("inbox"); }}
              style={{
                width: "100%",
                background: "transparent",
                border: "1px solid #1F2937",
                borderRadius: 8,
                padding: "9px 0",
                color: "#6B7280",
                fontSize: 13,
                cursor: "pointer",
              }}
            >
              Закрити
            </button>
          )}
        </div>
      )}
    </div>
  );
}
