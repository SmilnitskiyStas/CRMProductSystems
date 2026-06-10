import type { ReceiptStatus } from "../types";
import { RECEIPT_STATUS_COLOR, RECEIPT_STATUS_LABEL } from "../types";

export function ReceiptStatusBadge({ status }: { status: ReceiptStatus }) {
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
      {RECEIPT_STATUS_LABEL[status] ?? status}
    </span>
  );
}
