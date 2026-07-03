"use client";

import { useState } from "react";
import { useSupplierReviews } from "../hooks/useMarketplace";
import { StarRating } from "./StarRating";
import { ReviewModal } from "./ReviewModal";
import { reviewWord } from "../utils";
import { useMe } from "@/features/auth/hooks/useAuth";
import { TENANT_ROLES, hasRole } from "@/lib/roles";

interface Props {
  supplierId: string;
}

const PAGE_SIZE = 20;

export function SupplierReviewsTab({ supplierId }: Props) {
  const [showModal, setShowModal] = useState(false);
  const [page, setPage] = useState(1);
  const { data, isLoading, isError } = useSupplierReviews(supplierId, page, PAGE_SIZE);
  const { data: me } = useMe();

  // Only client tenants may leave reviews — the backend rejects provider team
  // (no tenant_id → 403) and supplier tenants / self-reviews (400), duplicates (409).
  const canReview = hasRole(me?.role, TENANT_ROLES);

  if (isLoading) {
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
        {[...Array(3)].map((_, i) => (
          <div
            key={i}
            style={{
              height: 80,
              background: "#111827",
              borderRadius: 10,
            }}
          />
        ))}
      </div>
    );
  }

  if (isError) {
    return (
      <div style={{ color: "#F87171", fontSize: 13, padding: "16px 0" }}>
        Не вдалося завантажити відгуки.
      </div>
    );
  }

  const total = data?.total ?? 0;
  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <div>
      {/* Header: count + leave review */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          gap: 12,
          marginBottom: 20,
          flexWrap: "wrap",
        }}
      >
        <span style={{ color: "#9CA3AF", fontSize: 13 }}>
          {total} {reviewWord(total)}
        </span>
        {canReview && (
          <button
            onClick={() => setShowModal(true)}
            style={{
              padding: "9px 20px",
              borderRadius: 8,
              border: "none",
              background: "#1D4ED8",
              color: "#E8EDF5",
              fontSize: 13,
              fontWeight: 600,
              cursor: "pointer",
            }}
          >
            Залишити відгук
          </button>
        )}
      </div>

      {/* Reviews list */}
      {!data || data.items.length === 0 ? (
        <div
          style={{
            textAlign: "center",
            padding: "40px 0",
            color: "#4B5563",
            fontSize: 14,
          }}
        >
          Відгуків ще немає{canReview ? " — будьте першим!" : "."}
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
                <span style={{ color: "#9CA3AF", fontSize: 12 }}>
                  {review.reviewerName}
                </span>
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

      {/* Review modal */}
      {showModal && (
        <ReviewModal supplierId={supplierId} onClose={() => setShowModal(false)} />
      )}
    </div>
  );
}
