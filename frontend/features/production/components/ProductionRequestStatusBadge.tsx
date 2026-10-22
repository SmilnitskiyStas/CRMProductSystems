"use client";

import type { CSSProperties } from "react";
import { useTranslations } from "next-intl";
import { StatusPill } from "@/components/ui/StatusPill";
import { inputStyle, labelStyle } from "@/components/ui/fieldStyles";
import type { ProductionRequestStatus, ProductionStage } from "../types";

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
  return <StatusPill bg={bg} text={color}>{t(status)}</StatusPill>;
}

/** Origin badge: auto-generated (buffer / promo) vs manual. Renders nothing for manual by default. */
export function RequestSourceBadge({ source, showManual = false }: { source: string; showManual?: boolean }) {
  const t = useTranslations("Dashboard.production.requests.source");
  if (source !== "auto_buffer" && source !== "auto_promo" && !showManual) return null;
  const auto = source === "auto_buffer" || source === "auto_promo";
  return <StatusPill bg={auto ? "#2E1065" : "#1F2937"} text={auto ? "#C4B5FD" : "#9CA3AF"}>{auto ? t(source) : t("manual")}</StatusPill>;
}

const STAGE_COLORS: Record<string, { bg: string; color: string }> = {
  awaiting_order: { bg: "#422006", color: "#FBBF24" },
  planned: { bg: "#1F2937", color: "#9CA3AF" },
  in_production: { bg: "#1E3A5F", color: "#60A5FA" },
  ready: { bg: "#064E3B", color: "#34D399" },
  shipped: { bg: "#064E3B", color: "#6EE7B7" },
};

/** Production pipeline stage chip (TASK-762). Renders nothing for "none"/unknown. */
export function RequestStageBadge({ stage }: { stage?: ProductionStage | null }) {
  const t = useTranslations("Dashboard.production.requests.stage");
  if (!stage || !STAGE_COLORS[stage]) return null;
  const { bg, color } = STAGE_COLORS[stage];
  return <StatusPill bg={bg} text={color}>{t(stage)}</StatusPill>;
}

export const reqInputStyle = inputStyle;

export const reqLabelStyle = labelStyle;

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
