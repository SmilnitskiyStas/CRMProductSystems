import type { FiscalStatus } from "../types";

const META: Record<FiscalStatus, { label: string; color: string; bg: string; border: string }> = {
  pending_fiscalization: {
    label: "Очікує",
    color: "#fbbf24",
    bg: "#fbbf2418",
    border: "#fbbf2440",
  },
  fiscalized: {
    label: "Фіскалізовано",
    color: "#22c55e",
    bg: "#22c55e18",
    border: "#22c55e40",
  },
  fiscalization_failed: {
    label: "Помилка",
    color: "#ef4444",
    bg: "#ef444418",
    border: "#ef444440",
  },
};

export function FiscalBadge({ status }: { status: FiscalStatus }) {
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
      {m.label}
    </span>
  );
}
