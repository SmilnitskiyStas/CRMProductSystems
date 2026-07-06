"use client";

import { useState } from "react";
import { StarRating } from "@/features/marketplace/components/StarRating";
import {
  useCabinetReviews,
  useCabinetMetrics,
  useCabinetReviewStats,
  useReplyToReview,
} from "../hooks/useSupplierCabinet";
import type { CabinetReview } from "../types";

const PAGE_SIZE = 20;

function ReviewStatsWidget() {
  const { data: stats } = useCabinetReviewStats();

  if (!stats || stats.total === 0) return null;

  const pct = (n: number) => (stats.total > 0 ? (n / stats.total) * 100 : 0);

  return (
    <div
      style={{
        background: "#111827",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "16px 24px",
        marginBottom: 16,
      }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 20,
          flexWrap: "wrap",
          marginBottom: 12,
        }}
      >
        <span style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>
          {stats.averageRating != null ? stats.averageRating.toFixed(1) : "—"}
        </span>
        <span style={{ color: "#6B7280", fontSize: 13 }}>Всього: {stats.total}</span>
        <span style={{ color: "#34D399", fontSize: 13 }}>Позитивні: {stats.positive}</span>
        <span style={{ color: "#9CA3AF", fontSize: 13 }}>Нейтральні: {stats.neutral}</span>
        <span style={{ color: "#F87171", fontSize: 13 }}>Негативні: {stats.negative}</span>
      </div>
      <div
        style={{
          display: "flex",
          height: 8,
          borderRadius: 4,
          overflow: "hidden",
          background: "#1F2937",
        }}
      >
        <div style={{ width: `${pct(stats.positive)}%`, background: "#34D399" }} />
        <div style={{ width: `${pct(stats.neutral)}%`, background: "#9CA3AF" }} />
        <div style={{ width: `${pct(stats.negative)}%`, background: "#F87171" }} />
      </div>
    </div>
  );
}

function ReviewReplyBlock({ review }: { review: CabinetReview }) {
  const [isEditing, setIsEditing] = useState(false);
  const [text, setText] = useState(review.replyText ?? "");
  const replyMutation = useReplyToReview();

  const hasReply = !!review.replyText;

  if (!isEditing && hasReply) {
    return (
      <div
        style={{
          marginTop: 4,
          padding: "10px 14px",
          background: "#0B111C",
          border: "1px solid #1F2937",
          borderRadius: 8,
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: 4,
          }}
        >
          <span style={{ color: "#60A5FA", fontSize: 12, fontWeight: 600 }}>
            Відповідь постачальника:
          </span>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            {review.repliedAt && (
              <span style={{ color: "#4B5563", fontSize: 11 }}>
                {new Date(review.repliedAt).toLocaleDateString("uk-UA")}
              </span>
            )}
            <button
              onClick={() => {
                setText(review.replyText ?? "");
                setIsEditing(true);
              }}
              style={{
                background: "none",
                border: "none",
                color: "#6B7280",
                fontSize: 11,
                cursor: "pointer",
                padding: 0,
              }}
            >
              Редагувати
            </button>
          </div>
        </div>
        <p style={{ color: "#9CA3AF", fontSize: 13, margin: 0, lineHeight: 1.6 }}>
          {review.replyText}
        </p>
      </div>
    );
  }

  if (!isEditing) {
    return (
      <button
        onClick={() => setIsEditing(true)}
        style={{
          alignSelf: "flex-start",
          padding: "6px 14px",
          borderRadius: 6,
          border: "1px solid #1F2937",
          background: "transparent",
          color: "#60A5FA",
          fontSize: 12,
          cursor: "pointer",
        }}
      >
        Відповісти
      </button>
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      <textarea
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder="Ваша відповідь на відгук..."
        rows={3}
        style={{
          background: "#0B111C",
          border: "1px solid #1F2937",
          borderRadius: 8,
          padding: "10px 12px",
          color: "#E8EDF5",
          fontSize: 13,
          resize: "vertical",
          fontFamily: "inherit",
        }}
      />
      <div style={{ display: "flex", gap: 8 }}>
        <button
          disabled={!text.trim() || replyMutation.isPending}
          onClick={() =>
            replyMutation.mutate(
              { id: review.id, replyText: text.trim() },
              { onSuccess: () => setIsEditing(false) }
            )
          }
          style={{
            padding: "6px 16px",
            borderRadius: 6,
            border: "none",
            background: !text.trim() ? "#1F2937" : "#1D4ED8",
            color: "#E8EDF5",
            fontSize: 12,
            fontWeight: 600,
            cursor: !text.trim() || replyMutation.isPending ? "not-allowed" : "pointer",
          }}
        >
          {replyMutation.isPending ? "Надсилання..." : "Надіслати"}
        </button>
        <button
          onClick={() => {
            setIsEditing(false);
            setText(review.replyText ?? "");
          }}
          style={{
            padding: "6px 16px",
            borderRadius: 6,
            border: "1px solid #1F2937",
            background: "transparent",
            color: "#6B7280",
            fontSize: 12,
            cursor: "pointer",
          }}
        >
          Скасувати
        </button>
      </div>
    </div>
  );
}

export function CabinetReviews() {
  const [page, setPage] = useState(1);
  const { data, isLoading, isError, error } = useCabinetReviews(page, PAGE_SIZE);
  const { data: metrics } = useCabinetMetrics();

  if (isLoading) {
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
        {[...Array(3)].map((_, i) => (
          <div key={i} style={{ height: 80, background: "#111827", borderRadius: 10 }} />
        ))}
      </div>
    );
  }

  if (isError) {
    return (
      <div style={{ color: "#F87171", fontSize: 13 }}>
        Не вдалося завантажити відгуки.{" "}
        {error instanceof Error ? error.message : ""}
      </div>
    );
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <div>
      {/* Rating summary */}
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: "18px 24px",
          marginBottom: 20,
          display: "flex",
          alignItems: "center",
          gap: 14,
          flexWrap: "wrap",
        }}
      >
        <StarRating value={metrics?.rating ?? 0} size={18} />
        <span style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700 }}>
          {metrics?.rating != null ? metrics.rating.toFixed(1) : "—"}
        </span>
        <span style={{ color: "#6B7280", fontSize: 13 }}>
          {data?.total ?? 0} відгук(ів)
        </span>
      </div>

      {/* Stats widget */}
      <ReviewStatsWidget />

      {/* List */}
      {!data || data.items.length === 0 ? (
        <div
          style={{
            textAlign: "center",
            padding: "48px 0",
            color: "#4B5563",
            fontSize: 14,
            background: "#111827",
            border: "1px solid #1F2937",
            borderRadius: 12,
          }}
        >
          Відгуків ще немає.
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          {data.items.map((review) => (
            <div
              key={review.id}
              style={{
                background: "#111827",
                border: "1px solid #1F2937",
                borderRadius: 10,
                padding: "16px 20px",
                display: "flex",
                flexDirection: "column",
                gap: 8,
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
                <StarRating value={review.rating} size={14} />
                <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
                  {review.rating}/5
                </span>
                <span style={{ color: "#9CA3AF", fontSize: 12 }}>{review.reviewerName}</span>
                <span style={{ color: "#4B5563", fontSize: 12, marginLeft: "auto" }}>
                  {new Date(review.createdAt).toLocaleDateString("uk-UA")}
                </span>
              </div>
              {review.comment && (
                <p style={{ color: "#9CA3AF", fontSize: 13, margin: 0, lineHeight: 1.6 }}>
                  {review.comment}
                </p>
              )}
              <ReviewReplyBlock review={review} />
            </div>
          ))}
        </div>
      )}

      {/* Pagination */}
      {data && data.total > data.pageSize && (
        <div
          style={{
            display: "flex",
            justifyContent: "center",
            alignItems: "center",
            gap: 12,
            marginTop: 24,
          }}
        >
          <button
            disabled={page === 1}
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            style={{
              padding: "8px 18px",
              borderRadius: 8,
              border: "1px solid #1F2937",
              background: "transparent",
              color: page === 1 ? "#374151" : "#6B7280",
              fontSize: 13,
              cursor: page === 1 ? "not-allowed" : "pointer",
            }}
          >
            ← Попередня
          </button>
          <span style={{ color: "#4B5563", fontSize: 13 }}>
            Сторінка {page} з {totalPages}
          </span>
          <button
            disabled={page >= totalPages}
            onClick={() => setPage((p) => p + 1)}
            style={{
              padding: "8px 18px",
              borderRadius: 8,
              border: "1px solid #1F2937",
              background: "transparent",
              color: page >= totalPages ? "#374151" : "#6B7280",
              fontSize: 13,
              cursor: page >= totalPages ? "not-allowed" : "pointer",
            }}
          >
            Наступна →
          </button>
        </div>
      )}
    </div>
  );
}
