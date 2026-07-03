"use client";

import { useState } from "react";
import { useCreateReview } from "../hooks/useMarketplace";
import { StarRating } from "./StarRating";
import { ApiError } from "@/lib/api";

interface Props {
  supplierId: string;
  onClose: () => void;
}

export function ReviewModal({ supplierId, onClose }: Props) {
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState("");
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const { mutate, isPending } = useCreateReview(supplierId);

  function handleSubmit() {
    if (rating === 0) {
      setErrorMsg("Оберіть оцінку від 1 до 5 зірок.");
      return;
    }
    setErrorMsg(null);
    mutate(
      { rating, comment: comment.trim() || undefined },
      {
        onSuccess: () => {
          onClose();
        },
        onError: (err) => {
          if (err instanceof ApiError && err.status === 409) {
            // Duplicate — one review per tenant per supplier (TASK-285)
            setErrorMsg("Ви вже залишили відгук для цього постачальника.");
          } else if (err instanceof ApiError && err.status === 400) {
            // v4.1 backend guards (TASK-285)
            if (err.message.includes("your own supplier")) {
              setErrorMsg("Не можна залишати відгук на власний профіль постачальника.");
            } else if (err.message.includes("Supplier tenants")) {
              setErrorMsg("Постачальники не можуть залишати відгуки.");
            } else {
              setErrorMsg(err.message || "Некоректні дані відгуку.");
            }
          } else {
            setErrorMsg(
              err instanceof Error ? err.message : "Помилка при збереженні відгуку."
            );
          }
        },
      }
    );
  }

  return (
    /* Backdrop */
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      {/* Dialog */}
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
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
          Залишити відгук
        </h2>

        {/* Star selector */}
        <div>
          <div style={{ color: "#9CA3AF", fontSize: 13, marginBottom: 10 }}>
            Ваша оцінка
          </div>
          <StarRating value={rating} size={28} interactive onChange={setRating} />
        </div>

        {/* Comment */}
        <div>
          <div style={{ color: "#9CA3AF", fontSize: 13, marginBottom: 8 }}>
            Коментар (необов&apos;язково)
          </div>
          <textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            rows={4}
            placeholder="Ваш досвід роботи з постачальником…"
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

        {/* Error */}
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

        {/* Actions */}
        <div style={{ display: "flex", gap: 10, justifyContent: "flex-end" }}>
          <button
            onClick={onClose}
            disabled={isPending}
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
            Скасувати
          </button>
          <button
            onClick={handleSubmit}
            disabled={isPending || rating === 0}
            style={{
              padding: "9px 20px",
              borderRadius: 8,
              border: "none",
              background: rating === 0 || isPending ? "#1F2937" : "#1D4ED8",
              color: rating === 0 || isPending ? "#4B5563" : "#E8EDF5",
              fontSize: 13,
              fontWeight: 600,
              cursor: rating === 0 || isPending ? "not-allowed" : "pointer",
              transition: "background 0.1s",
            }}
          >
            {isPending ? "Збереження…" : "Надіслати відгук"}
          </button>
        </div>
      </div>
    </div>
  );
}
