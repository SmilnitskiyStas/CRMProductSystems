"use client";

import type { ShiftStatus } from "../types";
import { useMyShifts } from "../hooks/useSchedules";

function formatTime(time: string): string {
  return time.slice(0, 5);
}

function formatDate(dateStr: string): string {
  const d = new Date(dateStr);
  return d.toLocaleDateString("uk-UA", { weekday: "short", day: "2-digit", month: "2-digit" });
}

const STATUS_LABELS: Record<ShiftStatus, string> = {
  scheduled: "Заплановано",
  confirmed:  "Підтверджено",
  completed:  "Виконано",
  cancelled:  "Скасовано",
};

const STATUS_COLOR: Record<ShiftStatus, string> = {
  scheduled: "#60A5FA",
  confirmed:  "#4ADE80",
  completed:  "#6B7280",
  cancelled:  "#F87171",
};

/** Returns YYYY-MM-DD for a date offset by `days` from today. */
function isoDate(offsetDays: number): string {
  const d = new Date();
  d.setDate(d.getDate() + offsetDays);
  return d.toISOString().slice(0, 10);
}

export function MyShifts() {
  const from = isoDate(0);
  const to   = isoDate(6);

  const { data: shifts = [], isLoading } = useMyShifts(from, to);

  if (isLoading) {
    return <p style={{ color: "#4B5563", fontSize: 13, padding: "24px 0" }}>Завантаження…</p>;
  }

  if (shifts.length === 0) {
    return (
      <div
        style={{
          padding: "48px 0",
          textAlign: "center",
          color: "#4B5563",
          fontSize: 13,
        }}
      >
        Немає запланованих змін на поточний тиждень.
      </div>
    );
  }

  return (
    <div>
      <h3 style={{ color: "#9CA3AF", fontSize: 13, fontWeight: 600, marginBottom: 12 }}>
        Мій розклад — поточний тиждень
      </h3>
      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        {shifts.map((shift) => (
          <div
            key={shift.id}
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              padding: "12px 16px",
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              gap: 12,
            }}
          >
            <div>
              <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
                {formatDate(shift.shiftDate)}
              </div>
              <div style={{ color: "#9CA3AF", fontSize: 12, marginTop: 2 }}>
                {formatTime(shift.startTime)} – {formatTime(shift.endTime)}
                {shift.breakMinutes > 0 && (
                  <span style={{ color: "#4B5563", marginLeft: 8 }}>
                    перерва {shift.breakMinutes} хв
                  </span>
                )}
              </div>
              {shift.notes && (
                <div style={{ color: "#4B5563", fontSize: 11, marginTop: 4 }}>{shift.notes}</div>
              )}
            </div>
            <span style={{ color: STATUS_COLOR[shift.status], fontSize: 11, fontWeight: 600, whiteSpace: "nowrap" }}>
              {STATUS_LABELS[shift.status]}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}
