import type { TicketPriority } from "../types";
import { TICKET_PRIORITY_LABELS } from "../types";

interface Props {
  priority: TicketPriority;
}

const PRIORITY_STYLES: Record<TicketPriority, { background: string; color: string; border: string }> = {
  critical: { background: "#2D0A0A", color: "#FCA5A5", border: "#DC2626" },
  high:     { background: "#1C100A", color: "#FDBA74", border: "#C2410C" },
  medium:   { background: "#1C1A07", color: "#FCD34D", border: "#B45309" },
  low:      { background: "#052E16", color: "#86EFAC", border: "#15803D" },
};

export function PriorityBadge({ priority }: Props) {
  const s = PRIORITY_STYLES[priority];
  return (
    <span
      style={{
        display: "inline-flex",
        alignItems: "center",
        padding: "2px 8px",
        borderRadius: 9999,
        fontSize: 11,
        fontWeight: 600,
        background: s.background,
        color: s.color,
        border: `1px solid ${s.border}`,
        whiteSpace: "nowrap",
      }}
    >
      {TICKET_PRIORITY_LABELS[priority]}
    </span>
  );
}
