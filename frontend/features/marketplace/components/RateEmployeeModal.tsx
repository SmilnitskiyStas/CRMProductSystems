"use client";

// Shared rating modal for the two buyer-side per-employee rating entry points (TASK-696,
// Phase 8): a delivered order's responsible manager, and a chat-thread participant. Styling
// mirrors ReviewModal. Rating 1–5 + optional comment; the caller owns the mutation.

import { useState } from "react";
import { useTranslations } from "next-intl";
import { StarRating } from "./StarRating";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { textareaStyle } from "@/components/ui/fieldStyles";

interface Props {
  title: string;
  /** Name of the person being rated — shown read-only for context. */
  personName: string;
  initialRating?: number;
  initialComment?: string;
  pending: boolean;
  /** True when an existing rating is being changed (affects the submit label). */
  isEdit?: boolean;
  onSubmit: (rating: number, comment: string | undefined) => void;
  onClose: () => void;
}

export function RateEmployeeModal({
  title,
  personName,
  initialRating = 0,
  initialComment = "",
  pending,
  isEdit = false,
  onSubmit,
  onClose,
}: Props) {
  const t = useTranslations("Dashboard.marketplace.rateEmployee");
  const [rating, setRating] = useState(initialRating);
  const [comment, setComment] = useState(initialComment);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  function handleSubmit() {
    if (rating === 0) {
      setErrorMsg(t("errorSelectRating"));
      return;
    }
    setErrorMsg(null);
    onSubmit(rating, comment.trim() || undefined);
  }

  return (
    <Modal title={title} onClose={onClose} width={440}>
      <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
        <div>
          <div style={{ color: "#9CA3AF", fontSize: 13, marginBottom: 4 }}>{t("personLabel")}</div>
          <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{personName}</div>
        </div>

        <div>
          <div style={{ color: "#9CA3AF", fontSize: 13, marginBottom: 10 }}>{t("ratingLabel")}</div>
          <StarRating value={rating} size={28} interactive onChange={setRating} />
        </div>

        <div>
          <div style={{ color: "#9CA3AF", fontSize: 13, marginBottom: 8 }}>{t("commentLabel")}</div>
          <textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            rows={4}
            placeholder={t("commentPlaceholder")}
            style={textareaStyle}
          />
        </div>

        {errorMsg && (
          <div
            style={{
              padding: "10px 14px",
              background: "#1c0707",
              border: "1px solid #7f1d1d",
              borderRadius: 8,
              color: "#F87171",
              fontSize: 13,
            }}
          >
            {errorMsg}
          </div>
        )}

        <div style={{ display: "flex", gap: 10, justifyContent: "flex-end" }}>
          <Btn variant="ghost" onClick={onClose} disabled={pending}>
            {t("cancel")}
          </Btn>
          <Btn onClick={handleSubmit} disabled={pending || rating === 0}>
            {pending ? t("saving") : isEdit ? t("submitEdit") : t("submit")}
          </Btn>
        </div>
      </div>
    </Modal>
  );
}
