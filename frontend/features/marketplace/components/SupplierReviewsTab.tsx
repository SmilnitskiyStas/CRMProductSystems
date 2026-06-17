"use client";

import { useState } from "react";
import { useSupplierReviews } from "../hooks/useMarketplace";
import { StarRating } from "./StarRating";
import { ReviewModal } from "./ReviewModal";

interface Props {
  supplierId: string;
}

export function SupplierReviewsTab({ supplierId }: Props) {
  const [showModal, setShowModal] = useState(false);
  const { data, isLoading, isError } = useSupplierReviews(supplierId);

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

  return (
    <div>
      {/* Leave review button */}
      <div style={{ display: "flex", justifyContent: "flex-end", marginBottom: 20 }}>
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
      </div>

      {/* Reviews list */}
      {!data || data.length === 0 ? (
        <div
          style={{
            textAlign: "center",
            padding: "40px 0",
            color: "#4B5563",
            fontSize: 14,
          }}
        >
          Відгуків ще немає — будьте першим!
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          {data.map((review) => (
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
              <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                <StarRating value={review.rating} size={14} />
                <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
                  {review.rating}/5
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

      {/* Review modal */}
      {showModal && (
        <ReviewModal supplierId={supplierId} onClose={() => setShowModal(false)} />
      )}
    </div>
  );
}
