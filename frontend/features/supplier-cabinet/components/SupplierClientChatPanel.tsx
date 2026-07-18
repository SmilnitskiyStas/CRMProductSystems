"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { MessageCircle, Send, X } from "lucide-react";
import {
  useSupplierChatMessages,
  useSendSupplierChatMessage,
} from "../hooks/useSupplierCabinet";
import { useMe } from "@/features/auth/hooks/useAuth";
import { Btn } from "@/components/ui/Btn";

interface Props {
  clientTenantId: string;
  clientTenantName: string;
  onClose: () => void;
}

/** Modal chat panel for the supplier side of the supplier↔client chat (TASK-314). */
export function SupplierClientChatPanel({ clientTenantId, clientTenantName, onClose }: Props) {
  const t = useTranslations("Dashboard.supplierCabinet.clientChatPanel");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: me } = useMe();
  const { data: messages = [] } = useSupplierChatMessages(clientTenantId);
  const sendMessage = useSendSupplierChatMessage(clientTenantId);
  const [text, setText] = useState("");
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  async function handleSend() {
    if (!text.trim()) return;
    await sendMessage.mutateAsync({ body: text.trim() });
    setText("");
  }

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
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <MessageCircle size={16} color="#60A5FA" />
            <span style={{ color: "#E8EDF5", fontWeight: 600, fontSize: 14 }}>
              {clientTenantName}
            </span>
          </div>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563", padding: 4 }}
          >
            <X size={18} />
          </button>
        </div>

        {/* Messages */}
        <div style={{ flex: 1, overflowY: "auto", padding: 16, display: "flex", flexDirection: "column", gap: 10 }}>
          {messages.length === 0 && (
            <div style={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "center", color: "#4B5563", fontSize: 13 }}>
              {t("emptyMessages")}
            </div>
          )}
          {messages.map((m) => {
            const isMe = me?.tenantId ? m.senderTenantId === me.tenantId : false;
            return (
              <div key={m.id} style={{ display: "flex", justifyContent: isMe ? "flex-end" : "flex-start" }}>
                <div
                  style={{
                    maxWidth: "70%",
                    background: isMe ? "#1D3461" : "#1F2937",
                    border: `1px solid ${isMe ? "#3B82F6" : "#374151"}`,
                    borderRadius: isMe ? "12px 12px 2px 12px" : "12px 12px 12px 2px",
                    padding: "8px 12px",
                  }}
                >
                  {!isMe && (
                    <p style={{ color: "#9CA3AF", fontSize: 11, margin: "0 0 3px", fontWeight: 600 }}>
                      {m.senderName}
                    </p>
                  )}
                  <p style={{ color: "#E8EDF5", fontSize: 13, margin: 0, whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
                    {m.body}
                  </p>
                  <p style={{ color: "#4B5563", fontSize: 10, margin: "4px 0 0", textAlign: "right" }}>
                    {new Date(m.createdAt).toLocaleTimeString(intlLocale, { hour: "2-digit", minute: "2-digit" })}
                  </p>
                </div>
              </div>
            );
          })}
          <div ref={bottomRef} />
        </div>

        {/* Input */}
        <div style={{ padding: "12px 16px", borderTop: "1px solid #1F2937", display: "flex", gap: 10 }}>
          <input
            value={text}
            onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                handleSend();
              }
            }}
            placeholder={t("inputPlaceholder")}
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
          <Btn
            icon={<Send size={15} />}
            onClick={handleSend}
            disabled={!text.trim() || sendMessage.isPending}
            style={{ padding: "9px 14px" }}
          >
            {""}
          </Btn>
        </div>
    </div>
  );
}
