"use client";

import { useTranslations } from "next-intl";
import type { CSSProperties } from "react";
import type { ProductionRequestStatus } from "../types";

const COLORS: Record<ProductionRequestStatus, { bg: string; color: string }> = {
  draft: { bg: "#1F2937", color: "#9CA3AF" },
  submitted: { bg: "#1E3A5F", color: "#60A5FA" },
  accepted: { bg: "#064E3B", color: "#34D399" },
  partial: { bg: "#422006", color: "#FBBF24" },
  rejected: { bg: "#1F1010", color: "#F87171" },
  fulfilled: { bg: "#064E3B", color: "#6EE7B7" },
  cancelled: { bg: "#1F1010", color: "#9CA3AF" },
};

export function RequestStatusBadge({ status }: { status: ProductionRequestStatus }) {
  const t = useTranslations("Dashboard.production.requests.status");
  const { bg, color } = COLORS[status] ?? COLORS.draft;
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 10px",
        borderRadius: 12,
        fontSize: 12,
        fontWeight: 600,
        background: bg,
        color,
      }}
    >
      {t(status)}
    </span>
  );
}

/** Origin badge: auto-generated (buffer / promo) vs manual. Renders nothing for manual by default. */
export function RequestSourceBadge({ source, showManual = false }: { source: string; showManual?: boolean }) {
  const t = useTranslations("Dashboard.production.requests.source");
  if (source !== "auto_buffer" && source !== "auto_promo" && !showManual) return null;
  const auto = source === "auto_buffer" || source === "auto_promo";
  return (
    <span
      style={{
        display: "inline-block",
        padding: "2px 8px",
        borderRadius: 12,
        fontSize: 11,
        fontWeight: 600,
        background: auto ? "#2E1065" : "#1F2937",
        color: auto ? "#C4B5FD" : "#9CA3AF",
        whiteSpace: "nowrap",
      }}
    >
      {auto ? t(source) : t("manual")}
    </span>
  );
}

export const reqInputStyle: CSSProperties = {
  width: "100%",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px",
  outline: "none",
  boxSizing: "border-box",
};

export const reqLabelStyle: CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  marginBottom: 5,
};

export const reqErrorStyle: CSSProperties = {
  padding: "10px 14px",
  background: "#1F1010",
  border: "1px solid #7F1D1D",
  borderRadius: 8,
  color: "#F87171",
  fontSize: 13,
};

/** "yyyy-MM-dd" to localized date (parsed as a local date, no timezone shift). */
export function formatDateOnly(d: string | null | undefined, intlLocale: string) {
  if (!d) return "—";
  const [y, m, day] = d.split("-").map(Number);
  return new Date(y, m - 1, day).toLocaleDateString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

export function errMessage(err: unknown, fallback: string) {
  return err instanceof Error && err.message ? err.message : fallback;
}
