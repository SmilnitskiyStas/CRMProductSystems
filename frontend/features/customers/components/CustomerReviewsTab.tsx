"use client";

import { useTranslations, useLocale } from "next-intl";
import { StarRating } from "@/features/marketplace/components/StarRating";
import type { CustomerReviewPreview } from "../types";

interface Props {
  reviews: CustomerReviewPreview[];
}

/**
 * TASK-621 (§4 "Відгуки" tab). `recentReviews` is already included in the customer-detail
 * response (capped at 5, newest-first, never null) — no extra fetch. The full paged inbox with
 * reply capability is `/customer-support`'s Reviews tab (TASK-617's ReviewsInboxController); this
 * is a read-only preview mirroring `CabinetReviews.tsx`'s card layout.
 */
export function CustomerReviewsTab({ reviews }: Props) {
  const t = useTranslations("Dashboard.customers.reviews");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (reviews.length === 0) {
    return (
      <div style={{ color: "#374151", fontSize: 13, padding: "20px 0", textAlign: "center" }}>
        {t("empty")}
      </div>
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
      {reviews.map((review, i) => (
        <div
          key={i}
          style={{
            background: "#0A1020",
            border: "1px solid #1F2937",
            borderRadius: 10,
            padding: "14px 16px",
            display: "flex",
            flexDirection: "column",
            gap: 8,
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <StarRating value={review.rating} size={14} />
            <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{review.rating}/5</span>
            <span style={{ color: "#4B5563", fontSize: 12, marginLeft: "auto" }}>
              {new Date(review.createdAt).toLocaleDateString(intlLocale)}
            </span>
          </div>
          {review.comment && (
            <p style={{ color: "#9CA3AF", fontSize: 13, margin: 0, lineHeight: 1.5 }}>{review.comment}</p>
          )}
          {review.replyText && (
            <div
              style={{
                marginTop: 2,
                padding: "8px 12px",
                background: "#0D1521",
                border: "1px solid #1F2937",
                borderRadius: 8,
              }}
            >
              <div style={{ color: "#60A5FA", fontSize: 11, fontWeight: 600, marginBottom: 3 }}>
                {t("replyLabel")}
              </div>
              <div style={{ color: "#9CA3AF", fontSize: 12, lineHeight: 1.5 }}>{review.replyText}</div>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
