"use client";

import { useEffect, useRef, useState } from "react";
import { MessageCircle, Send, Star, X } from "lucide-react";
import { toast } from "sonner";
import { useTranslations, useLocale } from "next-intl";
import {
  useSupplierChatMessages,
  useSendSupplierChatMessage,
  useMyChatParticipantRatings,
  useRateChatParticipant,
} from "../hooks/useMarketplace";
import { useMe } from "@/features/auth/hooks/useAuth";
import { Btn } from "@/components/ui/Btn";
import { StarRating } from "./StarRating";
import { RateEmployeeModal } from "./RateEmployeeModal";

interface Props {
  supplierId: string;
  supplierName: string;
  onClose: () => void;
}

/** Client-side chat panel for supplier↔client chat, opened from a supplier's
 * public marketplace page (TASK-314, Частина 2). Supplier-side participants can be
 * rated per person from their message-group header (TASK-696, Phase 8). */
export function SupplierChatPanel({ supplierId, supplierName, onClose }: Props) {
  const t = useTranslations("Dashboard.marketplace.chatPanel");
  const rt = useTranslations("Dashboard.marketplace.rateEmployee");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: me } = useMe();
  const { data: messages = [] } = useSupplierChatMessages(supplierId);
  const sendMessage = useSendSupplierChatMessage(supplierId);
  const { data: participantRatings = [] } = useMyChatParticipantRatings(supplierId);
  const rateParticipant = useRateChatParticipant(supplierId);
  const [text, setText] = useState("");
  const [rateTarget, setRateTarget] = useState<{ userId: string; name: string } | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  async function handleSend() {
    if (!text.trim()) return;
    await sendMessage.mutateAsync({ body: text.trim() });
    setText("");
  }

  const ratingFor = (userId: string) =>
    participantRatings.find((r) => r.supplierUserId === userId);

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
              {supplierName}
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
          {messages.map((m, i) => {
            const isMe = me?.tenantId ? m.senderTenantId === me.tenantId : false;
            const prev = messages[i - 1];
            const prevIsMe = prev
              ? me?.tenantId
                ? prev.senderTenantId === me.tenantId
                : false
              : null;
            const isFirstOfRun =
              !prev || prev.senderUserId !== m.senderUserId || prevIsMe !== isMe;
            const existingRating = ratingFor(m.senderUserId);

            return (
              <div
                key={m.id}
                style={{
                  display: "flex",
                  flexDirection: "column",
                  alignItems: isMe ? "flex-end" : "flex-start",
                  gap: 3,
                }}
              >
                {!isMe && isFirstOfRun && (
                  <div style={{ display: "flex", alignItems: "center", gap: 6, padding: "0 2px" }}>
                    <span style={{ color: "#9CA3AF", fontSize: 11, fontWeight: 600 }}>
                      {m.senderName}
                    </span>
                    {existingRating ? (
                      <button
                        type="button"
                        onClick={() => setRateTarget({ userId: m.senderUserId, name: m.senderName })}
                        title={t("rateParticipantTooltip")}
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: 4,
                          background: "none",
                          border: "none",
                          padding: 0,
                          cursor: "pointer",
                        }}
                      >
                        <StarRating value={existingRating.rating} size={10} />
                        <span style={{ color: "#60A5FA", fontSize: 10 }}>{t("editRating")}</span>
                      </button>
                    ) : (
                      <button
                        type="button"
                        onClick={() => setRateTarget({ userId: m.senderUserId, name: m.senderName })}
                        title={t("rateParticipant")}
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          justifyContent: "center",
                          background: "none",
                          border: "none",
                          padding: 2,
                          cursor: "pointer",
                          color: "#4B5563",
                        }}
                      >
                        <Star size={12} />
                      </button>
                    )}
                  </div>
                )}
                <div
                  style={{
                    maxWidth: "70%",
                    background: isMe ? "#1D3461" : "#1F2937",
                    border: `1px solid ${isMe ? "#3B82F6" : "#374151"}`,
                    borderRadius: isMe ? "12px 12px 2px 12px" : "12px 12px 12px 2px",
                    padding: "8px 12px",
                  }}
                >
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

        {rateTarget && (
          <RateEmployeeModal
            title={t("rateParticipant")}
            personName={rateTarget.name}
            initialRating={ratingFor(rateTarget.userId)?.rating ?? 0}
            initialComment={ratingFor(rateTarget.userId)?.comment ?? ""}
            isEdit={Boolean(ratingFor(rateTarget.userId))}
            pending={rateParticipant.isPending}
            onSubmit={(r, c) =>
              rateParticipant.mutate(
                { supplierUserId: rateTarget.userId, rating: r, comment: c },
                {
                  onSuccess: () => {
                    toast.success(rt("toastSuccess"));
                    setRateTarget(null);
                  },
                  onError: (err) => toast.error(err.message),
                },
              )
            }
            onClose={() => setRateTarget(null)}
          />
        )}
    </div>
  );
}
