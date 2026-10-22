"use client";

// Small confirm modal with an optional/required reason textarea (TASK-318).
// Used for order cancellation, request rejection and agreement termination.

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import { Btn } from "./Btn";
import { labelStyle, textareaStyle } from "./fieldStyles";

interface Props {
  title: string;
  /** Label above the textarea. Defaults to the translated "Reason" when omitted. */
  label?: string;
  placeholder?: string;
  confirmLabel?: string;
  /** When true the confirm button is disabled until a reason is entered. */
  required?: boolean;
  /** Confirm button variant — "danger" by default (reject/cancel flows). */
  variant?: "primary" | "danger" | "success";
  pending?: boolean;
  onConfirm: (reason: string) => void;
  onClose: () => void;
}

export function ReasonModal({
  title,
  label,
  placeholder,
  confirmLabel,
  required = false,
  variant = "danger",
  pending = false,
  onConfirm,
  onClose,
}: Props) {
  const t = useTranslations("Dashboard.ui.reasonModal");
  const tCommon = useTranslations("Common");
  const resolvedLabel = label ?? t("reasonLabel");
  const resolvedPlaceholder = placeholder ?? t("reasonPlaceholder");
  const resolvedConfirmLabel = confirmLabel ?? tCommon("confirm");
  const [reason, setReason] = useState("");

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.7)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
        padding: 20,
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          width: "100%",
          maxWidth: 440,
          padding: 20,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: 14,
          }}
        >
          <h3 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
            {title}
          </h3>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563", padding: 4 }}
          >
            <X size={18} />
          </button>
        </div>

        <label style={labelStyle}>
          {resolvedLabel}
          {required && <span style={{ color: "#F87171" }}> *</span>}
        </label>
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder={resolvedPlaceholder}
          rows={3}
          style={textareaStyle}
        />

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 16 }}>
          <Btn variant="ghost" onClick={onClose}>
            {tCommon("cancel")}
          </Btn>
          <Btn
            variant={variant}
            disabled={pending || (required && !reason.trim())}
            onClick={() => onConfirm(reason.trim())}
          >
            {pending ? t("pending") : resolvedConfirmLabel}
          </Btn>
        </div>
      </div>
    </div>
  );
}
