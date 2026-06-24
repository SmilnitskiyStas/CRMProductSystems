"use client";

import { useEffect, useRef, useState } from "react";
import { MessageCircle, Plus, Send, X } from "lucide-react";
import {
  useMyChats,
  useChatMessages,
  useCreateChat,
  useSendMessage,
} from "../hooks/useChat";
import { useMe } from "@/features/auth/hooks/useAuth";
import type { ChatSessionDto } from "../types";
import { RatingModal } from "./RatingModal";

export function ClientChatPanel() {
  const { data: me } = useMe();
  const { data: sessions = [] } = useMyChats();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [newText, setNewText] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  const [newSubject, setNewSubject] = useState("");
  const [newFirstMsg, setNewFirstMsg] = useState("");
  const [ratingSession, setRatingSession] = useState<ChatSessionDto | null>(null);

  const { data: messages = [] } = useChatMessages(selectedId);
  const createChat = useCreateChat();
  const sendMessage = useSendMessage(selectedId);
  const bottomRef = useRef<HTMLDivElement>(null);

  // Detect newly-closed sessions that haven't been rated yet → show modal
  const prevSessionsRef = useRef<ChatSessionDto[]>([]);
  useEffect(() => {
    const prev = prevSessionsRef.current;
    for (const s of sessions) {
      if (s.status === "closed" && s.rating === null) {
        const wasOpen = prev.find((p) => p.id === s.id && p.status === "open");
        if (wasOpen) {
          setRatingSession(s);
          break;
        }
      }
    }
    prevSessionsRef.current = sessions;
  }, [sessions]);

  // Also show modal for already-closed unrated session when user opens it
  useEffect(() => {
    if (!selectedId) return;
    const session = sessions.find((s) => s.id === selectedId);
    if (session && session.status === "closed" && session.rating === null) {
      setRatingSession(session);
    }
  }, [selectedId, sessions]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  async function handleSend() {
    if (!newText.trim() || !selectedId) return;
    await sendMessage.mutateAsync({ body: newText.trim() });
    setNewText("");
  }

  async function handleCreate() {
    if (!newSubject.trim() || !newFirstMsg.trim()) return;
    const session = await createChat.mutateAsync({
      subject: newSubject.trim(),
      firstMessage: newFirstMsg.trim(),
    });
    setSelectedId(session.id);
    setShowCreate(false);
    setNewSubject("");
    setNewFirstMsg("");
  }

  const selectedSession = sessions.find((s) => s.id === selectedId);

  const col: React.CSSProperties = {
    background: "#0F1623",
    border: "1px solid #1F2937",
    borderRadius: 10,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
  };

  return (
    <div style={{ display: "flex", gap: 16, height: "calc(100vh - 220px)", minHeight: 400 }}>
      {/* ── Session list ─────────────────────────────────────────────── */}
      <div style={{ ...col, width: 280, flexShrink: 0 }}>
        <div style={{
          padding: "14px 16px",
          borderBottom: "1px solid #1F2937",
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
        }}>
          <span style={{ color: "#E8EDF5", fontWeight: 600, fontSize: 14 }}>Мої чати</span>
          <button
            onClick={() => setShowCreate(true)}
            style={{
              background: "#1D3461",
              border: "1px solid #3B82F6",
              borderRadius: 6,
              color: "#93C5FD",
              fontSize: 12,
              fontWeight: 600,
              padding: "5px 10px",
              cursor: "pointer",
              display: "flex",
              alignItems: "center",
              gap: 5,
            }}
          >
            <Plus size={12} />
            Новий
          </button>
        </div>

        <div style={{ flex: 1, overflowY: "auto" }}>
          {sessions.length === 0 && (
            <div style={{ padding: 20, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
              Немає чатів
            </div>
          )}
          {sessions.map((s) => (
            <button
              key={s.id}
              onClick={() => setSelectedId(s.id)}
              style={{
                width: "100%",
                textAlign: "left",
                background: selectedId === s.id ? "#1F2937" : "transparent",
                border: "none",
                borderBottom: "1px solid #111827",
                padding: "12px 16px",
                cursor: "pointer",
              }}
            >
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
                <span style={{
                  color: "#E8EDF5",
                  fontSize: 13,
                  fontWeight: 500,
                  overflow: "hidden",
                  textOverflow: "ellipsis",
                  whiteSpace: "nowrap",
                  maxWidth: 160,
                }}>
                  {s.subject}
                </span>
                <span style={{
                  fontSize: 10,
                  fontWeight: 600,
                  padding: "2px 7px",
                  borderRadius: 10,
                  background: s.status === "open" ? "#14532D" : "#1F2937",
                  color: s.status === "open" ? "#4ADE80" : "#6B7280",
                  flexShrink: 0,
                  marginLeft: 6,
                }}>
                  {s.status === "open" ? "Відкрито" : "Закрито"}
                </span>
              </div>
              {s.lastMessage && (
                <p style={{
                  color: "#6B7280",
                  fontSize: 12,
                  margin: "4px 0 0",
                  overflow: "hidden",
                  textOverflow: "ellipsis",
                  whiteSpace: "nowrap",
                }}>
                  {s.lastMessage.body}
                </p>
              )}
              {s.status === "closed" && s.rating === null && (
                <p style={{ color: "#FBBF24", fontSize: 11, margin: "4px 0 0" }}>
                  ⭐ Залиште оцінку
                </p>
              )}
            </button>
          ))}
        </div>
      </div>

      {/* ── Message area ─────────────────────────────────────────────── */}
      <div style={{ ...col, flex: 1 }}>
        {!selectedSession ? (
          <div style={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "center", flexDirection: "column", gap: 12, color: "#4B5563" }}>
            <MessageCircle size={40} />
            <p style={{ margin: 0, fontSize: 14 }}>Оберіть чат або створіть новий</p>
          </div>
        ) : (
          <>
            {/* Header */}
            <div style={{
              padding: "12px 16px",
              borderBottom: "1px solid #1F2937",
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
            }}>
              <div>
                <span style={{ color: "#E8EDF5", fontWeight: 600, fontSize: 14 }}>
                  {selectedSession.subject}
                </span>
                {selectedSession.status === "closed" && (
                  <span style={{ color: "#6B7280", fontSize: 12, marginLeft: 10 }}>
                    • Чат закрито
                    {selectedSession.rating !== null && ` · Оцінка: ${selectedSession.rating}/5`}
                  </span>
                )}
              </div>
              <button
                onClick={() => setSelectedId(null)}
                style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563", padding: 4 }}
              >
                <X size={16} />
              </button>
            </div>

            {/* Messages */}
            <div style={{ flex: 1, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 10 }}>
              {messages.map((m) => {
                const isMe = me?.id ? m.senderUserId === me.id : false;
                return (
                  <div key={m.id} style={{ display: "flex", justifyContent: isMe ? "flex-end" : "flex-start" }}>
                    <div style={{
                      maxWidth: "70%",
                      background: isMe ? "#1D3461" : "#1F2937",
                      border: `1px solid ${isMe ? "#3B82F6" : "#374151"}`,
                      borderRadius: isMe ? "12px 12px 2px 12px" : "12px 12px 12px 2px",
                      padding: "8px 12px",
                    }}>
                      {!isMe && (
                        <p style={{ color: "#9CA3AF", fontSize: 11, margin: "0 0 3px", fontWeight: 600 }}>
                          {m.senderName}
                        </p>
                      )}
                      <p style={{ color: "#E8EDF5", fontSize: 13, margin: 0 }}>{m.body}</p>
                      <p style={{ color: "#4B5563", fontSize: 10, margin: "4px 0 0", textAlign: "right" }}>
                        {new Date(m.createdAt).toLocaleTimeString("uk-UA", { hour: "2-digit", minute: "2-digit" })}
                      </p>
                    </div>
                  </div>
                );
              })}
              <div ref={bottomRef} />
            </div>

            {/* Input */}
            {selectedSession.status === "open" ? (
              <div style={{
                padding: "12px 16px",
                borderTop: "1px solid #1F2937",
                display: "flex",
                gap: 10,
              }}>
                <input
                  value={newText}
                  onChange={(e) => setNewText(e.target.value)}
                  onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); handleSend(); } }}
                  placeholder="Написати повідомлення..."
                  style={{
                    flex: 1,
                    background: "#1F2937",
                    border: "1px solid #374151",
                    borderRadius: 8,
                    color: "#E8EDF5",
                    fontSize: 13,
                    padding: "9px 12px",
                    outline: "none",
                  }}
                />
                <button
                  onClick={handleSend}
                  disabled={!newText.trim() || sendMessage.isPending}
                  style={{
                    background: "#1D3461",
                    border: "1px solid #3B82F6",
                    borderRadius: 8,
                    color: "#93C5FD",
                    padding: "9px 14px",
                    cursor: "pointer",
                    display: "flex",
                    alignItems: "center",
                  }}
                >
                  <Send size={15} />
                </button>
              </div>
            ) : (
              <div style={{
                padding: "12px 16px",
                borderTop: "1px solid #1F2937",
                textAlign: "center",
                color: "#6B7280",
                fontSize: 13,
              }}>
                Чат закрито службою підтримки
                {selectedSession.rating === null && (
                  <button
                    onClick={() => setRatingSession(selectedSession)}
                    style={{
                      marginLeft: 12,
                      background: "none",
                      border: "1px solid #FBBF24",
                      borderRadius: 6,
                      color: "#FBBF24",
                      fontSize: 12,
                      padding: "3px 10px",
                      cursor: "pointer",
                    }}
                  >
                    ⭐ Оцінити
                  </button>
                )}
              </div>
            )}
          </>
        )}
      </div>

      {/* ── New chat dialog ──────────────────────────────────────────── */}
      {showCreate && (
        <div style={{
          position: "fixed",
          inset: 0,
          background: "rgba(0,0,0,0.6)",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          zIndex: 9998,
        }}>
          <div style={{
            background: "#111827",
            border: "1px solid #1F2937",
            borderRadius: 12,
            padding: "28px 32px",
            width: 420,
            maxWidth: "90vw",
          }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 20 }}>
              <h2 style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, margin: 0 }}>
                Новий чат підтримки
              </h2>
              <button
                onClick={() => setShowCreate(false)}
                style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563" }}
              >
                <X size={18} />
              </button>
            </div>

            <label style={{ color: "#9CA3AF", fontSize: 12, display: "block", marginBottom: 6 }}>
              Тема звернення
            </label>
            <input
              value={newSubject}
              onChange={(e) => setNewSubject(e.target.value)}
              placeholder="Коротко опишіть проблему..."
              style={{
                width: "100%",
                background: "#1F2937",
                border: "1px solid #374151",
                borderRadius: 8,
                color: "#E8EDF5",
                fontSize: 13,
                padding: "9px 12px",
                outline: "none",
                boxSizing: "border-box",
                marginBottom: 14,
              }}
            />

            <label style={{ color: "#9CA3AF", fontSize: 12, display: "block", marginBottom: 6 }}>
              Повідомлення
            </label>
            <textarea
              value={newFirstMsg}
              onChange={(e) => setNewFirstMsg(e.target.value)}
              placeholder="Деталі звернення..."
              rows={4}
              style={{
                width: "100%",
                background: "#1F2937",
                border: "1px solid #374151",
                borderRadius: 8,
                color: "#E8EDF5",
                fontSize: 13,
                padding: "9px 12px",
                outline: "none",
                resize: "vertical",
                boxSizing: "border-box",
                marginBottom: 20,
              }}
            />

            <div style={{ display: "flex", gap: 10 }}>
              <button
                onClick={handleCreate}
                disabled={!newSubject.trim() || !newFirstMsg.trim() || createChat.isPending}
                style={{
                  flex: 1,
                  background: "#1D3461",
                  border: "1px solid #3B82F6",
                  borderRadius: 8,
                  color: "#93C5FD",
                  fontSize: 14,
                  fontWeight: 600,
                  padding: "10px 0",
                  cursor: "pointer",
                }}
              >
                {createChat.isPending ? "Створення..." : "Почати чат"}
              </button>
              <button
                onClick={() => setShowCreate(false)}
                style={{
                  background: "transparent",
                  border: "1px solid #374151",
                  borderRadius: 8,
                  color: "#6B7280",
                  fontSize: 14,
                  padding: "10px 16px",
                  cursor: "pointer",
                }}
              >
                Скасувати
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ── Rating modal ─────────────────────────────────────────────── */}
      {ratingSession && (
        <RatingModal
          sessionId={ratingSession.id}
          subject={ratingSession.subject}
          onDone={() => setRatingSession(null)}
        />
      )}
    </div>
  );
}
