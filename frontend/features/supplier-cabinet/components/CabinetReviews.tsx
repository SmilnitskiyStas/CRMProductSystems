"use client";

import { useState } from "react";
import { StarRating } from "@/features/marketplace/components/StarRating";
import { useCabinetReviews, useCabinetMetrics } from "../hooks/useSupplierCabinet";

const PAGE_SIZE = 20;

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
    <div style={{ maxWidth: 720 }}>
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
