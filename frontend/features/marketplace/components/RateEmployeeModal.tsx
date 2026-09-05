"use client";

// Shared rating modal for the two buyer-side per-employee rating entry points (TASK-696,
// Phase 8): a delivered order's responsible manager, and a chat-thread participant. Styling
// mirrors ReviewModal. Rating 1–5 + optional comment; the caller owns the mutation.

import { useState } from "react";
import { useTranslations } from "next-intl";
import { StarRating } from "./StarRating";

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
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1100,
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 14,
          padding: "28px 32px",
          width: 440,
          maxWidth: "calc(100vw - 32px)",
          display: "flex",
          flexDirection: "column",
          gap: 20,
        }}
      >
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>{title}</h2>

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
            style={{
              width: "100%",
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 8,
              color: "#E8EDF5",
              fontSize: 13,
              padding: "10px 12px",
              resize: "vertical",
              outline: "none",
              boxSizing: "border-box",
            }}
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
          <button
            onClick={onClose}
            disabled={pending}
            style={{
              padding: "9px 20px",
              borderRadius: 8,
              border: "1px solid #1F2937",
              background: "transparent",
              color: "#6B7280",
              fontSize: 13,
              cursor: "pointer",
            }}
          >
            {t("cancel")}
          </button>
          <button
            onClick={handleSubmit}
            disabled={pending || rating === 0}
            style={{
              padding: "9px 20px",
              borderRadius: 8,
              border: "none",
              background: rating === 0 || pending ? "#1F2937" : "#1D4ED8",
              color: rating === 0 || pending ? "#4B5563" : "#E8EDF5",
              fontSize: 13,
              fontWeight: 600,
              cursor: rating === 0 || pending ? "not-allowed" : "pointer",
              transition: "background 0.1s",
            }}
          >
            {pending ? t("saving") : isEdit ? t("submitEdit") : t("submit")}
          </button>
        </div>
      </div>
    </div>
  );
}
