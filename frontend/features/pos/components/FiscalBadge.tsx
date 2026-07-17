"use client";

import { useTranslations } from "next-intl";
import type { FiscalStatus } from "../types";

const META: Record<FiscalStatus, { color: string; bg: string; border: string }> = {
  pending_fiscalization: {
    color: "#fbbf24",
    bg: "#fbbf2418",
    border: "#fbbf2440",
  },
  fiscalized: {
    color: "#22c55e",
    bg: "#22c55e18",
    border: "#22c55e40",
  },
  fiscalization_failed: {
    color: "#ef4444",
    bg: "#ef444418",
    border: "#ef444440",
  },
};

export function FiscalBadge({ status }: { status: FiscalStatus }) {
  const t = useTranslations("Dashboard.pos.fiscalStatus");
  const m = META[status] ?? META.pending_fiscalization;
  return (
    <span
      style={{
        display: "inline-block",
        background: m.bg,
        border: `1px solid ${m.border}`,
        borderRadius: 5,
        color: m.color,
        fontSize: 11,
        fontWeight: 700,
        padding: "2px 8px",
        whiteSpace: "nowrap",
      }}
    >
      {t.has(status) ? t(status) : status}
    </span>
  );
}
