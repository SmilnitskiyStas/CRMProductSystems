"use client";

import { useState, useRef, useEffect, KeyboardEvent } from "react";
import { useTranslations, useLocale } from "next-intl";
import {
  MessageCircle,
  Building2,
  Clock,
  Send,
  ChevronLeft,
  X,
} from "lucide-react";
import {
  useProviderChats,
  useProviderChatMessages,
  useProviderSendMessage,
  useProviderCloseChat,
} from "@/features/chat/hooks/useChat";
import { useMe } from "@/features/auth/hooks/useAuth";
import type { ChatSessionDto } from "@/features/chat/types";

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatTime(iso: string, locale: string): string {
  return new Date(iso).toLocaleTimeString(locale, {
    hour: "2-digit",
    minute: "2-digit",
  });
}

function formatDate(iso: string, locale: string): string {
  const d = new Date(iso);
  const today = new Date();
  if (d.toDateString() === today.toDateString()) return formatTime(iso, locale);
  return d.toLocaleDateString(locale, { day: "2-digit", month: "2-digit" });
}

// ── Session row ───────────────────────────────────────────────────────────────

function SessionRow({
  session,
  selected,
  onClick,
}: {
  session: ChatSessionDto;
  selected: boolean;
  onClick: () => void;
}) {
  const t = useTranslations("Dashboard.provider.chatSupportTab");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  return (
    <div
      onClick={onClick}
      style={{
        background: selected ? "#0D1B2E" : "#0D1117",
        border: `1px solid ${selected ? "#3B82F6" : "#1F2937"}`,
        borderRadius: 10,
        padding: "12px 14px",
        cursor: "pointer",
        transition: "border-color 0.15s, background 0.15s",
      }}
      onMouseEnter={(e) => {
        if (!selected) {
          (e.currentTarget as HTMLElement).style.borderColor = "#374151";
          (e.currentTarget as HTMLElement).style.background = "#111827";
        }
      }}
      onMouseLeave={(e) => {
        if (!selected) {
          (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
          (e.currentTarget as HTMLElement).style.background = "#0D1117";
        }
      }}
    >
      {/* Top row */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8, marginBottom: 6 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <span
            style={{
              display: "flex",
              alignItems: "center",
              gap: 4,
              background: "#1D3461",
              border: "1px solid #2D4A7A",
              borderRadius: 5,
              padding: "1px 7px",
              fontSize: 11,
              color: "#93C5FD",
            }}
          >
            <Building2 size={10} />
            {session.tenantName}
          </span>
          <span
            style={{
              fontSize: 10,
              color: session.status === "open" ? "#4ADE80" : "#4B5563",
              background: session.status === "open" ? "rgba(74,222,128,0.1)" : "#1F2937",
              borderRadius: 4,
              padding: "1px 6px",
            }}
          >
            {session.status === "open" ? t("statusOpen") : t("statusClosed")}
          </span>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 6, flexShrink: 0 }}>
          {session.unreadCount > 0 && (
            <span
              style={{
                background: "#3B82F6",
                color: "#fff",
                borderRadius: 999,
                padding: "1px 7px",
                fontSize: 10,
                fontWeight: 700,
              }}
            >
              {session.unreadCount}
            </span>
          )}
          <span style={{ display: "flex", alignItems: "center", gap: 3, color: "#4B5563", fontSize: 11 }}>
            <Clock size={10} />
            {formatDate(session.updatedAt, intlLocale)}
          </span>
        </div>
      </div>

      {/* Subject */}
      <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, marginBottom: 4 }}>
        {session.subject}
      </div>

      {/* Last message preview */}
      {session.lastMessage && (
        <div
          style={{
            color: "#6B7280",
            fontSize: 12,
            overflow: "hidden",
            textOverflow: "ellipsis",
            whiteSpace: "nowrap",
          }}
        >
          {session.lastMessage.senderName}: {session.lastMessage.body}
        </div>
      )}
    </div>
  );
}

// ── Chat panel (right side) ───────────────────────────────────────────────────

function ChatPanel({
  session,
  currentUserId,
  onClose,
}: {
  session: ChatSessionDto;
  currentUserId: string;
  onClose: () => void;
}) {
  const t = useTranslations("Dashboard.provider.chatSupportTab");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [text, setText] = useState("");
  const bottomRef = useRef<HTMLDivElement>(null);
  const { data: messages } = useProviderChatMessages(session.id);
  const sendMessage = useProviderSendMessage(session.id);
  const closeChat = useProviderCloseChat();

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  async function handleSend() {
    const body = text.trim();
    if (!body || sendMessage.isPending) return;
    setText("");
    await sendMessage.mutateAsync({ body });
  }

  function handleKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  }

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        height: "100%",
      }}
    >
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 10,
          padding: "14px 18px",
          borderBottom: "1px solid #1F2937",
          flexShrink: 0,
        }}
      >
        <div style={{ flex: 1 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 2 }}>
            <span
              style={{
                display: "flex",
                alignItems: "center",
                gap: 4,
                background: "#1D3461",
                border: "1px solid #2D4A7A",
                borderRadius: 5,
                padding: "1px 7px",
                fontSize: 11,
                color: "#93C5FD",
              }}
            >
              <Building2 size={10} />
              {session.tenantName}
            </span>
            <span
              style={{
                fontSize: 10,
                color: session.status === "open" ? "#4ADE80" : "#4B5563",
              }}
            >
              • {session.status === "open" ? t("statusOpen") : t("statusClosed")}
            </span>
          </div>
          <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>
            {session.subject}
          </div>
          {session.assignedAgentName && (
            <div style={{ color: "#6B7280", fontSize: 11, marginTop: 2 }}>
              {t("assignedAgentPrefix")} <span style={{ color: "#93C5FD" }}>{session.assignedAgentName}</span>
            </div>
          )}
        </div>

        <div style={{ display: "flex", gap: 8 }}>
          {session.status === "open" && (
            <button
              onClick={() => closeChat.mutateAsync(session.id)}
              disabled={closeChat.isPending}
              style={{
                background: "transparent",
                border: "1px solid #1F2937",
                borderRadius: 6,
                padding: "5px 12px",
                color: "#6B7280",
                fontSize: 12,
                cursor: "pointer",
              }}
            >
              {t("closeChatButton")}
            </button>
          )}
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}
          >
            <X size={16} />
          </button>
        </div>
      </div>

      {/* Messages */}
      <div
        style={{
          flex: 1,
          overflowY: "auto",
          padding: "16px 18px",
          display: "flex",
          flexDirection: "column",
          gap: 10,
        }}
      >
        {!messages?.length && (
          <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "20px 0" }}>
            {t("noMessagesYet")}
          </div>
        )}
        {(messages ?? []).map((msg) => {
          if (msg.isSystem) {
            return (
              <div key={msg.id} style={{ display: "flex", justifyContent: "center", padding: "2px 0" }}>
                <span style={{
                  background: "#1F2937",
                  border: "1px solid #374151",
                  borderRadius: 20,
                  color: "#9CA3AF",
                  fontSize: 11,
                  padding: "4px 14px",
                }}>
                  {msg.body}
                </span>
              </div>
            );
          }
          const isMe = msg.senderUserId === currentUserId;
          return (
            <div
              key={msg.id}
              style={{
                display: "flex",
                flexDirection: "column",
                alignItems: isMe ? "flex-end" : "flex-start",
              }}
            >
              {!isMe && (
                <div style={{ color: "#6B7280", fontSize: 11, marginBottom: 3, paddingLeft: 4 }}>
                  {msg.senderName}
                </div>
              )}
              <div
                style={{
                  maxWidth: "72%",
                  background: isMe ? "#1D3461" : "#1F2937",
                  border: `1px solid ${isMe ? "#3B82F6" : "#374151"}`,
                  borderRadius: isMe ? "12px 12px 4px 12px" : "12px 12px 12px 4px",
                  padding: "9px 13px",
                  color: "#E8EDF5",
                  fontSize: 13,
                  lineHeight: 1.5,
                  wordBreak: "break-word",
                }}
              >
                {msg.body}
              </div>
              <div style={{ color: "#374151", fontSize: 10, marginTop: 2, paddingLeft: 4, paddingRight: 4 }}>
                {formatTime(msg.createdAt, intlLocale)}
              </div>
            </div>
          );
        })}
        <div ref={bottomRef} />
      </div>

      {/* Input */}
      {session.status === "open" && (
        <div
          style={{
            borderTop: "1px solid #1F2937",
            padding: "12px 16px",
            flexShrink: 0,
            display: "flex",
            gap: 8,
            alignItems: "flex-end",
          }}
        >
          <textarea
            value={text}
            onChange={(e) => setText(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={t("replyPlaceholder")}
            rows={2}
            style={{
              flex: 1,
              background: "#080C10",
              border: "1px solid #1F2937",
              borderRadius: 8,
              padding: "9px 12px",
              color: "#E8EDF5",
              fontSize: 13,
              outline: "none",
              resize: "none",
              lineHeight: 1.5,
              fontFamily: "inherit",
            }}
          />
          <button
            onClick={handleSend}
            disabled={!text.trim() || sendMessage.isPending}
            style={{
              background: text.trim() ? "#1D3461" : "#111827",
              border: `1px solid ${text.trim() ? "#3B82F6" : "#1F2937"}`,
              borderRadius: 8,
              padding: "10px 14px",
              color: text.trim() ? "#93C5FD" : "#374151",
              cursor: text.trim() ? "pointer" : "not-allowed",
              display: "flex",
              alignItems: "center",
              gap: 6,
              fontSize: 13,
              fontWeight: 600,
              flexShrink: 0,
            }}
          >
            <Send size={14} />
            {t("sendButton")}
          </button>
        </div>
      )}
    </div>
  );
}

// ── Main component ────────────────────────────────────────────────────────────

export function ChatSupportTab() {
  const t = useTranslations("Dashboard.provider.chatSupportTab");
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<"" | "open" | "closed">("");

  const { data: sessions, isLoading } = useProviderChats();
  const { data: me } = useMe();

  const filteredSessions = (sessions ?? []).filter(
    (s) => !statusFilter || s.status === statusFilter,
  );

  const selectedSession = (sessions ?? []).find((s) => s.id === selectedSessionId) ?? null;

  const totalUnread = (sessions ?? []).reduce((acc, s) => acc + s.unreadCount, 0);

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "20px 24px",
        height: "calc(100vh - 230px)",
        display: "flex",
        flexDirection: "column",
      }}
    >
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          marginBottom: 20,
          flexWrap: "wrap",
          gap: 12,
          flexShrink: 0,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <MessageCircle size={18} color="#60A5FA" />
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
            {t("title")}
          </h2>
          {sessions && (
            <span style={{ color: "#4B5563", fontSize: 12 }}>({sessions.length})</span>
          )}
          {totalUnread > 0 && (
            <span
              style={{
                background: "#3B82F6",
                color: "#fff",
                borderRadius: 999,
                padding: "1px 8px",
                fontSize: 11,
                fontWeight: 700,
              }}
            >
              {t("newMessagesCount", { count: totalUnread })}
            </span>
          )}
        </div>

        {/* Status filter */}
        <div style={{ display: "flex", gap: 6 }}>
          {(
            [
              { value: "" as const, label: t("filterAll") },
              { value: "open" as const, label: t("filterOpen") },
              { value: "closed" as const, label: t("filterClosed") },
            ] as const
          ).map(({ value, label }) => (
            <button
              key={value}
              onClick={() => setStatusFilter(value)}
              style={{
                padding: "6px 12px",
                borderRadius: 7,
                fontSize: 12,
                cursor: "pointer",
                background: statusFilter === value ? "#1D3461" : "transparent",
                border: `1px solid ${statusFilter === value ? "#3B82F6" : "transparent"}`,
                color: statusFilter === value ? "#93C5FD" : "#6B7280",
              }}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      {/* Two-panel layout */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: selectedSession ? "340px 1fr" : "1fr",
          gap: 16,
          flex: 1,
          minHeight: 0,
        }}
      >
        {/* Left: session list */}
        <div style={{ display: "flex", flexDirection: "column", gap: 8, overflowY: "auto", minHeight: 0 }}>
          {isLoading ? (
            <div style={{ color: "#4B5563", fontSize: 14, padding: "20px 0" }}>{t("loading")}</div>
          ) : !filteredSessions.length ? (
            <div
              style={{
                color: "#4B5563",
                fontSize: 13,
                padding: "40px 0",
                textAlign: "center",
              }}
            >
              {t("noChats")}
            </div>
          ) : (
            filteredSessions.map((s) => (
              <SessionRow
                key={s.id}
                session={s}
                selected={s.id === selectedSessionId}
                onClick={() => setSelectedSessionId(s.id)}
              />
            ))
          )}
        </div>

        {/* Right: chat panel */}
        {selectedSession && me && (
          <ChatPanel
            session={selectedSession}
            currentUserId={me.id}
            onClose={() => setSelectedSessionId(null)}
          />
        )}
      </div>
    </div>
  );
}
