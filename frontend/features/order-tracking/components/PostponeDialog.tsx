"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { usePostponeOrder } from "../hooks/useOrderTracking";
import type { TrackedOrder } from "../types";

function addDays(base: Date, days: number): string {
  const d = new Date(base);
  d.setDate(d.getDate() + days);
  return d.toISOString().slice(0, 10);
}

/**
 * "Відтермінувати доставку": moves the expected date of a delayed internal order. The order stays
 * open, so it keeps counting as "in transit" — the program will not suggest ordering it again,
 * it just knows the delivery is late (and until when).
 */
export function PostponeDialog({ order, onClose }: { order: TrackedOrder; onClose: () => void }) {
  const t = useTranslations("Dashboard.orderTracking.postpone");
  const postpone = usePostponeOrder();

  const tomorrow = addDays(new Date(), 1);
  const current = order.expectedAt && order.expectedAt > tomorrow ? order.expectedAt : null;
  const minDate = current ? addDays(new Date(current), 1) : tomorrow;
  const [date, setDate] = useState(() => addDays(new Date(current ?? new Date()), 3));
  const [reason, setReason] = useState("");

  const submit = () =>
    postpone.mutate(
      { id: order.id, newExpectedAt: date, reason: reason.trim() || undefined },
      {
        onSuccess: () => {
          toast.success(t("toastDone", { date: new Date(date).toLocaleDateString() }));
          onClose();
        },
        onError: (err) => toast.error(err.message),
      },
    );

  return (
    <div
      role="dialog"
      aria-modal="true"
      onClick={() => !postpone.isPending && onClose()}
      style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,0.6)", zIndex: 300, display: "flex", alignItems: "center", justifyContent: "center", padding: 16 }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12, width: "min(460px, 100%)", padding: 22 }}
      >
        <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: "0 0 6px" }}>{t("title")}</h2>
        <p style={{ color: "#6B7280", fontSize: 13, margin: "0 0 16px" }}>{t("body")}</p>

        <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, marginBottom: 4 }}>{t("newDate")}</label>
        <input
          type="date"
          value={date}
          min={minDate}
          onChange={(e) => setDate(e.target.value)}
          style={{ ...field, marginBottom: 14 }}
        />

        <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, marginBottom: 4 }}>{t("reason")}</label>
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          maxLength={500}
          rows={3}
          placeholder={t("reasonPlaceholder")}
          style={{ ...field, resize: "vertical", marginBottom: 18 }}
        />

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10 }}>
          <Btn variant="ghost" onClick={onClose} disabled={postpone.isPending}>{t("cancel")}</Btn>
          <Btn onClick={submit} disabled={postpone.isPending || !date || date < minDate}>
            {postpone.isPending ? t("saving") : t("confirm")}
          </Btn>
        </div>
      </div>
    </div>
  );
}

const field: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  background: "#0A0F1A",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px",
  outline: "none",
};
