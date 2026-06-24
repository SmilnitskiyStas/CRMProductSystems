"use client";

import { useState } from "react";
import { Star, X } from "lucide-react";
import { useSubmitRating } from "../hooks/useChat";

interface Props {
  sessionId: string;
  subject: string;
  onDone: () => void;
}

export function RatingModal({ sessionId, subject, onDone }: Props) {
  const [rating, setRating] = useState(0);
  const [hovered, setHovered] = useState(0);
  const [comment, setComment] = useState("");
  const submit = useSubmitRating();

  async function handleSubmit() {
    if (rating === 0) return;
    await submit.mutateAsync({ sessionId, req: { rating, comment: comment || undefined } });
    onDone();
  }

  const overlay: React.CSSProperties = {
    position: "fixed",
    inset: 0,
    background: "rgba(0,0,0,0.65)",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    zIndex: 9999,
  };

  const card: React.CSSProperties = {
    background: "#111827",
    border: "1px solid #1F2937",
    borderRadius: 12,
    padding: "28px 32px",
    width: 400,
    maxWidth: "90vw",
  };

  return (
    <div style={overlay}>
      <div style={card}>
        {/* Header */}
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 20 }}>
          <div>
            <h2 style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, margin: 0 }}>
              Оцініть підтримку
            </h2>
            <p style={{ color: "#6B7280", fontSize: 13, marginTop: 4, marginBottom: 0 }}>
              {subject}
            </p>
          </div>
          <button
            onClick={onDone}
            style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563", padding: 4 }}
          >
            <X size={18} />
          </button>
        </div>

        {/* Closed notice */}
        <div style={{
          background: "#1F2937",
          borderRadius: 8,
          padding: "10px 14px",
          marginBottom: 20,
          color: "#9CA3AF",
          fontSize: 13,
        }}>
          Чат завершено службою підтримки. Будь ласка, залиште оцінку.
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
          <span style={{ color: "#6B7280", fontSize: 11 }}>Погано</span>
          <span style={{ color: "#6B7280", fontSize: 11 }}>Відмінно</span>
        </div>

        {/* Comment */}
        <textarea
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          placeholder="Коментар (необов'язково)..."
          rows={3}
          style={{
            width: "100%",
            background: "#1F2937",
            border: "1px solid #374151",
            borderRadius: 8,
            color: "#E8EDF5",
            fontSize: 13,
            padding: "10px 12px",
            resize: "vertical",
            outline: "none",
            boxSizing: "border-box",
            marginBottom: 16,
          }}
        />

        {/* Actions */}
        <div style={{ display: "flex", gap: 10 }}>
          <button
            onClick={handleSubmit}
            disabled={rating === 0 || submit.isPending}
            style={{
              flex: 1,
              background: rating === 0 ? "#1F2937" : "#1D3461",
              border: `1px solid ${rating === 0 ? "#374151" : "#3B82F6"}`,
              borderRadius: 8,
              color: rating === 0 ? "#4B5563" : "#93C5FD",
              fontSize: 14,
              fontWeight: 600,
              padding: "10px 0",
              cursor: rating === 0 ? "not-allowed" : "pointer",
            }}
          >
            {submit.isPending ? "Збереження..." : "Надіслати оцінку"}
          </button>
          <button
            onClick={onDone}
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
            Пропустити
          </button>
        </div>
      </div>
    </div>
  );
}
