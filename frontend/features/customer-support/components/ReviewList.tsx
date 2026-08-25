"use client";

import { useState } from "react";
import { toast } from "sonner";
import { useTranslations, useLocale } from "next-intl";
import { StarRating } from "@/features/marketplace/components/StarRating";
import { useCustomerReviews, useReplyToReview } from "../hooks/useCustomerReviews";
import type { PurchaseReviewDto } from "../types";

const PAGE_SIZE = 30;
const RATINGS = [5, 4, 3, 2, 1];

function ReviewReplyBlock({ review }: { review: PurchaseReviewDto }) {
  const t = useTranslations("Dashboard.customerSupport.reviewList");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [isReplying, setIsReplying] = useState(false);
  const [text, setText] = useState("");
  const replyMutation = useReplyToReview();

  // ReviewService rejects a second reply with 409 (deliberate divergence from the supplier-review
  // cabinet flow, which allows silent overwrite) — the UI mirrors that: once ReplyText is set,
  // it's shown read-only, never re-opened for editing.
  if (review.replyText) {
    return (
      <div style={{ marginTop: 4, padding: "10px 14px", background: "#0B111C", border: "1px solid #1F2937", borderRadius: 8 }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 4 }}>
          <span style={{ color: "#60A5FA", fontSize: 12, fontWeight: 600 }}>{t("replyFromStaff")}</span>
          {review.repliedAt && (
            <span style={{ color: "#4B5563", fontSize: 11 }}>
              {new Date(review.repliedAt).toLocaleDateString(intlLocale)}
            </span>
          )}
        </div>
        <p style={{ color: "#9CA3AF", fontSize: 13, margin: 0, lineHeight: 1.6 }}>{review.replyText}</p>
      </div>
    );
  }

  if (!isReplying) {
    return (
      <button
        onClick={() => setIsReplying(true)}
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
        {t("reply")}
      </button>
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      <textarea
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder={t("replyPlaceholder")}
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
              {
                onSuccess: () => { toast.success(t("toastReplySent")); setIsReplying(false); },
                onError: (err) => toast.error(err.message),
              },
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
          {replyMutation.isPending ? t("sending") : t("send")}
        </button>
        <button
          onClick={() => { setIsReplying(false); setText(""); }}
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
          {t("cancel")}
        </button>
      </div>
    </div>
  );
}

export function ReviewList() {
  const t = useTranslations("Dashboard.customerSupport.reviewList");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [page, setPage] = useState(1);
  const [rating, setRating] = useState<number | "">("");

  const { data, isLoading } = useCustomerReviews({
    rating: rating || undefined,
    page,
    pageSize: PAGE_SIZE,
  });

  const reviews = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;
  const totalCount = data?.totalCount ?? 0;

  const selectStyle = {
    background: "#111827",
    border: "1px solid #1F2937",
    borderRadius: 6,
    color: "#9CA3AF",
    fontSize: 13,
    padding: "7px 10px",
    cursor: "pointer",
    outline: "none",
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      {/* Filter */}
      <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
        <select
          value={rating}
          onChange={(e) => { setRating(e.target.value ? Number(e.target.value) : ""); setPage(1); }}
          style={selectStyle}
        >
          <option value="">{t("allRatingsOption")}</option>
          {RATINGS.map((r) => (
            <option key={r} value={r}>{t("ratingOption", { rating: r })}</option>
          ))}
        </select>
      </div>

      {!isLoading && (
        <div style={{ color: "#4B5563", fontSize: 12 }}>{t("countLabel", { count: totalCount })}</div>
      )}

      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "20px 0" }}>{t("loading")}</div>
      ) : reviews.length === 0 ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "40px 0", textAlign: "center" }}>
          {t("empty")}
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          {reviews.map((review) => (
            <div
              key={review.id}
              style={{
                background: "#0A1020",
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
                <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{review.rating}/5</span>
                <span style={{ color: "#9CA3AF", fontSize: 12 }}>{review.consumerName}</span>
                <span style={{ color: "#4B5563", fontSize: 12, marginLeft: "auto" }}>
                  {new Date(review.createdAt).toLocaleDateString(intlLocale)}
                </span>
              </div>
              {review.comment && (
                <p style={{ color: "#9CA3AF", fontSize: 13, margin: 0, lineHeight: 1.6 }}>{review.comment}</p>
              )}
              <ReviewReplyBlock review={review} />
            </div>
          ))}
        </div>
      )}

      {totalPages > 1 && (
        <div style={{ display: "flex", justifyContent: "center", alignItems: "center", gap: 12, marginTop: 8 }}>
          <button
            disabled={page === 1}
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            style={{
              padding: "8px 18px", borderRadius: 8, border: "1px solid #1F2937",
              background: "transparent", color: page === 1 ? "#374151" : "#6B7280",
              fontSize: 13, cursor: page === 1 ? "not-allowed" : "pointer",
            }}
          >
            {t("prevPage")}
          </button>
          <span style={{ color: "#4B5563", fontSize: 13 }}>{t("pageOf", { page, total: totalPages })}</span>
          <button
            disabled={page >= totalPages}
            onClick={() => setPage((p) => p + 1)}
            style={{
              padding: "8px 18px", borderRadius: 8, border: "1px solid #1F2937",
              background: "transparent", color: page >= totalPages ? "#374151" : "#6B7280",
              fontSize: 13, cursor: page >= totalPages ? "not-allowed" : "pointer",
            }}
          >
            {t("nextPage")}
          </button>
        </div>
      )}
    </div>
  );
}
