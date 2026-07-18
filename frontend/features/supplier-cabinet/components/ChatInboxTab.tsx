"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Inbox, MessageCircle } from "lucide-react";
import { useSupplierChatSessions } from "../hooks/useSupplierCabinet";
import { SupplierClientChatPanel } from "./SupplierClientChatPanel";

/** Inbox of all supplier↔client chat threads, including ones with no review/task
 * (BUG-018 fix — previously only ClientsTab opened chats, so threads started by
 * clients who never left a review or got a task assigned were unreachable). */
export function ChatInboxTab() {
  const t = useTranslations("Dashboard.supplierCabinet.chatInboxTab");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
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
          {t("title")}
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

      {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>}
      {isError && <div style={{ color: "#F87171", fontSize: 13 }}>{t("errorLoad")}</div>}

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
                  {session.lastMessage ?? t("noMessages")}
                </div>
              </div>
              <div style={{ display: "flex", flexDirection: "column", alignItems: "flex-end", gap: 4, flexShrink: 0 }}>
                {session.lastMessageAt && (
                  <div style={{ color: "#4B5563", fontSize: 11 }}>
                    {new Date(session.lastMessageAt).toLocaleString(intlLocale, {
                      day: "2-digit",
                      month: "2-digit",
                      hour: "2-digit",
                      minute: "2-digit",
                    })}
                  </div>
                )}
                {session.unreadCount > 0 && (
                  <span
                    style={{
                      background: "#EF4444",
                      color: "#fff",
                      borderRadius: 999,
                      padding: "1px 6px",
                      fontSize: 10,
                      fontWeight: 700,
                      minWidth: 18,
                      textAlign: "center",
                    }}
                  >
                    {session.unreadCount > 9 ? "9+" : session.unreadCount}
                  </span>
                )}
              </div>
            </div>
          ))}

          {(!sessions || sessions.length === 0) && (
            <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "24px 0" }}>
              {t("empty")}
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
