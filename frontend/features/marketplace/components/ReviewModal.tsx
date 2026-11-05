"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useCreateReview } from "../hooks/useMarketplace";
import { StarRating } from "./StarRating";
import { ApiError } from "@/lib/api";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { textareaStyle } from "@/components/ui/fieldStyles";

interface Props {
  supplierId: string;
  onClose: () => void;
}

export function ReviewModal({ supplierId, onClose }: Props) {
  const t = useTranslations("Dashboard.marketplace.reviewModal");
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState("");
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const { mutate, isPending } = useCreateReview(supplierId);

  function handleSubmit() {
    if (rating === 0) {
      setErrorMsg(t("errorSelectRating"));
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
            setErrorMsg(t("errorAlreadyReviewed"));
          } else if (err instanceof ApiError && err.status === 400) {
            // v4.1 backend guards (TASK-285)
            if (err.message.includes("your own supplier")) {
              setErrorMsg(t("errorOwnProfile"));
            } else if (err.message.includes("Supplier tenants")) {
              setErrorMsg(t("errorSupplierCannotReview"));
            } else {
              setErrorMsg(err.message || t("errorInvalidDefault"));
            }
          } else {
            setErrorMsg(
              err instanceof Error ? err.message : t("errorSaveDefault")
            );
          }
        },
      }
    );
  }

  return (
    <Modal title={t("title")} onClose={onClose} width={440}>
      <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
        {/* Star selector */}
        <div>
          <div style={{ color: "#9CA3AF", fontSize: 13, marginBottom: 10 }}>
            {t("yourRatingLabel")}
          </div>
          <StarRating value={rating} size={28} interactive onChange={setRating} />
        </div>

        {/* Comment */}
        <div>
          <div style={{ color: "#9CA3AF", fontSize: 13, marginBottom: 8 }}>
            {t("commentLabel")}
          </div>
          <textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            rows={4}
            placeholder={t("commentPlaceholder")}
            style={textareaStyle}
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
          <Btn variant="ghost" onClick={onClose} disabled={isPending}>
            {t("cancel")}
          </Btn>
          <Btn onClick={handleSubmit} disabled={isPending || rating === 0}>
            {isPending ? t("saving") : t("submit")}
          </Btn>
        </div>
      </div>
    </Modal>
  );
}
