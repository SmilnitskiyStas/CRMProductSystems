"use client";

import { useTranslations } from "next-intl";
import type { BatchStatus } from "../types";
import { STATUS_COLOR } from "../types";

interface Props {
  status: BatchStatus;
}

export function StatusBadge({ status }: Props) {
  const t = useTranslations("Dashboard.shelf.status");
  const colors = STATUS_COLOR[status] ?? STATUS_COLOR.safe;
  return (
    <span
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 5,
        padding: "3px 8px",
        borderRadius: 20,
        background: colors.bg,
        color: colors.text,
        fontSize: 11,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      <span
        style={{
          width: 6,
          height: 6,
          borderRadius: "50%",
          background: colors.dot,
          flexShrink: 0,
        }}
      />
      {t.has(status) ? t(status) : status}
    </span>
  );
}
