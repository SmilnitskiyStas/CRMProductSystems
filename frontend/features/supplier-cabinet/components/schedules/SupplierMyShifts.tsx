"use client";

import { useTranslations, useLocale } from "next-intl";
import type { ShiftStatus } from "@/features/schedules/types";
import { useSupplierMyShifts } from "../../hooks/useSupplierSchedules";

// Supplier-portal expansion Phase 5 (plan D6). Fork of
// features/schedules/components/MyShifts.tsx — same layout, reads the supplier
// cabinet my-shifts endpoint. Any supplier_admin staff member can open this.

function formatTime(time: string): string {
  return time.slice(0, 5);
}

function formatDate(dateStr: string, locale: string): string {
  const d = new Date(dateStr);
  return d.toLocaleDateString(locale, { weekday: "short", day: "2-digit", month: "2-digit" });
}

const STATUS_COLOR: Record<ShiftStatus, string> = {
  scheduled: "#60A5FA",
  confirmed:  "#4ADE80",
  completed:  "#6B7280",
  cancelled:  "#F87171",
};

function isoDate(offsetDays: number): string {
  const d = new Date();
  d.setDate(d.getDate() + offsetDays);
  return d.toISOString().slice(0, 10);
}

export function SupplierMyShifts() {
  const t = useTranslations("Dashboard.schedules.myShifts");
  const tShiftStatus = useTranslations("Dashboard.schedules.shiftStatus");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const from = isoDate(0);
  const to   = isoDate(6);

  const { data: shifts = [], isLoading } = useSupplierMyShifts(from, to);

  if (isLoading) {
    return <p style={{ color: "#4B5563", fontSize: 13, padding: "24px 0" }}>{t("loading")}</p>;
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
        {t("empty")}
      </div>
    );
  }

  return (
    <div>
      <h3 style={{ color: "#9CA3AF", fontSize: 13, fontWeight: 600, marginBottom: 12 }}>
        {t("title")}
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
                {formatDate(shift.shiftDate, intlLocale)}
              </div>
              <div style={{ color: "#9CA3AF", fontSize: 12, marginTop: 2 }}>
                {formatTime(shift.startTime)} – {formatTime(shift.endTime)}
                {shift.breakMinutes > 0 && (
                  <span style={{ color: "#4B5563", marginLeft: 8 }}>
                    {t("breakSuffix", { minutes: shift.breakMinutes })}
                  </span>
                )}
              </div>
              {shift.notes && (
                <div style={{ color: "#4B5563", fontSize: 11, marginTop: 4 }}>{shift.notes}</div>
              )}
            </div>
            <span style={{ color: STATUS_COLOR[shift.status], fontSize: 11, fontWeight: 600, whiteSpace: "nowrap" }}>
              {tShiftStatus(shift.status)}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}
