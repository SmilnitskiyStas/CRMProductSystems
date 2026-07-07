"use client";

import { useState } from "react";
import { Inbox, MessageCircle } from "lucide-react";
import { useSupplierChatSessions } from "../hooks/useSupplierCabinet";
import { SupplierClientChatPanel } from "./SupplierClientChatPanel";

/** Inbox of all supplier↔client chat threads, including ones with no review/task
 * (BUG-018 fix — previously only ClientsTab opened chats, so threads started by
 * clients who never left a review or got a task assigned were unreachable). */
export function ChatInboxTab() {
  const { data: sessions, isLoading, isError } = useSupplierChatSessions();
  const [chatClient, setChatClient] = useState<{ tenantId: string; tenantName: string } | null>(null);

  return (
    <div
      style={{
        background: "#111827",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "20px 24px",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 16 }}>
        <Inbox size={18} color="#60A5FA" />
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
          Повідомлення
        </h2>
        {sessions && (
          <span
            style={{
              padding: "2px 8px",
              borderRadius: 10,
              background: "#1F2937",
              color: "#9CA3AF",
              fontSize: 11,
            }}
          >
            {sessions.length}
          </span>
        )}
      </div>

      {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</div>}
      {isError && <div style={{ color: "#F87171", fontSize: 13 }}>Не вдалося завантажити повідомлення.</div>}

      {!isLoading && !isError && (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          {sessions?.map((session) => (
            <div
              key={session.id}
              onClick={() =>
                setChatClient({ tenantId: session.otherTenantId, tenantName: session.otherTenantName })
              }
              style={{
                background: "#0D1117",
                border: "1px solid #1F2937",
                borderRadius: 10,
                padding: "12px 16px",
                display: "flex",
                alignItems: "center",
                gap: 12,
                cursor: "pointer",
              }}
            >
              <MessageCircle size={16} color="#60A5FA" style={{ flexShrink: 0 }} />
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 4 }}>
                  {session.otherTenantName}
                </div>
                <div
                  style={{
                    color: "#6B7280",
                    fontSize: 12,
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap",
                  }}
                >
                  {session.lastMessage ?? "Немає повідомлень"}
                </div>
              </div>
              {session.lastMessageAt && (
                <div style={{ color: "#4B5563", fontSize: 11, flexShrink: 0 }}>
                  {new Date(session.lastMessageAt).toLocaleString("uk-UA", {
                    day: "2-digit",
                    month: "2-digit",
                    hour: "2-digit",
                    minute: "2-digit",
                  })}
                </div>
              )}
            </div>
          ))}

          {(!sessions || sessions.length === 0) && (
            <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "24px 0" }}>
              Повідомлень від клієнтів ще немає.
            </div>
          )}
        </div>
      )}

      {chatClient && (
        <SupplierClientChatPanel
          clientTenantId={chatClient.tenantId}
          clientTenantName={chatClient.tenantName}
          onClose={() => setChatClient(null)}
        />
      )}
    </div>
  );
}
