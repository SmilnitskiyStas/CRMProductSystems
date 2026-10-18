"use client";

import { useState } from "react";
import type { CSSProperties, ReactNode } from "react";
import { Copy, Check } from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import type { RegisterStatus } from "../types";

export const inputStyle: CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

export const labelStyle: CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

export const mutedText: CSSProperties = { color: "#6B7280", fontSize: 12 };

const STATUS_COLORS: Record<RegisterStatus, { bg: string; border: string; color: string }> = {
  active: { bg: "#1a3a2e", border: "#065F46", color: "#4ADE80" },
  pending: { bg: "#1D3461", border: "#3B82F6", color: "#93C5FD" },
  locked: { bg: "#3a2a0a", border: "#92400E", color: "#FBBF24" },
  revoked: { bg: "#2d0a0a", border: "#7F1D1D", color: "#F87171" },
};

export function StatusBadge({ status }: { status: RegisterStatus }) {
  const t = useTranslations("Dashboard.registers.status");
  const c = STATUS_COLORS[status] ?? STATUS_COLORS.pending;
  return (
    <span
      style={{
        display: "inline-block",
        padding: "2px 10px",
        borderRadius: 999,
        fontSize: 11,
        fontWeight: 600,
        background: c.bg,
        border: `1px solid ${c.border}`,
        color: c.color,
      }}
    >
      {t(status)}
    </span>
  );
}

export function formatDateTime(iso: string | null | undefined, locale?: string): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleString(locale, { dateStyle: "short", timeStyle: "short" });
}

export function formatMoney(value: number): string {
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/**
 * Shows a secret (activation code / PIN) exactly once. The value lives only in the caller's
 * state while this modal is open and is never persisted or refetched.
 */
export function SecretModal({
  title,
  secret,
  hint,
  warning,
  onClose,
}: {
  title: string;
  secret: string;
  hint?: ReactNode;
  warning?: ReactNode;
  onClose: () => void;
}) {
  const t = useTranslations("Dashboard.registers.secret");
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(secret);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      toast.error(t("copyFailed"));
    }
  }

  return (
    <Modal title={title} onClose={onClose} width={460}>
      {hint && <p style={{ color: "#9CA3AF", fontSize: 13, marginTop: 0 }}>{hint}</p>}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          gap: 12,
          background: "#0A0F1A",
          border: "1px solid #374151",
          borderRadius: 8,
          padding: "14px 16px",
          marginBottom: 14,
        }}
      >
        <code style={{ color: "#E8EDF5", fontSize: 22, letterSpacing: "0.12em", wordBreak: "break-all" }}>
          {secret}
        </code>
        <Btn size="sm" variant="ghost" icon={copied ? <Check size={13} /> : <Copy size={13} />} onClick={copy}>
          {copied ? t("copied") : t("copy")}
        </Btn>
      </div>
      <p style={{ color: "#FBBF24", fontSize: 12, margin: "0 0 6px" }}>{t("shownOnce")}</p>
      {warning && <p style={{ color: "#F87171", fontSize: 12, margin: "0 0 6px" }}>{warning}</p>}
      <div style={{ display: "flex", justifyContent: "flex-end", marginTop: 16 }}>
        <Btn onClick={onClose}>{t("close")}</Btn>
      </div>
    </Modal>
  );
}

export function ConfirmModal({
  title,
  text,
  confirmLabel,
  danger,
  isPending,
  onConfirm,
  onClose,
}: {
  title: string;
  text: ReactNode;
  confirmLabel: string;
  danger?: boolean;
  isPending?: boolean;
  onConfirm: () => void;
  onClose: () => void;
}) {
  const t = useTranslations("Dashboard.registers");
  return (
    <Modal title={title} onClose={onClose} width={440}>
      <p style={{ color: "#9CA3AF", fontSize: 13, marginTop: 0 }}>{text}</p>
      <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 18 }}>
        <Btn variant="ghost" onClick={onClose}>{t("cancel")}</Btn>
        <Btn variant={danger ? "danger" : "primary"} disabled={isPending} onClick={onConfirm}>
          {confirmLabel}
        </Btn>
      </div>
    </Modal>
  );
}
