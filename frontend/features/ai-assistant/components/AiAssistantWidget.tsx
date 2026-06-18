"use client";

import { useState, useRef, useEffect } from "react";
import { useAiAssistant } from "../hooks/useAiAssistant";
import type { AiAssistantContextSummary } from "../types";

interface ChatMessage {
  role: "user" | "assistant";
  text: string;
  context?: AiAssistantContextSummary;
}

export function AiAssistantWidget() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
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
            {
              role: "assistant",
              text: `Помилка: ${err.message ?? "AI сервіс недоступний"}`,
            },
          ]);
        },
      },
    );
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  }

  return (
    <div
      style={{
        background: "#161B26",
        border: "1px solid #1F2937",
        borderRadius: 12,
        display: "flex",
        flexDirection: "column",
        overflow: "hidden",
        minHeight: 340,
        maxHeight: 480,
      }}
    >
      {/* Header */}
      <div
        style={{
          padding: "14px 20px",
          borderBottom: "1px solid #1F2937",
          display: "flex",
          alignItems: "center",
          gap: 10,
        }}
      >
        <span style={{ fontSize: 18 }}>🤖</span>
        <div>
          <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, margin: 0 }}>
            AI Бізнес-Асистент
          </h2>
          <p style={{ color: "#4B5563", fontSize: 11, margin: 0 }}>
            Запитуй про залишки, замовлення, продажі та постачальників
          </p>
        </div>
      </div>

      {/* Chat area */}
      <div
        style={{
          flex: 1,
          overflowY: "auto",
          padding: "16px 20px",
          display: "flex",
          flexDirection: "column",
          gap: 12,
        }}
      >
        {messages.length === 0 && (
          <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", marginTop: 24 }}>
            Привіт! Запитай про стан магазину. Наприклад:
            <br />
            <em style={{ color: "#6B7280" }}>«Які товари закінчуються?»</em>
            <br />
            <em style={{ color: "#6B7280" }}>«Що продається найкраще цього тижня?»</em>
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
                padding: "10px 14px",
                borderRadius: msg.role === "user" ? "12px 12px 4px 12px" : "12px 12px 12px 4px",
                background: msg.role === "user" ? "#1D4ED8" : "#1F2937",
                color: "#E8EDF5",
                fontSize: 13,
                lineHeight: 1.55,
                whiteSpace: "pre-wrap",
              }}
            >
              {msg.text}
            </div>

            {msg.role === "assistant" && msg.context && (
              <ContextBadges context={msg.context} />
            )}
          </div>
        ))}

        {isPending && (
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: 8,
              color: "#4B5563",
              fontSize: 12,
            }}
          >
            <PulsingDots />
            <span>AI аналізує дані…</span>
          </div>
        )}

        <div ref={bottomRef} />
      </div>

      {/* Input area */}
      <div
        style={{
          borderTop: "1px solid #1F2937",
          padding: "12px 16px",
          display: "flex",
          gap: 10,
          alignItems: "flex-end",
        }}
      >
        <textarea
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Запитай AI (Enter — надіслати)"
          rows={1}
          style={{
            flex: 1,
            background: "#0D1117",
            border: "1px solid #374151",
            borderRadius: 8,
            color: "#E8EDF5",
            fontSize: 13,
            padding: "9px 12px",
            resize: "none",
            outline: "none",
            lineHeight: 1.5,
            fontFamily: "inherit",
          }}
          disabled={isPending}
        />
        <button
          onClick={handleSend}
          disabled={!input.trim() || isPending}
          style={{
            background: input.trim() && !isPending ? "#1D4ED8" : "#1F2937",
            border: "none",
            borderRadius: 8,
            color: input.trim() && !isPending ? "#fff" : "#4B5563",
            fontSize: 13,
            fontWeight: 600,
            padding: "9px 16px",
            cursor: input.trim() && !isPending ? "pointer" : "not-allowed",
            flexShrink: 0,
            transition: "background 0.15s",
          }}
        >
          Надіслати
        </button>
      </div>
    </div>
  );
}

function ContextBadges({ context }: { context: AiAssistantContextSummary }) {
  const badges = [
    { label: `${context.criticalStockBatchesCount} критичних партій`, color: "#EF4444" },
    { label: `${context.pendingOrdersCount} замовлень`, color: "#F59E0B" },
    { label: `${context.salesDaysCount} рядків продажів`, color: "#3B82F6" },
    { label: `${context.activeSuppliersCount} постачальників`, color: "#10B981" },
  ].filter((b) => {
    const num = parseInt(b.label);
    return num > 0;
  });

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
