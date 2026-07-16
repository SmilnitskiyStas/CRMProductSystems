"use client";

import { useState, useRef, useEffect, KeyboardEvent } from "react";
import { useTranslations, useLocale } from "next-intl";
import {
  X,
  LifeBuoy,
  Send,
  ChevronRight,
  ChevronLeft,
  Plus,
  MessageCircle,
  Bot,
} from "lucide-react";
import {
  useMyChats,
  useChatMessages,
  useCreateChat,
  useSendMessage,
  useCloseChat,
} from "@/features/chat/hooks/useChat";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useAiAssistant } from "@/features/ai-assistant/hooks/useAiAssistant";
import type { AiAssistantContextSummary } from "@/features/ai-assistant/types";
import type { ChatSessionDto } from "@/features/chat/types";
import { PROVIDER_TEAM } from "@/lib/roles";
import type { AppRole } from "@/lib/roles";

export type ChatTab = "support" | "assistant";

interface Props {
  userRole: AppRole | "";
  onClose: () => void;
  initialTab?: ChatTab;
  activeTab: ChatTab;
  onTabChange: (tab: ChatTab) => void;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatTime(iso: string, locale: string): string {
  return new Date(iso).toLocaleTimeString(locale === "en" ? "en-US" : "uk-UA", {
    hour: "2-digit",
    minute: "2-digit",
  });
}

function formatDate(iso: string, locale: string): string {
  const d = new Date(iso);
  const today = new Date();
  if (d.toDateString() === today.toDateString()) return formatTime(iso, locale);
  return d.toLocaleDateString(locale === "en" ? "en-US" : "uk-UA", { day: "2-digit", month: "2-digit" });
}

// ── Session list item ─────────────────────────────────────────────────────────

function SessionItem({
  session,
  onClick,
}: {
  session: ChatSessionDto;
  onClick: () => void;
}) {
  const t = useTranslations("Common");
  const locale = useLocale();
  return (
    <div
      onClick={onClick}
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "10px 12px",
        cursor: "pointer",
        transition: "border-color 0.15s, background 0.15s",
      }}
      onMouseEnter={(e) => {
        (e.currentTarget as HTMLElement).style.borderColor = "#374151";
        (e.currentTarget as HTMLElement).style.background = "#111827";
      }}
      onMouseLeave={(e) => {
        (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
        (e.currentTarget as HTMLElement).style.background = "#0D1117";
      }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
          gap: 8,
          marginBottom: 4,
        }}
      >
        <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, lineHeight: 1.4, flex: 1 }}>
          {session.subject}
        </div>
        <div style={{ display: "flex", flexDirection: "column", alignItems: "flex-end", gap: 4, flexShrink: 0 }}>
          <span style={{ color: "#4B5563", fontSize: 11 }}>
            {formatDate(session.updatedAt, locale)}
          </span>
          {session.unreadCount > 0 && (
            <span
              style={{
                background: "#3B82F6",
                color: "#fff",
                borderRadius: 999,
                padding: "1px 6px",
                fontSize: 10,
                fontWeight: 700,
                minWidth: 18,
                textAlign: "center",
              }}
            >
              {session.unreadCount}
            </span>
          )}
        </div>
      </div>
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
      <div style={{ marginTop: 4 }}>
        <span
          style={{
            fontSize: 10,
            color: session.status === "open" ? "#4ADE80" : "#4B5563",
            background: session.status === "open" ? "rgba(74,222,128,0.1)" : "#1F2937",
            borderRadius: 4,
            padding: "1px 6px",
          }}
        >
          {session.status === "open" ? t("statusActive") : t("statusClosed")}
        </span>
      </div>
    </div>
  );
}

// ── Chat view (messages + input) ─────────────────────────────────────────────

function ChatView({
  session,
  currentUserId,
  onBack,
}: {
  session: ChatSessionDto;
  currentUserId: string;
  onBack: () => void;
}) {
  const t = useTranslations("Common");
  const tChat = useTranslations("Dashboard.supportChat");
  const locale = useLocale();
  const [text, setText] = useState("");
  const bottomRef = useRef<HTMLDivElement>(null);
  const { data: messages } = useChatMessages(session.id);
  const sendMessage = useSendMessage(session.id);
  const closeChat = useCloseChat();

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
    <>
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 8,
          padding: "10px 16px",
          borderBottom: "1px solid #1F2937",
          flexShrink: 0,
        }}
      >
        <button
          onClick={onBack}
          style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 2 }}
        >
          <ChevronLeft size={16} />
        </button>
        <div style={{ flex: 1 }}>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{session.subject}</div>
          <div style={{ color: "#4B5563", fontSize: 11 }}>
            {session.status === "open" ? t("statusActive") : t("statusClosed")}
          </div>
        </div>
        {session.status === "open" && (
          <button
            onClick={() => closeChat.mutateAsync(session.id)}
            disabled={closeChat.isPending}
            style={{
              background: "transparent",
              border: "1px solid #1F2937",
              borderRadius: 6,
              padding: "4px 10px",
              color: "#6B7280",
              fontSize: 11,
              cursor: "pointer",
            }}
          >
            {t("close")}
          </button>
        )}
      </div>

      <div
        style={{
          flex: 1,
          overflowY: "auto",
          padding: "12px 16px",
          display: "flex",
          flexDirection: "column",
          gap: 8,
        }}
      >
        {!messages?.length && (
          <div style={{ color: "#4B5563", fontSize: 12, textAlign: "center", padding: "20px 0" }}>
            {tChat("noMessages")}
          </div>
        )}
        {(messages ?? []).map((msg) => {
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
                <div style={{ color: "#6B7280", fontSize: 10, marginBottom: 2, paddingLeft: 4 }}>
                  {msg.senderName}
                </div>
              )}
              <div
                style={{
                  maxWidth: "80%",
                  background: isMe ? "#1D3461" : "#1F2937",
                  border: `1px solid ${isMe ? "#3B82F6" : "#374151"}`,
                  borderRadius: isMe ? "12px 12px 4px 12px" : "12px 12px 12px 4px",
                  padding: "8px 12px",
                  color: "#E8EDF5",
                  fontSize: 13,
                  lineHeight: 1.5,
                  wordBreak: "break-word",
                }}
              >
                {msg.body}
              </div>
              <div style={{ color: "#374151", fontSize: 10, marginTop: 2, paddingLeft: 4, paddingRight: 4 }}>
                {formatTime(msg.createdAt, locale)}
              </div>
            </div>
          );
        })}
        <div ref={bottomRef} />
      </div>

      {session.status === "open" && (
        <div
          style={{
            borderTop: "1px solid #1F2937",
            padding: "10px 12px",
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
            placeholder={tChat("typePlaceholder")}
            rows={2}
            style={{
              flex: 1,
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 8,
              padding: "8px 10px",
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
              padding: "9px 12px",
              color: text.trim() ? "#93C5FD" : "#374151",
              cursor: text.trim() ? "pointer" : "not-allowed",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              flexShrink: 0,
            }}
          >
            <Send size={15} />
          </button>
        </div>
      )}
    </>
  );
}

// ── New chat form ─────────────────────────────────────────────────────────────

function NewChatForm({ onBack }: { onBack: () => void }) {
  const t = useTranslations("Common");
  const tChat = useTranslations("Dashboard.supportChat");
  const [subject, setSubject] = useState("");
  const [firstMessage, setFirstMessage] = useState("");
  const createChat = useCreateChat();

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
    fontFamily: "inherit",
  };

  const labelStyle: React.CSSProperties = {
    color: "#9CA3AF",
    fontSize: 12,
    marginBottom: 5,
    display: "block",
  };

  const canSubmit = subject.trim() && firstMessage.trim() && !createChat.isPending;

  async function handleSubmit() {
    if (!canSubmit) return;
    await createChat.mutateAsync({ subject: subject.trim(), firstMessage: firstMessage.trim() });
    onBack();
  }

  return (
    <>
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 8,
          padding: "10px 16px",
          borderBottom: "1px solid #1F2937",
          flexShrink: 0,
        }}
      >
        <button
          onClick={onBack}
          style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 2 }}
        >
          <ChevronLeft size={16} />
        </button>
        <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{tChat("newChat")}</div>
      </div>

      <div style={{ flex: 1, overflowY: "auto", padding: "16px" }}>
        <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <div>
            <label style={labelStyle}>{tChat("subjectLabel")}</label>
            <input
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              placeholder={tChat("subjectPlaceholder")}
              style={inputStyle}
            />
          </div>
          <div>
            <label style={labelStyle}>{tChat("firstMessageLabel")}</label>
            <textarea
              value={firstMessage}
              onChange={(e) => setFirstMessage(e.target.value)}
              placeholder={tChat("firstMessagePlaceholder")}
              rows={5}
              style={{ ...inputStyle, resize: "vertical", minHeight: 100 }}
            />
          </div>
          {createChat.isError && (
            <div style={{ color: "#EF4444", fontSize: 12 }}>
              {tChat("createError")}
            </div>
          )}
        </div>
      </div>

      <div style={{ borderTop: "1px solid #1F2937", padding: "10px 12px", flexShrink: 0 }}>
        <button
          onClick={handleSubmit}
          disabled={!canSubmit}
          style={{
            width: "100%",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            gap: 7,
            background: canSubmit ? "#1D3461" : "#111827",
            border: `1px solid ${canSubmit ? "#3B82F6" : "#1F2937"}`,
            borderRadius: 8,
            padding: "10px 0",
            color: canSubmit ? "#93C5FD" : "#374151",
            fontSize: 13,
            fontWeight: 600,
            cursor: canSubmit ? "pointer" : "not-allowed",
          }}
        >
          <Send size={14} />
          {createChat.isPending ? t("sending") : t("send")}
        </button>
      </div>
    </>
  );
}

// ── AI Assistant tab content ──────────────────────────────────────────────────

interface AiMessage {
  role: "user" | "assistant";
  text: string;
  context?: AiAssistantContextSummary;
}

function AiAssistantTab() {
  const tChat = useTranslations("Dashboard.supportChat");
  const [messages, setMessages] = useState<AiMessage[]>([]);
  const [input, setInput] = useState("");
  const bottomRef = useRef<HTMLDivElement>(null);
  const { mutate: ask, isPending } = useAiAssistant();

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, isPending]);

  function handleSend() {
    const text = input.trim();
    if (!text || isPending) return;
    setMessages((prev) => [...prev, { role: "user", text }]);
    setInput("");
    ask(
      { message: text },
      {
        onSuccess: (data) => {
          setMessages((prev) => [
            ...prev,
            { role: "assistant", text: data.reply, context: data.context },
          ]);
        },
        onError: (err) => {
          setMessages((prev) => [
            ...prev,
            { role: "assistant", text: tChat("aiError", { message: err.message ?? tChat("aiUnavailable") }) },
          ]);
        },
      },
    );
  }

  function handleKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  }

  return (
    <>
      <div
        style={{
          flex: 1,
          overflowY: "auto",
          padding: "12px 16px",
          display: "flex",
          flexDirection: "column",
          gap: 10,
        }}
      >
        {messages.length === 0 && (
          <div style={{ color: "#4B5563", fontSize: 12, textAlign: "center", padding: "24px 8px", lineHeight: 1.8 }}>
            {tChat("greeting")}
            <br />
            <em style={{ color: "#6B7280" }}>{tChat("exampleQuery1")}</em>
            <br />
            <em style={{ color: "#6B7280" }}>{tChat("exampleQuery2")}</em>
          </div>
        )}

        {messages.map((msg, i) => (
          <div
            key={i}
            style={{
              display: "flex",
              flexDirection: "column",
              alignItems: msg.role === "user" ? "flex-end" : "flex-start",
              gap: 4,
            }}
          >
            <div
              style={{
                maxWidth: "85%",
                padding: "9px 13px",
                borderRadius: msg.role === "user" ? "12px 12px 4px 12px" : "12px 12px 12px 4px",
                background: msg.role === "user" ? "#1D4ED8" : "#1F2937",
                border: `1px solid ${msg.role === "user" ? "#2563EB" : "#374151"}`,
                color: "#E8EDF5",
                fontSize: 13,
                lineHeight: 1.55,
                whiteSpace: "pre-wrap",
                wordBreak: "break-word",
              }}
            >
              {msg.text}
            </div>
            {msg.role === "assistant" && msg.context && (
              <AiContextBadges context={msg.context} />
            )}
          </div>
        ))}

        {isPending && (
          <div style={{ display: "flex", alignItems: "center", gap: 8, color: "#4B5563", fontSize: 12 }}>
            <PulsingDots />
            <span>{tChat("aiThinking")}</span>
          </div>
        )}

        <div ref={bottomRef} />
      </div>

      <div
        style={{
          borderTop: "1px solid #1F2937",
          padding: "10px 12px",
          flexShrink: 0,
          display: "flex",
          gap: 8,
          alignItems: "flex-end",
        }}
      >
        <textarea
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={tChat("askPlaceholder")}
          rows={2}
          disabled={isPending}
          style={{
            flex: 1,
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 8,
            padding: "8px 10px",
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
          disabled={!input.trim() || isPending}
          style={{
            background: input.trim() && !isPending ? "#1D3461" : "#111827",
            border: `1px solid ${input.trim() && !isPending ? "#3B82F6" : "#1F2937"}`,
            borderRadius: 8,
            padding: "9px 12px",
            color: input.trim() && !isPending ? "#93C5FD" : "#374151",
            cursor: input.trim() && !isPending ? "pointer" : "not-allowed",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            flexShrink: 0,
          }}
        >
          <Send size={15} />
        </button>
      </div>
    </>
  );
}

function AiContextBadges({ context }: { context: AiAssistantContextSummary }) {
  const tChat = useTranslations("Dashboard.supportChat");
  const badges = [
    { label: tChat("criticalBatches", { count: context.criticalStockBatchesCount }), color: "#EF4444" },
    { label: tChat("pendingOrders", { count: context.pendingOrdersCount }), color: "#F59E0B" },
    { label: tChat("salesRows", { count: context.salesDaysCount }), color: "#3B82F6" },
    { label: tChat("activeSuppliers", { count: context.activeSuppliersCount }), color: "#10B981" },
  ].filter((b) => parseInt(b.label) > 0);

  if (badges.length === 0) return null;

  return (
    <div style={{ display: "flex", flexWrap: "wrap", gap: 4, maxWidth: "85%" }}>
      {badges.map((b) => (
        <span
          key={b.label}
          style={{
            fontSize: 10,
            padding: "2px 7px",
            borderRadius: 99,
            background: `${b.color}15`,
            color: b.color,
            border: `1px solid ${b.color}30`,
          }}
        >
          {b.label}
        </span>
      ))}
    </div>
  );
}

function PulsingDots() {
  return (
    <div style={{ display: "flex", gap: 3 }}>
      {[0, 1, 2].map((i) => (
        <div
          key={i}
          style={{
            width: 5,
            height: 5,
            borderRadius: "50%",
            background: "#4B5563",
            animation: `pulse 1.2s ease-in-out ${i * 0.2}s infinite`,
          }}
        />
      ))}
      <style>{`
        @keyframes pulse {
          0%, 80%, 100% { opacity: 0.3; transform: scale(0.8); }
          40% { opacity: 1; transform: scale(1); }
        }
      `}</style>
    </div>
  );
}

// ── Main widget ───────────────────────────────────────────────────────────────

type SupportView = "list" | "new" | "chat";

export function SupportChatWidget({ userRole, onClose, activeTab, onTabChange }: Props) {
  const t = useTranslations("Common");
  const tTopBar = useTranslations("Dashboard.topBar");
  const tChat = useTranslations("Dashboard.supportChat");
  const isProvider = PROVIDER_TEAM.has(userRole as AppRole);
  const { data: me } = useMe();
  const [view, setView] = useState<SupportView>("list");
  const [selectedSession, setSelectedSession] = useState<ChatSessionDto | null>(null);

  const { data: sessions, isLoading } = useMyChats();

  function openSession(s: ChatSessionDto) {
    setSelectedSession(s);
    setView("chat");
  }

  // Tab bar is hidden when inside a support sub-view (new/chat) to avoid confusion
  const showTabs = !(activeTab === "support" && (view === "new" || view === "chat"));

  return (
    <div
      style={{
        position: "fixed",
        bottom: 24,
        right: 24,
        width: 380,
        height: 540,
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
      {/* ── Top bar: title + close ── */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "12px 16px 0",
          flexShrink: 0,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <div
            style={{
              width: 28,
              height: 28,
              borderRadius: 7,
              background: activeTab === "assistant"
                ? "linear-gradient(135deg, #1e3a5f, #1D4ED8)"
                : "linear-gradient(135deg, #1D3461, #1e3a8a)",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              transition: "background 0.2s",
            }}
          >
            {activeTab === "assistant"
              ? <Bot size={14} color="#60A5FA" />
              : <LifeBuoy size={14} color="#60A5FA" />}
          </div>
          <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>
            {activeTab === "assistant" ? tTopBar("aiAssistant") : tTopBar("support")}
          </span>
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

      {/* ── Tab switcher ── */}
      {showTabs && (
        <div
          style={{
            display: "flex",
            gap: 4,
            padding: "10px 16px 0",
            flexShrink: 0,
          }}
        >
          {(["support", "assistant"] as ChatTab[]).map((tab) => {
            const isActive = activeTab === tab;
            return (
              <button
                key={tab}
                onClick={() => onTabChange(tab)}
                style={{
                  flex: 1,
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  gap: 6,
                  padding: "7px 0",
                  borderRadius: 8,
                  border: `1px solid ${isActive ? "#3B82F6" : "#1F2937"}`,
                  background: isActive ? "#1D3461" : "transparent",
                  color: isActive ? "#93C5FD" : "#6B7280",
                  fontSize: 12,
                  fontWeight: isActive ? 600 : 400,
                  cursor: "pointer",
                  transition: "all 0.15s",
                }}
              >
                {tab === "support" ? <LifeBuoy size={13} /> : <Bot size={13} />}
                {tab === "support" ? tTopBar("support") : tTopBar("myAssistant")}
              </button>
            );
          })}
        </div>
      )}

      <div style={{ borderBottom: "1px solid #1F2937", marginTop: 10, flexShrink: 0 }} />

      {/* ── Support tab content ── */}
      <div style={{ flex: 1, display: activeTab === "support" ? "flex" : "none", flexDirection: "column", overflow: "hidden" }}>
        {/* Provider view */}
        {isProvider && (
          <div style={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "center" }}>
            <div style={{ textAlign: "center", padding: "24px 24px" }}>
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
                {tChat("providerTitle")}
              </div>
              <div style={{ color: "#4B5563", fontSize: 12, lineHeight: 1.6, marginBottom: 20 }}>
                {tChat("providerBody")}
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
                {tChat("openServiceDesk")}
                <ChevronRight size={14} />
              </a>
            </div>
          </div>
        )}

        {/* Tenant: session list */}
        {!isProvider && view === "list" && (
          <>
            <div style={{ flex: 1, overflowY: "auto", padding: "12px 16px" }}>
              {isLoading ? (
                <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "20px 0" }}>
                  {t("loading")}
                </div>
              ) : !sessions?.length ? (
                <div style={{ textAlign: "center", padding: "32px 0" }}>
                  <MessageCircle
                    size={32}
                    style={{ color: "#374151", margin: "0 auto 10px", display: "block" }}
                  />
                  <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, marginBottom: 4 }}>
                    {tChat("noChatsTitle")}
                  </div>
                  <div style={{ color: "#4B5563", fontSize: 12 }}>
                    {tChat("noChatsBody")}
                  </div>
                </div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                  {sessions.map((s) => (
                    <SessionItem
                      key={s.id}
                      session={s}
                      onClick={() => openSession(s)}
                    />
                  ))}
                </div>
              )}
            </div>

            <div style={{ borderTop: "1px solid #1F2937", padding: "10px 12px", flexShrink: 0 }}>
              <button
                onClick={() => setView("new")}
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
                <Plus size={14} />
                {tChat("newChat")}
              </button>
            </div>
          </>
        )}

        {/* Tenant: new chat form */}
        {!isProvider && view === "new" && (
          <NewChatForm onBack={() => setView("list")} />
        )}

        {/* Tenant: chat view */}
        {!isProvider && view === "chat" && selectedSession && me && (
          <ChatView
            session={selectedSession}
            currentUserId={me.id}
            onBack={() => { setView("list"); setSelectedSession(null); }}
          />
        )}
      </div>

      {/* ── AI Assistant tab content — always mounted to preserve message history ── */}
      <div style={{ flex: 1, display: activeTab === "assistant" ? "flex" : "none", flexDirection: "column", overflow: "hidden" }}>
        <AiAssistantTab />
      </div>
    </div>
  );
}
