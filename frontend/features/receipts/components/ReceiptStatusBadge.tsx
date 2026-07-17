"use client";

import { useTranslations } from "next-intl";
import type { ReceiptStatus } from "../types";
import { RECEIPT_STATUS_COLOR } from "../types";

export function ReceiptStatusBadge({ status }: { status: ReceiptStatus }) {
  const t = useTranslations("Dashboard.receipts.status");
  const colors = RECEIPT_STATUS_COLOR[status] ?? RECEIPT_STATUS_COLOR.draft;
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 8px",
        borderRadius: 20,
        background: colors.bg,
        color: colors.text,
        fontSize: 11,
        fontWeight: 600,
      }}
    >
      {t.has(status) ? t(status) : status}
    </span>
  );
}
