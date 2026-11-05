"use client";

import { useState } from "react";
import { Star } from "lucide-react";
import { useTranslations } from "next-intl";
import { useSubmitRating } from "../hooks/useChat";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { textareaStyle } from "@/components/ui/fieldStyles";

interface Props {
  sessionId: string;
  subject: string;
  onDone: () => void;
}

export function RatingModal({ sessionId, subject, onDone }: Props) {
  const t = useTranslations("Dashboard.chat.ratingModal");
  const [rating, setRating] = useState(0);
  const [hovered, setHovered] = useState(0);
  const [comment, setComment] = useState("");
  const submit = useSubmitRating();

  async function handleSubmit() {
    if (rating === 0) return;
    await submit.mutateAsync({ sessionId, req: { rating, comment: comment || undefined } });
    onDone();
  }

  return (
    <Modal title={t("title")} onClose={onDone} width={400}>
        <p style={{ color: "#6B7280", fontSize: 13, margin: "-14px 0 20px" }}>
          {subject}
        </p>

        {/* Closed notice */}
        <div style={{
          background: "#1F2937",
          borderRadius: 8,
          padding: "10px 14px",
          marginBottom: 20,
          color: "#9CA3AF",
          fontSize: 13,
        }}>
          {t("closedNotice")}
        </div>

        {/* Stars */}
        <div style={{ display: "flex", justifyContent: "center", gap: 10, marginBottom: 20 }}>
          {[1, 2, 3, 4, 5].map((star) => (
            <button
              key={star}
              onClick={() => setRating(star)}
              onMouseEnter={() => setHovered(star)}
              onMouseLeave={() => setHovered(0)}
              style={{ background: "none", border: "none", cursor: "pointer", padding: 4 }}
            >
              <Star
                size={32}
                fill={(hovered || rating) >= star ? "#FBBF24" : "none"}
                stroke={(hovered || rating) >= star ? "#FBBF24" : "#4B5563"}
              />
            </button>
          ))}
        </div>

        {/* Labels */}
        <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 20 }}>
          <span style={{ color: "#6B7280", fontSize: 11 }}>{t("poorLabel")}</span>
          <span style={{ color: "#6B7280", fontSize: 11 }}>{t("excellentLabel")}</span>
        </div>

        {/* Comment */}
        <textarea
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          placeholder={t("commentPlaceholder")}
          rows={3}
          style={{ ...textareaStyle, marginBottom: 16 }}
        />

        {/* Actions */}
        <div style={{ display: "flex", gap: 10 }}>
          <Btn
            onClick={handleSubmit}
            disabled={rating === 0 || submit.isPending}
            style={{ flex: 1, justifyContent: "center" }}
          >
            {submit.isPending ? t("savingButton") : t("submitButton")}
          </Btn>
          <Btn variant="ghost" onClick={onDone}>
            {t("skipButton")}
          </Btn>
        </div>
    </Modal>
  );
}
