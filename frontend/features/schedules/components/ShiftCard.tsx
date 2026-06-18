"use client";

import type { ScheduleShiftDto, ShiftStatus } from "../types";

const STATUS_COLORS: Record<ShiftStatus, { bg: string; border: string; text: string; strikethrough?: boolean }> = {
  scheduled:  { bg: "#0a1628", border: "#1D4ED8", text: "#60A5FA" },
  confirmed:  { bg: "#052e16", border: "#16A34A", text: "#4ADE80" },
  completed:  { bg: "#111827", border: "#374151", text: "#6B7280" },
  cancelled:  { bg: "#1c0a0a", border: "#991B1B", text: "#F87171", strikethrough: true },
};

function formatTime(time: string): string {
  return time.slice(0, 5);
}

interface Props {
  shift: ScheduleShiftDto;
  onClick: () => void;
}

export function ShiftCard({ shift, onClick }: Props) {
  const colors = STATUS_COLORS[shift.status];

  return (
    <button
      onClick={onClick}
      title={`${shift.userName} · ${formatTime(shift.startTime)}–${formatTime(shift.endTime)}${shift.notes ? `\n${shift.notes}` : ""}`}
      style={{
        width: "100%",
        background: colors.bg,
        border: `1px solid ${colors.border}`,
        borderRadius: 6,
        padding: "4px 7px",
        color: colors.text,
        fontSize: 11,
        fontWeight: 500,
        cursor: "pointer",
        textAlign: "left",
        textDecoration: colors.strikethrough ? "line-through" : "none",
        lineHeight: 1.4,
        transition: "opacity 0.1s",
      }}
      onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.opacity = "0.8"; }}
      onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.opacity = "1"; }}
    >
      {formatTime(shift.startTime)}–{formatTime(shift.endTime)}
      {shift.roleOverride && (
        <span style={{ display: "block", color: "#9CA3AF", fontSize: 10 }}>
          {shift.roleOverride}
        </span>
      )}
    </button>
  );
}
